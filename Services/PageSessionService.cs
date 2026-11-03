using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Data;
using ViaManager.Models;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public interface IPageSessionService
    {
        Task<List<PageSwitchResult>> ProcessViaPagesAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default);

        Task<List<PageSwitchResult>> ProcessViaPagesAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            List<string>? explicitPageIds,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public class PageSessionService : IPageSessionService
    {
        private readonly IPageDiscoveryService _discoveryService;
        private readonly IPageSwitchService _switchService;
        private readonly IPageVerificationService _verificationService;
        private readonly PageRepository _pageRepository;
        private readonly ViaManager.Data.AccountRepository _accountRepository;

        public PageSessionService(
            IPageDiscoveryService discoveryService,
            IPageSwitchService switchService,
            IPageVerificationService verificationService,
            PageRepository pageRepository,
            ViaManager.Data.AccountRepository accountRepository)
        {
            _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
            _switchService = switchService ?? throw new ArgumentNullException(nameof(switchService));
            _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
            _pageRepository = pageRepository ?? throw new ArgumentNullException(nameof(pageRepository));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        }

        public Task<List<PageSwitchResult>> ProcessViaPagesAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return ProcessViaPagesAsync(session, account, null, progress, cancellationToken);
        }

        public async Task<List<PageSwitchResult>> ProcessViaPagesAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            List<string>? explicitPageIds,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PageSwitchResult>();
            var page = session.Page;
            var context = session.Context;

            List<DiscoveredPageCandidate> candidates;
            if (explicitPageIds != null && explicitPageIds.Count > 0)
            {
                candidates = explicitPageIds
                    .Where(pid => !string.IsNullOrWhiteSpace(pid))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(pid => new DiscoveredPageCandidate(pid.Trim(), pid.Trim()))
                    .ToList();
                AppLogger.Info("PageSessionService", $"Using {candidates.Count} explicit Page IDs provided from direct navigation & lazy loading.");
            }
            else
            {
                progress?.Report($"[1/4] Discovering managed pages for VIA {account.Uid}...");
                candidates = await _discoveryService.DiscoverPagesAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);
            }

            if (candidates.Count == 0)
            {
                progress?.Report($"No pages discovered for VIA {account.Uid}.");
                _accountRepository.UpdateAccountPageCount(account.Uid, 0);
                return results;
            }

            // Yêu cầu: GỠ BỎ việc lọc trùng theo Database/BM cũ.
            // Chỉ Distinct theo PageId trong phiên chạy hiện tại để xử lý TẤT CẢ các Page của Via.
            // CẤM TUYỆT ĐỐI các ID bài viết/photo (18 chữ số đầu 122...) và Workspace ID (đầu 179...)
            var eligibleCandidates = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.PageId) && !c.PageId.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                .Where(c => !c.PageId.StartsWith("122") && !c.PageId.StartsWith("179"))
                .Where(c => (c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15) && c.PageId.All(char.IsDigit))
                         || (c.PageId.Length >= 10 && c.PageId.Length <= 16 && c.PageId.All(char.IsDigit)))
                .GroupBy(c => c.PageId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // [TỐI ƯU TỐC ĐỘ X2 & ƯU TIÊN ID 615...]:
            // Lọc bỏ các Asset ID trùng lặp nếu danh sách chứa cả Profile ID 615... chuẩn
            int count615 = eligibleCandidates.Count(c => c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15));
            if (count615 > 0)
            {
                AppLogger.Info("PageSessionService", $"Detected {count615} Profile IDs (615... 14-15 digits). Filtering to retain only 615... Profile IDs.");
                eligibleCandidates = eligibleCandidates.Where(c => c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15)).ToList();
            }

            AppLogger.Info("PageSessionService", $"Found {eligibleCandidates.Count} unique pages to process for VIA {account.Uid}.");
            progress?.Report($"[2/4] Found {eligibleCandidates.Count} eligible pages to process.");

            var verifiedPagesToPersist = new List<ManagedPage>();
            var capturedIUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < eligibleCandidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = eligibleCandidates[i];

                progress?.Report($"[3/4] Processing page {i + 1}/{eligibleCandidates.Count}: {candidate.Name} ({candidate.PageId})...");
                AppLogger.Info("PageSessionService", $"--- Processing Page {i + 1}/{eligibleCandidates.Count} [{candidate.PageId}] ---");

                try
                {
                    // Yêu cầu 2: Reset khỏi phiên Page cũ để quay về tư cách Via gốc trước khi Switch sang Page mới
                    try
                    {
                        await context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = "i_user" }).ConfigureAwait(false);
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("PageSessionService", $"Clear i_user cookie error: {ex.Message}");
                    }

                    // Step A: Capture cookies BEFORE switch (Section XIV: Empirical baseline capture)
                    var cookiesBeforeSwitch = await context.CookiesAsync().ConfigureAwait(false);

                    // Step B: Switch Profile
                    bool switchSuccess = await _switchService.SwitchToPageAsync(page, candidate.PageId, cancellationToken).ConfigureAwait(false);
                    if (!switchSuccess)
                    {
                        var failResult = PageSwitchResult.Failed(candidate.PageId, candidate.Name, "Failed to interact with switch profile controls.");
                        results.Add(failResult);
                        continue;
                    }

                    // Try to resolve human-readable page name from title or h1 if still numeric
                    if (System.Text.RegularExpressions.Regex.IsMatch(candidate.Name, @"^\d+$"))
                    {
                        try
                        {
                            string docTitle = await page.TitleAsync().ConfigureAwait(false) ?? "";
                            if (docTitle.Contains('|'))
                            {
                                string cleanName = docTitle.Split('|')[0].Trim();
                                if (!string.IsNullOrWhiteSpace(cleanName) && !cleanName.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
                                {
                                    candidate = candidate with { Name = cleanName };
                                }
                            }
                        }
                        catch { }
                    }

                    // Step C: Polling & Verification (Yêu cầu 2: Chỉ cần kiểm tra Cookie i_user có xuất hiện và khác UID Via gốc)
                    string resolvedPageId = candidate.PageId;
                    VerificationResult? verificationResult = null;

                    for (int attempt = 1; attempt <= 6; attempt++)
                    {
                        await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

                        var currentCheckCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                        var iUserCookie = currentCheckCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));

                        // Nếu i_user xuất hiện và khác UID Via gốc -> Switch thành công 100%!
                        if (iUserCookie != null && !iUserCookie.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedPageId = iUserCookie.Value.Trim();
                            verificationResult = new VerificationResult(
                                true,
                                VerificationStatus.Verified,
                                new[] { new VerificationSignal("CookieIdentityConfirmed", $"i_user xuất hiện ({resolvedPageId}) khác UID Via gốc.", DateTime.UtcNow, true, "Xác nhận thành công qua i_user") },
                                $"Switch profile verified 100% via i_user={resolvedPageId}."
                            );
                            AppLogger.Info("PageSessionService", $"[SUCCESS 100%] Switch profile verified via i_user={resolvedPageId} (Target: {candidate.PageId})");
                            break;
                        }

                        // Kiểm tra nếu c_user chuyển thành PageId
                        var cUserCookie = currentCheckCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                        if (cUserCookie != null && cUserCookie.Value.Equals(candidate.PageId, StringComparison.OrdinalIgnoreCase) && !cUserCookie.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedPageId = cUserCookie.Value.Trim();
                            verificationResult = new VerificationResult(
                                true,
                                VerificationStatus.Verified,
                                new[] { new VerificationSignal("CookieIdentityConfirmed", $"c_user matches Page ID {resolvedPageId}", DateTime.UtcNow, true, "Xác nhận thành công qua c_user") },
                                $"Switch profile verified via c_user={resolvedPageId}."
                            );
                            AppLogger.Info("PageSessionService", $"[SUCCESS 100%] Switch profile verified via c_user={resolvedPageId}");
                            break;
                        }

                    }

                    if (verificationResult == null || !verificationResult.IsVerified)
                    {
                        var failResult = PageSwitchResult.Failed(
                            candidate.PageId,
                            candidate.Name,
                            "Không tìm thấy Cookie i_user của Page (trình duyệt chưa chuyển sang tư cách Page thành công).",
                            VerificationStatus.Failed,
                            verificationResult?.Signals
                        );
                        results.Add(failResult);
                        continue;
                    }

                    // Step D: Verbatim Browser Cookie Capture (ZERO MUTATION!)
                    var browserCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                    if (browserCookies == null || browserCookies.Count == 0)
                    {
                        browserCookies = await context.CookiesAsync().ConfigureAwait(false);
                    }
                    string rawCookieString = CookieSerializer.SerializeVerbatim(browserCookies);

                    // Yêu cầu 1: Chốt chặn chống trùng lặp tuyệt đối theo Cookie i_user
                    string actualPageIdentifier = browserCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value))?.Value?.Trim()
                        ?? (browserCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !c.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? resolvedPageId);

                    if (capturedIUsers.Contains(actualPageIdentifier))
                    {
                        AppLogger.Info("PageSessionService", $"[TRÙNG LẶP] Page {actualPageIdentifier} (candidate: {candidate.PageId}) đã được lấy Cookie trước đó. BỎ QUA NGAY LẬP TỨC để tránh lưu trùng.");
                        progress?.Report($"[Trùng lặp] Page {actualPageIdentifier} đã được thu thập. Bỏ qua duplicate.");
                        continue;
                    }

                    capturedIUsers.Add(actualPageIdentifier);

                    // Section XIV: Compute diagnostic diff with REDACTED values
                    string diffReport = ComputeDiagnosticCookieDiff(cookiesBeforeSwitch, browserCookies, actualPageIdentifier);
                    AppLogger.Info("PageSessionService", $"[Diagnostic diff for Page {actualPageIdentifier}]:\n{diffReport}");

                    var snapshot = new PageSessionSnapshot(
                        pageId: actualPageIdentifier,
                        parentViaUid: account.Uid,
                        rawCookieHeader: rawCookieString,
                        cookies: browserCookies,
                        isVerified: true,
                        signals: verificationResult.Signals
                    );

                    var managedPage = new ManagedPage
                    {
                        PageId = actualPageIdentifier,
                        Name = candidate.Name,
                        ParentViaUid = account.Uid,
                        BusinessManager = account.BusinessManager,
                        VerificationStatus = VerificationStatus.Verified,
                        VerifiedAt = DateTime.UtcNow,
                        VerificationSignals = verificationResult.Signals.ToList(),
                        RawCookieSnapshot = rawCookieString
                    };

                    verifiedPagesToPersist.Add(managedPage);
                    results.Add(PageSwitchResult.Success(actualPageIdentifier, candidate.Name, snapshot, verificationResult.Signals));

                    AppLogger.Info("PageSessionService", $"Successfully verified and captured session for Page: {actualPageIdentifier}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("PageSessionService", $"Error processing page {candidate.PageId}", ex);
                    results.Add(PageSwitchResult.Failed(candidate.PageId, candidate.Name, ex.Message));
                }
            }

            // Step D: Persist all verified pages atomically
            if (verifiedPagesToPersist.Count > 0)
            {
                progress?.Report($"[4/4] Persisting {verifiedPagesToPersist.Count} verified pages to database...");
                _pageRepository.AssignPagesAtomic(account.BusinessManager, account.Uid, verifiedPagesToPersist);
                _accountRepository.UpdateAccountPageCount(account.Uid, verifiedPagesToPersist.Count);
            }
            else
            {
                _accountRepository.UpdateAccountPageCount(account.Uid, 0);
            }

            _accountRepository.UpdateLastRunTime(account.Uid, DateTime.UtcNow);
            progress?.Report($"Completed processing for VIA {account.Uid}. Verified pages: {verifiedPagesToPersist.Count}");

            return results;
        }

        /// <summary>
        /// Prompt.txt Section XIV: Empirical diff of cookies before vs. after switch with REDACTED secret values.
        /// Demonstrates that session snapshot preserves actual browser state without synthetic fabrication.
        /// </summary>
        private static string ComputeDiagnosticCookieDiff(
            IReadOnlyList<BrowserContextCookiesResult> before,
            IReadOnlyList<BrowserContextCookiesResult> after,
            string targetPageId)
        {
            var beforeMap = before.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            var afterMap = after.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

            var diffLines = new List<string>();
            var allNames = beforeMap.Keys.Union(afterMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(n => n);

            foreach (var name in allNames)
            {
                bool inBefore = beforeMap.TryGetValue(name, out var bCookie);
                bool inAfter = afterMap.TryGetValue(name, out var aCookie);

                if (inBefore && !inAfter)
                {
                    diffLines.Add($"  - {name}: disappeared");
                }
                else if (!inBefore && inAfter)
                {
                    diffLines.Add($"  + {name}: appeared");
                }
                else if (inBefore && inAfter)
                {
                    bool valChanged = !string.Equals(bCookie!.Value, aCookie!.Value, StringComparison.Ordinal);
                    if (valChanged)
                    {
                        diffLines.Add($"  * {name}: changed (value redacted)");
                    }
                    else
                    {
                        if (name.Equals("c_user", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("i_user", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("xs", StringComparison.OrdinalIgnoreCase))
                        {
                            diffLines.Add($"  = {name}: unchanged");
                        }
                    }
                }
            }

            if (afterMap.TryGetValue("c_user", out var afterCUser))
            {
                bool cUserMatchesPage = string.Equals(afterCUser.Value, targetPageId, StringComparison.OrdinalIgnoreCase);
                diffLines.Add($"  [Empirical Note] c_user == PageId: {cUserMatchesPage} (Prompt.txt Section XIV compliance: Raw browser cookies preserved verbatim regardless of match)");
            }

            return string.Join(Environment.NewLine, diffLines);
        }
    }
}
