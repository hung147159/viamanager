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
                // Đóng bất kỳ popup Thông báo / Tin nhắn / Menu rác nào nếu đang mở
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

                // Kiểm tra xem hộp thoại Profile Switcher div[role='dialog'] đã đang mở và có sẵn danh sách hay chưa
                var existingDialog = page.Locator("div[role='dialog']:visible");
                if (await existingDialog.CountAsync().ConfigureAwait(false) > 0)
                {
                    var seeAllInside = page.Locator("div[role='dialog'] div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='dialog'] div[role='button']:has-text('See all profiles'):visible, div[role='dialog'] div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible");
                    if (await seeAllInside.CountAsync().ConfigureAwait(false) > 0)
                    {
                        try { await seeAllInside.Nth(0).ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false); } catch { }
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }

                    var existingItems = await GetDialogProfileElementsAsync(page, cancellationToken).ConfigureAwait(false);
                    if (existingItems.Count > 1)
                    {
                        return true;
                    }
                }

                // Định vị chính xác nút Avatar người dùng trên banner:
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

                // Fallback: Nếu không match được qua aria-label tiếng Việt/Anh, lấy nút góc phải ngoài cùng của banner (luôn là Avatar)
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
                    return false;
                }

                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

                // Click vào nút "Xem tất cả trang cá nhân" / "See all profiles" nếu có
                var seeAllProfilesBtn = page.Locator("div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='button']:has-text('See all profiles'):visible, div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible, span:has-text('Xem tất cả trang cá nhân'):visible, span:has-text('See all profiles'):visible");
                if (await seeAllProfilesBtn.CountAsync().ConfigureAwait(false) > 0)
                {
                    try
                    {
                        await seeAllProfilesBtn.Nth(0).ClickAsync(new() { Timeout = 4000 }).ConfigureAwait(false);
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }
                    catch { }
                }

                // Chờ div[role='dialog'] xuất hiện (tối đa 5s)
                var dialog = await page.WaitForSelectorAsync("div[role='dialog']:visible", new() { Timeout = 5000 }).ConfigureAwait(false);
                if (dialog != null)
                {
                    // Kiểm tra dialog vừa mở có phải bảng Thông báo hay không; nếu là Thông báo thì đóng ngay
                    bool isNotification = await page.EvaluateAsync<bool>(@"() => {
                        const dialogs = document.querySelectorAll('div[role=""dialog""]');
                        for (const d of dialogs) {
                            const txt = (d.innerText || '').toLowerCase();
                            const aria = (d.getAttribute('aria-label') || '').toLowerCase();
                            if (aria.includes('thông báo') || aria.includes('notification')) return true;
                            if (txt.includes('thông báo trước đó') || txt.includes('đánh dấu tất cả là đã đọc')) return true;
                        }
                        return false;
                    }").ConfigureAwait(false);

                    if (isNotification)
                    {
                        await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    // Nếu có nút "Xem tất cả trang cá nhân" nằm ngay bên trong dialog vừa mở, click tiếp
                    var seeAllInside = page.Locator("div[role='dialog'] div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='dialog'] div[role='button']:has-text('See all profiles'):visible, div[role='dialog'] div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible");
                    if (await seeAllInside.CountAsync().ConfigureAwait(false) > 0)
                    {
                        try
                        {
                            await seeAllInside.Nth(0).ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false);
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                        }
                        catch { }
                    }

                    // Đưa vị trí cuộn về đỉnh (top = 0)
                    await page.EvaluateAsync(@"() => {
                        let dialogs = document.querySelectorAll('div[role=""dialog""]');
                        if (dialogs.length > 0) {
                            let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                            for (let sc of scrollers) sc.scrollTop = 0;
                        }
                    }").ConfigureAwait(false);
                    return true;
                }
                return false;
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
                // Selector chuẩn: chỉ bắt các thẻ listitem chứa radio hoặc button chuyển profile
                var rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:has(div[role='radio']), div[role='dialog'] div[role='listitem']:has(div[role='button'])").AllAsync().ConfigureAwait(false);
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
                                if (/^(Chuyển|Switch|Chuyển ngay|Switch now)$/i.test(t)) {
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
