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
            var verifiedPagesToPersist = new List<ManagedPage>();
            var capturedIUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // =========================================================================
            // CHIẾN THUẬT 1: SWITCH TRỰC TIẾP TỪ MENU POPUP (NẾU KHÔNG CÓ EXPLICIT IDS)
            // =========================================================================
            if (explicitPageIds == null || explicitPageIds.Count == 0)
            {
                progress?.Report($"[1/4] Đang mở hộp thoại Switch Profile trên popup góc phải...");
                AppLogger.Info("PageSessionService", $"Attempting dynamic popup switch for VIA {account.Uid}");

                if (!page.Url.StartsWith("https://www.facebook.com", StringComparison.OrdinalIgnoreCase) || page.Url.Contains("/pages/"))
                {
                    try
                    {
                        await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                    }
                    catch { }
                }

                bool popupOpened = await _switchService.OpenProfileSwitcherDialogAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);
                if (popupOpened)
                {
                    progress?.Report($"[Popup Scan] Đang cuộn lazy load nạp danh sách Page trong popup...");
                    for (int scrollIdx = 0; scrollIdx < 5; scrollIdx++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await page.EvaluateAsync(@"() => {
                            let dialogs = Array.from(document.querySelectorAll('div[role=""dialog""]')).filter(d => d.offsetWidth > 0 && d.offsetHeight > 0);
                            for (let d of dialogs) {
                                let scrollers = d.querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                                for (let s of scrollers) s.scrollTop = s.scrollHeight;
                            }
                        }").ConfigureAwait(false);
                        await Task.Delay(600, cancellationToken).ConfigureAwait(false);
                    }

                    await page.EvaluateAsync(@"() => {
                        let dialogs = Array.from(document.querySelectorAll('div[role=""dialog""]')).filter(d => d.offsetWidth > 0 && d.offsetHeight > 0);
                        for (let d of dialogs) {
                            let scrollers = d.querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                            for (let s of scrollers) s.scrollTop = 0;
                        }
                    }").ConfigureAwait(false);
                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);

                    var initialElements = await _switchService.GetDialogProfileElementsAsync(page, cancellationToken).ConfigureAwait(false);
                    int totalElements = initialElements.Count;
                    AppLogger.Info("PageSessionService", $"[POPUP SCAN] Found {totalElements} elements in switcher popup.");

                    if (totalElements > 1)
                    {
                        progress?.Report($"[2/4] Phát hiện {totalElements - 1} Page trong Popup. Bắt đầu switch trực tiếp...");

                        for (int i = 1; i < totalElements; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress?.Report($"[Popup Switch] Đang xử lý Page thứ {i}/{totalElements - 1}...");

                            try
                            {
                                if (i > 1)
                                {
                                    try
                                    {
                                        await context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = "i_user" }).ConfigureAwait(false);
                                    }
                                    catch { }

                                    await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

                                    bool isDialogReady = await _switchService.OpenProfileSwitcherDialogAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);
                                    if (!isDialogReady)
                                    {
                                        await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                                        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                                        isDialogReady = await _switchService.OpenProfileSwitcherDialogAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);
                                    }

                                    if (!isDialogReady)
                                    {
                                        AppLogger.Warn("PageSessionService", $"Không thể mở lại dialog ở lượt {i}. Bỏ qua.");
                                        continue;
                                    }

                                    await page.EvaluateAsync(@"() => {
                                        let dialogs = Array.from(document.querySelectorAll('div[role=""dialog""]')).filter(d => d.offsetWidth > 0 && d.offsetHeight > 0);
                                        for (let d of dialogs) {
                                            let scrollers = d.querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                                            for (let s of scrollers) s.scrollTop = s.scrollHeight;
                                        }
                                    }").ConfigureAwait(false);
                                    await Task.Delay(600, cancellationToken).ConfigureAwait(false);
                                    await page.EvaluateAsync(@"() => {
                                        let dialogs = Array.from(document.querySelectorAll('div[role=""dialog""]')).filter(d => d.offsetWidth > 0 && d.offsetHeight > 0);
                                        for (let d of dialogs) {
                                            let scrollers = d.querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                                            for (let s of scrollers) s.scrollTop = 0;
                                        }
                                    }").ConfigureAwait(false);
                                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                                }

                                var currentElements = await _switchService.GetDialogProfileElementsAsync(page, cancellationToken).ConfigureAwait(false);
                                if (i >= currentElements.Count)
                                {
                                    AppLogger.Warn("PageSessionService", $"Số phần tử sau khi mở lại popup chỉ còn {currentElements.Count}, thiếu chỉ số {i}.");
                                    continue;
                                }

                                var targetElement = currentElements[i];
                                string rawText = (await targetElement.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                                string targetPageName = _switchService.ExtractCleanPageName(rawText);
                                if (string.IsNullOrWhiteSpace(targetPageName))
                                {
                                    targetPageName = $"Mục {i}";
                                }

                                AppLogger.Info("PageSessionService", $"[POPUP ITEM {i}/{totalElements - 1}] Bóc tách: Name='{targetPageName}' | Text='{rawText.Replace("\r", " ").Replace("\n", " | ")}'");

                                if (!_switchService.IsValidPageItem(rawText))
                                {
                                    AppLogger.Info("PageSessionService", $"Bỏ qua mục {i} do chứa text rác/thông báo.");
                                    continue;
                                }

                                var cookiesBefore = await context.CookiesAsync().ConfigureAwait(false);

                                bool clicked = await _switchService.SwitchToElementAsync(page, targetElement, cancellationToken).ConfigureAwait(false);
                                if (!clicked)
                                {
                                    results.Add(PageSwitchResult.Failed($"popup_item_{i}", targetPageName, "Click phần tử trong popup thất bại."));
                                    continue;
                                }

                                // Chờ và xác nhận identity bằng Cookie i_user / c_user
                                string? newIUser = null;
                                for (int poll = 0; poll < 15; poll++)
                                {
                                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                                    var currentCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                                    var iUserCookie = currentCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                                    if (iUserCookie != null && !iUserCookie.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newIUser = iUserCookie.Value.Trim();
                                        break;
                                    }

                                    var cUserCookie = currentCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                                    if (cUserCookie != null && !cUserCookie.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newIUser = cUserCookie.Value.Trim();
                                        break;
                                    }
                                }

                                if (string.IsNullOrEmpty(newIUser))
                                {
                                    AppLogger.Warn("PageSessionService", $"Không phát hiện cookie i_user/c_user Page mới sau khi switch mục {i} ({targetPageName}).");
                                    results.Add(PageSwitchResult.Failed($"popup_item_{i}", targetPageName, "Không phát hiện cookie phiên Page mới."));
                                    continue;
                                }

                                if (capturedIUsers.Contains(newIUser))
                                {
                                    AppLogger.Info("PageSessionService", $"[TRÙNG LẶP] Page {newIUser} đã lấy trước đó. Bỏ qua.");
                                    continue;
                                }

                                capturedIUsers.Add(newIUser);

                                var verificationResult = await _verificationService.VerifyActivePageIdentityAsync(page, newIUser, targetPageName, cancellationToken).ConfigureAwait(false);

                                var browserCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                                if (browserCookies == null || browserCookies.Count == 0)
                                {
                                    browserCookies = await context.CookiesAsync().ConfigureAwait(false);
                                }

                                string rawCookieString = CookieSerializer.SerializeVerbatim(browserCookies);

                                var snapshot = new PageSessionSnapshot(
                                    pageId: newIUser,
                                    parentViaUid: account.Uid,
                                    rawCookieHeader: rawCookieString,
                                    cookies: browserCookies,
                                    isVerified: true,
                                    signals: verificationResult.Signals
                                );

                                var managedPage = new ManagedPage
                                {
                                    PageId = newIUser,
                                    Name = targetPageName,
                                    ParentViaUid = account.Uid,
                                    BusinessManager = account.BusinessManager,
                                    VerificationStatus = VerificationStatus.Verified,
                                    VerifiedAt = DateTime.UtcNow,
                                    VerificationSignals = verificationResult.Signals.ToList(),
                                    RawCookieSnapshot = rawCookieString
                                };

                                verifiedPagesToPersist.Add(managedPage);
                                results.Add(PageSwitchResult.Success(newIUser, targetPageName, snapshot, verificationResult.Signals));

                                progress?.Report($"[Popup Success] Đã switch thành công Page: {newIUser} ('{targetPageName}')");
                                AppLogger.Info("PageSessionService", $"[POPUP SUCCESS] Switch Page {newIUser} ('{targetPageName}') successfully.");
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Error("PageSessionService", $"Lỗi xử lý popup mục {i}: {ex.Message}", ex);
                            }
                        }
                    }
                }
            }

            // =========================================================================
            // CHIẾN THUẬT 2: CƠ CHẾ DỰ PHÒNG TẠI TRANG /PAGES (NẾU POPUP CÓ 0 PAGE HOẶC CÓ EXPLICIT IDS)
            // =========================================================================
            if (verifiedPagesToPersist.Count == 0)
            {
                List<DiscoveredPageCandidate> candidates;
                if (explicitPageIds != null && explicitPageIds.Count > 0)
                {
                    candidates = explicitPageIds
                        .Where(pid => !string.IsNullOrWhiteSpace(pid))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(pid => new DiscoveredPageCandidate(pid.Trim(), pid.Trim()))
                        .ToList();
                    AppLogger.Info("PageSessionService", $"Using {candidates.Count} explicit Page IDs provided.");
                }
                else
                {
                    progress?.Report($"[Dự phòng /pages] Bắt đầu quét danh sách Page qua trang quản lý /pages...");
                    candidates = await _discoveryService.DiscoverPagesAsync(page, account.Uid, cancellationToken).ConfigureAwait(false);
                }

                // Tuyệt đối CHỈ LẤY các ID có độ dài 14-15 số và BẮT ĐẦU BẰNG 615... Cấm các ID 18 chữ số (đầu 122...) và Workspace ID (đầu 179...)
                var eligibleCandidates = candidates
                    .Where(c => !string.IsNullOrWhiteSpace(c.PageId) && !c.PageId.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                    .Where(c => !c.PageId.StartsWith("122") && !c.PageId.StartsWith("179"))
                    .Where(c => (c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15) && c.PageId.All(char.IsDigit))
                             || (c.PageId.Length >= 10 && c.PageId.Length <= 16 && c.PageId.All(char.IsDigit)))
                    .GroupBy(c => c.PageId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                int count615 = eligibleCandidates.Count(c => c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15));
                if (count615 > 0)
                {
                    AppLogger.Info("PageSessionService", $"Detected {count615} Profile IDs (615... 14-15 digits). Filtering to retain only 615... Profile IDs.");
                    eligibleCandidates = eligibleCandidates.Where(c => c.PageId.StartsWith("615") && (c.PageId.Length == 14 || c.PageId.Length == 15)).ToList();
                }

                AppLogger.Info("PageSessionService", $"Found {eligibleCandidates.Count} unique pages to process via /pages for VIA {account.Uid}.");
                progress?.Report($"[Dự phòng /pages] Tìm thấy {eligibleCandidates.Count} Page hợp lệ.");

                for (int i = 0; i < eligibleCandidates.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = eligibleCandidates[i];

                    progress?.Report($"[Dự phòng /pages] Đang switch Page {i + 1}/{eligibleCandidates.Count}: {candidate.Name} ({candidate.PageId})...");
                    AppLogger.Info("PageSessionService", $"--- Fallback Switch Page {i + 1}/{eligibleCandidates.Count} [{candidate.PageId}] ---");

                    try
                    {
                        try
                        {
                            await context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = "i_user" }).ConfigureAwait(false);
                            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        }
                        catch { }

                        var cookiesBeforeSwitch = await context.CookiesAsync().ConfigureAwait(false);

                        bool switchSuccess = await _switchService.SwitchToPageAsync(page, candidate.PageId, cancellationToken).ConfigureAwait(false);
                        if (!switchSuccess)
                        {
                            results.Add(PageSwitchResult.Failed(candidate.PageId, candidate.Name, "Không tìm thấy hoặc không tương tác được nút Chuyển."));
                            continue;
                        }

                        // Polling & 2-Signal Verification
                        string resolvedPageId = candidate.PageId;
                        VerificationResult? verificationResult = null;

                        for (int attempt = 1; attempt <= 6; attempt++)
                        {
                            await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

                            var currentCheckCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                            var iUserCookie = currentCheckCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));

                            if (iUserCookie != null && !iUserCookie.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedPageId = iUserCookie.Value.Trim();
                                verificationResult = new VerificationResult(
                                    true,
                                    VerificationStatus.Verified,
                                    new[] { new VerificationSignal("CookieIdentityConfirmed", $"i_user xuất hiện ({resolvedPageId}) khác UID Via gốc.", DateTime.UtcNow, true, "Xác nhận thành công qua i_user") },
                                    $"Switch profile verified 100% via i_user={resolvedPageId}."
                                );
                                break;
                            }

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
                                break;
                            }
                        }

                        if (verificationResult == null || !verificationResult.IsVerified)
                        {
                            verificationResult = await _verificationService.VerifyActivePageIdentityAsync(page, candidate.PageId, candidate.Name, cancellationToken).ConfigureAwait(false);
                        }

                        if (!verificationResult.IsVerified)
                        {
                            results.Add(PageSwitchResult.Failed(
                                candidate.PageId,
                                candidate.Name,
                                "Không tìm thấy Cookie i_user của Page (trình duyệt chưa chuyển sang tư cách Page thành công).",
                                VerificationStatus.Failed,
                                verificationResult.Signals
                            ));
                            continue;
                        }

                        // Verbatim Browser Cookie Capture (ZERO MUTATION!)
                        var browserCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                        if (browserCookies == null || browserCookies.Count == 0)
                        {
                            browserCookies = await context.CookiesAsync().ConfigureAwait(false);
                        }
                        string rawCookieString = CookieSerializer.SerializeVerbatim(browserCookies);

                        string actualPageIdentifier = browserCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value))?.Value?.Trim()
                            ?? (browserCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !c.Value.Equals(account.Uid, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? resolvedPageId);

                        if (capturedIUsers.Contains(actualPageIdentifier))
                        {
                            AppLogger.Info("PageSessionService", $"[TRÙNG LẶP] Page {actualPageIdentifier} đã được thu thập. Bỏ qua duplicate.");
                            continue;
                        }

                        capturedIUsers.Add(actualPageIdentifier);

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
            }

            // Persist all verified pages atomically
            if (verifiedPagesToPersist.Count > 0)
            {
                progress?.Report($"[Lưu Database] Đang lưu {verifiedPagesToPersist.Count} Page vào SQLite...");
                _pageRepository.AssignPagesAtomic(account.BusinessManager, account.Uid, verifiedPagesToPersist);
                _accountRepository.UpdateAccountPageCount(account.Uid, verifiedPagesToPersist.Count);
            }
            else
            {
                _accountRepository.UpdateAccountPageCount(account.Uid, 0);
            }

            _accountRepository.UpdateLastRunTime(account.Uid, DateTime.UtcNow);
            progress?.Report($"Hoàn tất xử lý VIA {account.Uid}. Số Page hợp lệ: {verifiedPagesToPersist.Count}");

            return results;
        }
    }
}
