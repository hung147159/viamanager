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

        public async Task<List<PageSwitchResult>> ProcessViaPagesAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PageSwitchResult>();
            var page = session.Page;
            var context = session.Context;

            progress?.Report($"[1/4] Discovering managed pages for VIA {account.Uid}...");
            var candidates = await _discoveryService.DiscoverPagesAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                progress?.Report($"No pages discovered for VIA {account.Uid}.");
                _accountRepository.UpdateAccountPageCount(account.Uid, 0);
                return results;
            }

            // Exclude pages already assigned to other active VIAs in the same BM
            var assignedOther = _pageRepository.GetAssignedPageIdsInBM(account.BusinessManager, account.Uid);
            var eligibleCandidates = candidates.Where(c => !assignedOther.Contains(c.PageId)).ToList();

            AppLogger.Info("PageSessionService", $"Found {candidates.Count} total pages, {eligibleCandidates.Count} eligible after duplicate check.");
            progress?.Report($"[2/4] Found {eligibleCandidates.Count} eligible pages to process.");

            var verifiedPagesToPersist = new List<ManagedPage>();

            for (int i = 0; i < eligibleCandidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = eligibleCandidates[i];

                progress?.Report($"[3/4] Processing page {i + 1}/{eligibleCandidates.Count}: {candidate.Name} ({candidate.PageId})...");
                AppLogger.Info("PageSessionService", $"--- Processing Page {i + 1}/{eligibleCandidates.Count} [{candidate.PageId}] ---");

                try
                {
                    // Step A: Switch Profile
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

                    // Step B: Polling & Two-Signal Verification
                    // Check up to 5 times with 1.5s delay to allow Facebook context to settle
                    VerificationResult? verificationResult = null;
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                        verificationResult = await _verificationService.VerifyActivePageIdentityAsync(page, candidate.PageId, candidate.Name, cancellationToken).ConfigureAwait(false);
                        if (verificationResult.IsVerified)
                            break;
                    }

                    if (verificationResult == null || !verificationResult.IsVerified)
                    {
                        var failResult = PageSwitchResult.Failed(
                            candidate.PageId,
                            candidate.Name,
                            verificationResult?.Summary ?? "Failed two-signal identity verification.",
                            VerificationStatus.Failed,
                            verificationResult?.Signals
                        );
                        results.Add(failResult);
                        continue;
                    }

                    // Step C: Verbatim Browser Cookie Capture (ZERO MUTATION!)
                    var browserCookies = await context.CookiesAsync().ConfigureAwait(false);
                    string rawCookieString = CookieSerializer.SerializeVerbatim(browserCookies);

                    var snapshot = new PageSessionSnapshot(
                        pageId: candidate.PageId,
                        parentViaUid: account.Uid,
                        rawCookieHeader: rawCookieString,
                        cookies: browserCookies,
                        isVerified: true,
                        signals: verificationResult.Signals
                    );

                    var managedPage = new ManagedPage
                    {
                        PageId = candidate.PageId,
                        Name = candidate.Name,
                        ParentViaUid = account.Uid,
                        BusinessManager = account.BusinessManager,
                        VerificationStatus = VerificationStatus.Verified,
                        VerifiedAt = DateTime.UtcNow,
                        VerificationSignals = verificationResult.Signals.ToList(),
                        RawCookieSnapshot = rawCookieString
                    };

                    verifiedPagesToPersist.Add(managedPage);
                    results.Add(PageSwitchResult.Success(candidate.PageId, candidate.Name, snapshot, verificationResult.Signals));

                    AppLogger.Info("PageSessionService", $"Successfully verified and captured session for Page: {candidate.PageId}");
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
    }
}
