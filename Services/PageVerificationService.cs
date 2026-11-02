using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Models;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public record VerificationResult(
        bool IsVerified,
        VerificationStatus Status,
        IReadOnlyList<VerificationSignal> Signals,
        string Summary
    );

    public interface IPageVerificationService
    {
        Task<VerificationResult> VerifyActivePageIdentityAsync(
            IPage page,
            string expectedPageId,
            string? expectedPageName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pure evaluation function that verifies signals without live browser dependencies,
        /// ensuring 100% deterministic testability.
        /// </summary>
        VerificationResult EvaluateSignals(
            string expectedPageId,
            string? expectedPageName,
            IEnumerable<VerificationSignal> signals,
            string? currentCUser = null);
    }

    public class PageVerificationService : IPageVerificationService
    {
        public async Task<VerificationResult> VerifyActivePageIdentityAsync(
            IPage page,
            string expectedPageId,
            string? expectedPageName,
            CancellationToken cancellationToken = default)
        {
            AppLogger.Info("PageVerification", $"Verifying active Page identity for Page ID: {expectedPageId} (Expected Name: '{expectedPageName ?? ""}')");
            var detectedSignals = new List<VerificationSignal>();

            // Signal 1: Active Profile UI Identity (Header profile name, avatar aria-label, admin controls, meta data)
            bool signal1Satisfied = false;
            string signal1Details = "";
            try
            {
                // 1. Resolve effective page name from parameter or document title
                string docTitle = "";
                try { docTitle = await page.TitleAsync().ConfigureAwait(false) ?? ""; } catch { }

                string cleanTitleName = docTitle.Contains('|') ? docTitle.Split('|')[0].Trim() : docTitle.Trim();
                string effectiveName = !string.IsNullOrWhiteSpace(expectedPageName) && !System.Text.RegularExpressions.Regex.IsMatch(expectedPageName, @"^\d+$")
                    ? expectedPageName
                    : cleanTitleName;

                var identitySelectors = new[]
                {
                    "div[role='navigation'] div[aria-label*='profile']",
                    "div[role='navigation'] h1",
                    "div[role='banner'] div[aria-label*='profile']",
                    "div[role='banner'] svg[aria-label*='profile']",
                    "div[role='main'] h1",
                    "div[aria-label*='Your profile']",
                    "div[aria-label*='Trang cá nhân của bạn']"
                };

                foreach (var sel in identitySelectors)
                {
                    var el = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                    if (el != null)
                    {
                        string text = (await el.InnerTextAsync().ConfigureAwait(false) ?? "").Trim();
                        string aria = (await el.GetAttributeAsync("aria-label").ConfigureAwait(false) ?? "").Trim();

                        if (!string.IsNullOrEmpty(effectiveName) && !effectiveName.Equals("Facebook", StringComparison.OrdinalIgnoreCase) &&
                            (text.Contains(effectiveName, StringComparison.OrdinalIgnoreCase) ||
                             aria.Contains(effectiveName, StringComparison.OrdinalIgnoreCase)))
                        {
                            signal1Satisfied = true;
                            signal1Details = $"UI identity matched name '{effectiveName}' via selector '{sel}'.";
                            break;
                        }

                        if (text.Contains(expectedPageId, StringComparison.OrdinalIgnoreCase) ||
                            aria.Contains(expectedPageId, StringComparison.OrdinalIgnoreCase))
                        {
                            signal1Satisfied = true;
                            signal1Details = $"UI identity matched Page ID '{expectedPageId}' via selector '{sel}'.";
                            break;
                        }
                    }
                }

                // 2. Active Page Admin Persona UI verification:
                // Check if admin management controls are active AND the switch button is consumed
                if (!signal1Satisfied)
                {
                    var pageAdminControls = await page.QuerySelectorAsync(
                        "div[aria-label='Quản lý'], div[aria-label='Manage'], " +
                        "div[role='button']:has-text('Quản lý'), div[role='button']:has-text('Manage'), " +
                        "div:has-text('Bảng điều khiển chuyên nghiệp'), div:has-text('Professional dashboard'), " +
                        "div[aria-label='Chỉnh sửa trang cá nhân'], div[aria-label='Chỉnh sửa'], div[aria-label='Edit profile']"
                    ).ConfigureAwait(false);

                    var remainingSwitchBtn = await page.QuerySelectorAsync(
                        "div[role='button']:has-text('Switch now'), div[role='button']:has-text('Chuyển ngay'), div[aria-label='Switch'], div[aria-label='Chuyển']"
                    ).ConfigureAwait(false);

                    if (pageAdminControls != null && remainingSwitchBtn == null)
                    {
                        signal1Satisfied = true;
                        signal1Details = "Active Page Admin UI persona verified (Switch button consumed & admin management controls active).";
                    }
                }

                // 3. Metadata & Data Layer identity verification
                if (!signal1Satisfied)
                {
                    var metaMatch = await page.EvaluateAsync<bool>($@"
                        () => {{
                            try {{
                                const id = '{expectedPageId}';
                                const mId = document.querySelector('meta[name=""page_id""]')?.content || '';
                                const mAndroid = document.querySelector('meta[property=""al:android:url""]')?.content || '';
                                const canonical = document.querySelector('link[rel=""canonical""]')?.href || '';
                                if (mId === id || mAndroid.includes(id) || canonical.includes(id)) return true;

                                if (window.require) {{
                                    const cuid = window.require('CurrentUserInitialData');
                                    if (cuid && (cuid.ACCOUNT_ID === id || cuid.USER_ID === id)) return true;
                                }}
                                const headerLinks = Array.from(document.querySelectorAll('div[role=""banner""] a[href], div[role=""navigation""] a[href]'));
                                for (const l of headerLinks) {{
                                    if ((l.getAttribute('href') || '').includes(id)) return true;
                                }}
                            }} catch {{}}
                            return false;
                        }}
                    ").ConfigureAwait(false);

                    if (metaMatch)
                    {
                        signal1Satisfied = true;
                        signal1Details = $"Active Page identity verified via metadata / data-layer for Page ID '{expectedPageId}'.";
                    }
                }
            }
            catch (Exception ex)
            {
                signal1Details = $"Error evaluating Signal 1: {ex.Message}";
            }

            detectedSignals.Add(new VerificationSignal(
                SignalType: "ActiveProfileUIIdentity",
                Value: signal1Satisfied ? "MATCHED" : "UNMATCHED",
                Timestamp: DateTime.UtcNow,
                IsSatisfied: signal1Satisfied,
                Details: signal1Details
            ));

            // Signal 2: Navigation Target / URL Administrative Context
            bool signal2Satisfied = false;
            string signal2Details = "";
            try
            {
                string currentUrl = page.Url ?? "";

                // Administrative Page URLs on Facebook: /pages/... or /{PageId}/ or /professional_dashboard/ or meta business suite
                bool urlMatchesPage = currentUrl.Contains($"facebook.com/{expectedPageId}", StringComparison.OrdinalIgnoreCase) ||
                                      currentUrl.Contains($"/pages/{expectedPageId}", StringComparison.OrdinalIgnoreCase) ||
                                      currentUrl.Contains("professional_dashboard", StringComparison.OrdinalIgnoreCase) ||
                                      currentUrl.Contains("/settings/profile_name", StringComparison.OrdinalIgnoreCase);

                // Or check for Page-specific administrative DOM controls (e.g. "Manage Page", "Professional Dashboard", "Bảng điều khiển chuyên nghiệp")
                var adminControls = await page.QuerySelectorAsync(
                    "a[href*='professional_dashboard'], " +
                    "div[role='navigation'] a[href*='settings'], " +
                    "div:has-text('Professional dashboard'), " +
                    "div:has-text('Bảng điều khiển chuyên nghiệp'), " +
                    "div:has-text('Manage Page'), " +
                    "div:has-text('Quản lý trang')"
                ).ConfigureAwait(false);

                if (urlMatchesPage || adminControls != null)
                {
                    signal2Satisfied = true;
                    signal2Details = $"Context satisfied. URL matched ({urlMatchesPage}) or admin controls found ({adminControls != null}). URL: {currentUrl}";
                }
                else
                {
                    signal2Details = $"Context unsatisfied. URL: {currentUrl}";
                }
            }
            catch (Exception ex)
            {
                signal2Details = $"Error evaluating Signal 2: {ex.Message}";
            }

            detectedSignals.Add(new VerificationSignal(
                SignalType: "PageAdministrativeContext",
                Value: signal2Satisfied ? "MATCHED" : "UNMATCHED",
                Timestamp: DateTime.UtcNow,
                IsSatisfied: signal2Satisfied,
                Details: signal2Details
            ));

            // Extract c_user for audit log, but STRICTLY DO NOT rely on it for verification
            string cUserValue = "";
            try
            {
                var cookies = await page.Context.CookiesAsync().ConfigureAwait(false);
                cUserValue = cookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            }
            catch { }

            return EvaluateSignals(expectedPageId, expectedPageName, detectedSignals, cUserValue);
        }

        public VerificationResult EvaluateSignals(
            string expectedPageId,
            string? expectedPageName,
            IEnumerable<VerificationSignal> signals,
            string? currentCUser = null)
        {
            var signalList = signals.ToList();

            var signal1 = signalList.FirstOrDefault(s => s.SignalType == "ActiveProfileUIIdentity");
            var signal2 = signalList.FirstOrDefault(s => s.SignalType == "PageAdministrativeContext");

            bool s1 = signal1?.IsSatisfied ?? false;
            bool s2 = signal2?.IsSatisfied ?? false;

            // ABSOLUTE RULE CHECK:
            // Even if c_user == expectedPageId, we MUST REJECT if the two independent non-secret signals are NOT met.
            if (s1 && s2)
            {
                string summary = $"Verification PASSED. Signal 1 (UI Identity) and Signal 2 (Admin Context) both satisfied. (Observed c_user: '{currentCUser}')";
                AppLogger.Info("PageVerification", summary);
                return new VerificationResult(true, VerificationStatus.Verified, signalList, summary);
            }
            else
            {
                string failureReason = $"Verification REJECTED. Signal 1 (UI Identity): {s1}, Signal 2 (Admin Context): {s2}.";
                if (string.Equals(currentCUser, expectedPageId, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason += " [NOTE: c_user matched PageId, but switch verification is strictly rejected due to lack of 2 independent non-secret signals].";
                }
                AppLogger.Warn("PageVerification", failureReason);
                return new VerificationResult(false, VerificationStatus.Failed, signalList, failureReason);
            }
        }
    }
}
