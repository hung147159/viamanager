using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public interface IPageSwitchService
    {
        Task<bool> SwitchToPageAsync(IPage page, string pageId, CancellationToken cancellationToken = default);
        Task<bool> OpenProfileSwitcherDialogAsync(IPage page, string viaUid, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ILocator>> GetDialogProfileElementsAsync(IPage page, CancellationToken cancellationToken = default);
        Task<bool> SwitchToElementAsync(IPage page, ILocator targetElement, CancellationToken cancellationToken = default);
        bool IsValidPageItem(string? text);
        string ExtractCleanPageName(string text);
    }

    public class PageSwitchService : IPageSwitchService
    {
        public bool IsValidPageItem(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.Trim();
            if (trimmed.Length > 120) return false;

            string lower = trimmed.ToLowerInvariant();
            string[] spamKeywords = new[]
            {
                "chưa đọc", "đăng nhập", "bình luận", "cập nhật",
                "thông báo trước đó", "tất cả thông báo", "unread", "comment",
                "nhắc đến bạn", "thích bài viết", "bày tỏ cảm xúc", "đã thích",
                "chia sẻ bài viết", "đã đăng", "vừa xong", "phút trước", "giờ trước",
                "ngày trước", "hôm qua", "tuần trước",
                "đánh dấu là đã đọc", "cài đặt thông báo",
                "cài đặt & quyền riêng tư", "trợ giúp & hỗ trợ", "màn hình & trợ năng",
                "đóng góp ý kiến", "đăng xuất", "log out",
                "settings & privacy", "help & support", "display & accessibility", "give feedback",
                "xem tất cả trang cá nhân", "see all profiles", "xem tất cả các trang cá nhân",
                "tạo trang", "create page", "quản lý trang",
                "đóng", "close", "quay lại", "back"
            };

            foreach (var spam in spamKeywords)
            {
                if (lower.Contains(spam)) return false;
            }

            if (trimmed.All(char.IsDigit)) return false;

            return true;
        }

        public string ExtractCleanPageName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l))
                            .ToList();
            return lines.Count > 0 ? lines[0] : text.Trim();
        }

        public async Task<bool> OpenProfileSwitcherDialogAsync(IPage page, string viaUid, CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Đóng bất kỳ popup Thông báo / Tin nhắn / Menu rác nào nếu đang mở
                var openDialogs = page.Locator("div[role='dialog']:visible");
                if (await openDialogs.CountAsync().ConfigureAwait(false) > 0)
                {
                    bool isNotification = await page.EvaluateAsync<bool>(@"() => {
                        const dialogs = document.querySelectorAll('div[role=""dialog""]');
                        for (const d of dialogs) {
                            const txt = (d.innerText || '').toLowerCase();
                            const aria = (d.getAttribute('aria-label') || '').toLowerCase();
                            if (aria.includes('thông báo') || aria.includes('notification') || aria.includes('tin nhắn') || aria.includes('messenger')) return true;
                            if (txt.includes('thông báo trước đó') || txt.includes('tất cả thông báo') || txt.includes('đánh dấu tất cả là đã đọc')) return true;
                        }
                        return false;
                    }").ConfigureAwait(false);

                    if (isNotification)
                    {
                        await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }
                }

                // 2. Kiểm tra xem hộp thoại Profile Switcher thực sự đã đang mở và có danh sách hay chưa:
                // Cần đảm bảo: KHÔNG PHẢI là menu gốc nhỏ (không chứa Cài đặt, Đăng xuất, Trợ giúp)
                // VÀ có nút Quay lại (Back) hoặc chứa các item radio profile
                bool alreadyInProfileSwitcher = await page.EvaluateAsync<bool>(@"() => {
                    const dialogs = document.querySelectorAll('div[role=""dialog""]');
                    for (const d of dialogs) {
                        const txt = (d.innerText || '').toLowerCase();
                        // Nếu vẫn chứa các nút menu hệ thống thì không phải Profile Switcher
                        if (txt.includes('cài đặt & quyền riêng tư') || txt.includes('settings & privacy') ||
                            txt.includes('trợ giúp & hỗ trợ') || txt.includes('help & support') ||
                            txt.includes('đăng xuất') || txt.includes('log out')) {
                            return false;
                        }
                        // Nếu có nút Quay lại hoặc phần tử role=radio
                        if (d.querySelector('div[role=""radio""], [role=""radio""]') ||
                            d.querySelector('div[aria-label*=""Quay lại""], div[aria-label*=""Back""], [aria-label*=""Quay lại""], [aria-label*=""Back""]')) {
                            return true;
                        }
                    }
                    return false;
                }").ConfigureAwait(false);

                if (alreadyInProfileSwitcher)
                {
                    var existingItems = await GetDialogProfileElementsAsync(page, cancellationToken).ConfigureAwait(false);
                    if (existingItems.Count > 1)
                    {
                        AppLogger.Info("PageSwitchService", "Hộp thoại Profile Switcher đã mở sẵn và chứa danh sách Page hợp lệ.");
                        return true;
                    }
                }

                // 3. Nếu menu nhỏ đang mở sẵn (có nút "Xem tất cả trang cá nhân"), bấm trực tiếp không cần click lại Avatar
                var seeAllExisting = page.Locator("div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='button']:has-text('See all profiles'):visible, div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible, span:has-text('Xem tất cả trang cá nhân'):visible, span:has-text('See all profiles'):visible").First;
                bool clickedSeeAllDirectly = false;
                if (await seeAllExisting.CountAsync().ConfigureAwait(false) > 0)
                {
                    try
                    {
                        await seeAllExisting.ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false);
                        clickedSeeAllDirectly = true;
                    }
                    catch
                    {
                        try
                        {
                            await seeAllExisting.EvaluateAsync("el => el.click()").ConfigureAwait(false);
                            clickedSeeAllDirectly = true;
                        }
                        catch { }
                    }
                }

                if (!clickedSeeAllDirectly)
                {
                    // 4. Bấm vào nút Avatar góc phải trên banner:
                    // Tuyệt đối KHÔNG click nhầm quả chuông (Thông báo) hoặc Messenger
                    bool avatarClicked = false;
                    var bannerButtons = page.Locator("div[role='banner'] div[role='button']:visible, div[role='banner'] [role='button']:visible");
                    int btnCount = await bannerButtons.CountAsync().ConfigureAwait(false);

                    for (int b = 0; b < btnCount; b++)
                    {
                        var btn = bannerButtons.Nth(b);
                        string aria = (await btn.GetAttributeAsync("aria-label").ConfigureAwait(false) ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(aria)) continue;

                        string ariaLower = aria.ToLowerInvariant();
                        // Bỏ qua tuyệt đối quả chuông (Thông báo), Messenger, Tin nhắn, Menu 9 chấm, Tìm kiếm
                        if (ariaLower.Contains("thông báo") || ariaLower.Contains("notification") ||
                            ariaLower.Contains("tin nhắn") || ariaLower.Contains("messenger") ||
                            ariaLower.Contains("menu") || ariaLower.Contains("tìm kiếm") || ariaLower.Contains("search"))
                        {
                            continue;
                        }

                        if (ariaLower.Contains("trang cá nhân") || ariaLower.Contains("your profile") ||
                            ariaLower.Contains("tài khoản") || ariaLower.Contains("account"))
                        {
                            try
                            {
                                await btn.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                                avatarClicked = true;
                                break;
                            }
                            catch { }
                        }
                    }

                    // Fallback nếu không khớp qua aria-label: Lấy nút góc phải ngoài cùng của banner (luôn là Avatar)
                    if (!avatarClicked)
                    {
                        avatarClicked = await page.EvaluateAsync<bool>(@"() => {
                            const banner = document.querySelector('div[role=""banner""]');
                            if (!banner) return false;
                            const buttons = Array.from(banner.querySelectorAll('div[role=""button""], [role=""button""]')).filter(b => {
                                const aria = (b.getAttribute('aria-label') || '').toLowerCase();
                                if (aria.includes('thông báo') || aria.includes('notification') || aria.includes('messenger') || aria.includes('tin nhắn') || aria.includes('menu') || aria.includes('tìm kiếm')) return false;
                                const rect = b.getBoundingClientRect();
                                return rect.width > 20 && rect.height > 20 && rect.top < 100 && rect.right > 0;
                            });
                            if (buttons.length > 0) {
                                buttons.sort((a, b) => b.getBoundingClientRect().right - a.getBoundingClientRect().right);
                                buttons[0].click();
                                return true;
                            }
                            return false;
                        }").ConfigureAwait(false);
                    }

                    if (!avatarClicked)
                    {
                        AppLogger.Warn("PageSwitchService", "Không thể click nút Avatar trên banner.");
                        return false;
                    }

                    // [BẮT BUỘC THEO QUY TẮC]: Ngay sau khi click Avatar góc phải, BẮT BUỘC phải chờ menu nhỏ hiện ra
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                    // [BẮT BUỘC THEO QUY TẮC]: Click nút "Xem tất cả trang cá nhân" (See all profiles)
                    var seeAllBtn = page.Locator("div[role='button']:has-text('Xem tất cả trang cá nhân'), div[role='button']:has-text('See all profiles'), div[role='button']:has-text('Xem tất cả các trang cá nhân'), span:has-text('Xem tất cả trang cá nhân'), span:has-text('See all profiles')").First;

                    try
                    {
                        await seeAllBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 4000 }).ConfigureAwait(false);
                    }
                    catch { }

                    if (await seeAllBtn.IsVisibleAsync().ConfigureAwait(false))
                    {
                        AppLogger.Info("PageSwitchService", "Đã tìm thấy nút 'Xem tất cả trang cá nhân'. Bắt đầu click...");
                        try
                        {
                            await seeAllBtn.ClickAsync(new() { Timeout = 4000 }).ConfigureAwait(false);
                        }
                        catch
                        {
                            await seeAllBtn.EvaluateAsync("el => el.click()").ConfigureAwait(false);
                        }
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        AppLogger.Warn("PageSwitchService", "Không tìm thấy nút 'Xem tất cả trang cá nhân' trong menu thả xuống.");
                    }
                }

                // 5. Chờ và xác nhận hộp thoại Profile Switcher lớn đã thực sự mở ra
                // Tuyệt đối không chấp nhận khi dialog vẫn là menu nhỏ (vẫn chứa Cài đặt / Trợ giúp / Đăng xuất)
                bool isProfileSwitcherReady = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 7000)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    bool isReady = await page.EvaluateAsync<bool>(@"() => {
                        const dialogs = document.querySelectorAll('div[role=""dialog""]');
                        for (const d of dialogs) {
                            const txt = (d.innerText || '').toLowerCase();
                            // Nếu vẫn chứa menu hệ thống thì chưa chuyển vào Profile Switcher
                            if (txt.includes('cài đặt & quyền riêng tư') || txt.includes('settings & privacy') ||
                                txt.includes('trợ giúp & hỗ trợ') || txt.includes('help & support') ||
                                txt.includes('đăng xuất') || txt.includes('log out')) {
                                continue;
                            }
                            // Kiểm tra dấu hiệu Profile Switcher: có nút Quay lại (Back) hoặc các nút radio profile
                            if (d.querySelector('div[role=""radio""], [role=""radio""]') ||
                                d.querySelector('div[aria-label*=""Quay lại""], div[aria-label*=""Back""], [aria-label*=""Quay lại""], [aria-label*=""Back""]')) {
                                return true;
                            }
                            // Hoặc có danh sách listitem bên trong dialog
                            const items = d.querySelectorAll('div[role=""listitem""], [role=""listitem""]');
                            if (items.length > 0) {
                                return true;
                            }
                        }
                        return false;
                    }").ConfigureAwait(false);

                    if (isReady)
                    {
                        isProfileSwitcherReady = true;
                        break;
                    }

                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                }

                if (!isProfileSwitcherReady)
                {
                    AppLogger.Warn("PageSwitchService", "Không thể vào hộp thoại Profile Switcher (vẫn bị kẹt ở menu nhỏ hoặc không tìm thấy danh sách Profile).");
                    return false;
                }

                // 6. Đưa vị trí cuộn về đỉnh (top = 0)
                await page.EvaluateAsync(@"() => {
                    let dialogs = Array.from(document.querySelectorAll('div[role=""dialog""]')).filter(d => d.offsetWidth > 0 && d.offsetHeight > 0);
                    for (let d of dialogs) {
                        let scrollers = d.querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                        for (let sc of scrollers) sc.scrollTop = 0;
                    }
                }").ConfigureAwait(false);

                AppLogger.Info("PageSwitchService", "Đã mở thành công hộp thoại Profile Switcher lớn.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PageSwitchService", $"Error opening Profile Switcher Dialog: {ex.Message}");
                return false;
            }
        }

        public async Task<IReadOnlyList<ILocator>> GetDialogProfileElementsAsync(IPage page, CancellationToken cancellationToken = default)
        {
            try
            {
                // Bảo vệ 1: Kiểm tra xem dialog hiện tại có phải là menu hệ thống nhỏ (chứa Cài đặt, Đăng xuất) hay không
                bool isRootMenu = await page.EvaluateAsync<bool>(@"() => {
                    const dialogs = document.querySelectorAll('div[role=""dialog""]');
                    for (const d of dialogs) {
                        const txt = (d.innerText || '').toLowerCase();
                        if (txt.includes('cài đặt & quyền riêng tư') || txt.includes('settings & privacy') ||
                            txt.includes('trợ giúp & hỗ trợ') || txt.includes('help & support') ||
                            txt.includes('đăng xuất') || txt.includes('log out')) {
                            return true;
                        }
                    }
                    return false;
                }").ConfigureAwait(false);

                if (isRootMenu)
                {
                    AppLogger.Warn("PageSwitchService", "GetDialogProfileElementsAsync: Dialog hiện tại là menu gốc nhỏ, không quét để tránh click nhầm menu hệ thống.");
                    return Array.Empty<ILocator>();
                }

                // Bảo vệ 2: Selector chuẩn: chỉ bắt các thẻ listitem chứa radio profile của hộp thoại Switcher
                var rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:has(div[role='radio']), div[role='dialog'] div[role='listitem']:has([role='radio'])").AllAsync().ConfigureAwait(false);
                if (rawElements.Count == 0)
                {
                    rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:has(div[role='button'])").AllAsync().ConfigureAwait(false);
                }
                if (rawElements.Count == 0)
                {
                    rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:visible").AllAsync().ConfigureAwait(false);
                }

                var validList = new List<ILocator>();
                for (int idx = 0; idx < rawElements.Count; idx++)
                {
                    var el = rawElements[idx];
                    try
                    {
                        string text = (await el.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                        if (IsValidPageItem(text))
                        {
                            validList.Add(el);
                        }
                    }
                    catch { }
                }

                return validList;
            }
            catch
            {
                return Array.Empty<ILocator>();
            }
        }

        public async Task<bool> SwitchToElementAsync(IPage page, ILocator targetElement, CancellationToken cancellationToken = default)
        {
            try
            {
                await targetElement.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);

                var clickable = targetElement.Locator("div[role='radio']:visible, div[role='button']:visible").Nth(0);
                if (await clickable.CountAsync().ConfigureAwait(false) > 0)
                {
                    await clickable.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                }
                else
                {
                    await targetElement.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                }

                await HandleConfirmModalIfPresentAsync(page, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                try
                {
                    await targetElement.EvaluateAsync(@"(el) => {
                        const target = el.querySelector('div[role=""radio""], div[role=""button""]') || el;
                        target.click();
                    }").ConfigureAwait(false);

                    await HandleConfirmModalIfPresentAsync(page, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("PageSwitchService", $"Failed to click target element: {ex.Message}");
                    return false;
                }
            }
        }

        public async Task<bool> SwitchToPageAsync(IPage page, string pageId, CancellationToken cancellationToken = default)
        {
            AppLogger.Info("PageSwitch", $"Initiating fallback switch profile to Page ID: {pageId}");

            try
            {
                string targetUrl = $"https://www.facebook.com/profile.php?id={pageId}";
                await page.GotoAsync(targetUrl, new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 25000
                }).ConfigureAwait(false);

                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

                var switchSelectors = new[]
                {
                    "div[aria-label='Chuyển']",
                    "div[role='button']:has-text('Chuyển ngay')",
                    "div[role='button']:has-text('Chuyển')",
                    "div[role='button']:has-text('Switch Now')",
                    "div[role='button']:has-text('Switch now')",
                    "div[role='button']:has-text('Switch')",
                    "div[aria-label='Switch']",
                    "div[aria-label*='Chuyển sang']",
                    "div[aria-label*='Switch to']",
                    "div[aria-label='Chuyển trang']",
                    "div[role='button']:has-text('Đổi')",
                    "a:has-text('Chuyển ngay')",
                    "a:has-text('Switch now')",
                    "div[role='banner'] div[role='button']:has-text('Chuyển')",
                    "div[role='banner'] div[role='button']:has-text('Switch')",
                    "div[role='main'] div[role='button']:has-text('Chuyển')",
                    "div[role='main'] div[role='button']:has-text('Switch')"
                };

                IElementHandle? switchBtn = null;
                string matchedSelector = string.Empty;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 8000)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var selector in switchSelectors)
                    {
                        var btn = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
                        if (btn != null && await btn.IsVisibleAsync().ConfigureAwait(false))
                        {
                            switchBtn = btn;
                            matchedSelector = selector;
                            break;
                        }
                    }

                    if (switchBtn != null)
                        break;

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }

                if (switchBtn != null)
                {
                    AppLogger.Info("PageSwitch", $"Found switch button with selector: {matchedSelector} after {sw.ElapsedMilliseconds}ms. Clicking...");

                    try
                    {
                        await Task.WhenAll(
                            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                            switchBtn.ClickAsync()
                        ).ConfigureAwait(false);
                    }
                    catch
                    {
                        await switchBtn.ClickAsync().ConfigureAwait(false);
                    }

                    await HandleConfirmModalIfPresentAsync(page, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    string warnMsg = $"[WARNING] [PageSwitch] Không tìm thấy nút Chuyển trên trang {pageId} (URL: {page.Url})";
                    AppLogger.Warn("PageSwitch", warnMsg);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PageSwitch", $"Direct page navigation switch failed for {pageId}: {ex.Message}");
            }

            return false;
        }

        private static async Task HandleConfirmModalIfPresentAsync(IPage page, CancellationToken cancellationToken = default)
        {
            try
            {
                for (int wait = 0; wait < 4; wait++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(600, cancellationToken).ConfigureAwait(false);

                    bool confirmClicked = await page.EvaluateAsync<bool>(@"() => {
                        const dialogs = document.querySelectorAll('div[role=""dialog""]');
                        for (let d = dialogs.length - 1; d >= 0; d--) {
                            const buttons = dialogs[d].querySelectorAll('div[role=""button""], button');
                            for (const btn of buttons) {
                                const t = (btn.innerText || '').trim();
                                const aria = (btn.getAttribute('aria-label') || '').trim();
                                if (/^(Chuyển|Switch|Chuyển ngay|Switch now|Chuyển sang|Đổi|Xác nhận)$/i.test(t) ||
                                    /^(Chuyển|Switch|Chuyển ngay|Switch now)$/i.test(aria)) {
                                    btn.click();
                                    return true;
                                }
                            }
                        }
                        return false;
                    }").ConfigureAwait(false);

                    if (confirmClicked)
                    {
                        AppLogger.Info("PageSwitch", "Confirmed switch modal interaction.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("PageSwitch", $"Confirm modal interaction completed: {ex.Message}");
            }
        }
    }
}
