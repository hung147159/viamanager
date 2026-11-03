using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public interface IPageSwitchService
    {
        Task<bool> SwitchToPageAsync(IPage page, string pageId, CancellationToken cancellationToken = default);
    }

    public class PageSwitchService : IPageSwitchService
    {
        public async Task<bool> SwitchToPageAsync(IPage page, string pageId, CancellationToken cancellationToken = default)
        {
            AppLogger.Info("PageSwitch", $"Initiating switch profile to Page ID: {pageId}");

            // Strategy 1: Navigate directly to the canonical profile page URL
            try
            {
                string targetUrl = $"https://www.facebook.com/profile.php?id={pageId}";
                await page.GotoAsync(targetUrl, new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 25000
                }).ConfigureAwait(false);

                // Wait briefly for banner or switch button to render
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
                string matchedSelector = "";

                // Chờ nút Chuyển hiển thị tối đa 8000ms (8 giây) do Facebook cần 2-3s để redirect URL và render nút
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

                    // Wait for any switch confirmation modal if present
                    await HandleConfirmModalIfPresentAsync(page).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    string warnMsg = $"[WARNING] [PageSwitch] Không tìm thấy nút Chuyển trên trang {pageId} (URL hiện tại: {page.Url})";
                    AppLogger.Warn("PageSwitch", warnMsg);
                    try { Console.WriteLine(warnMsg); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PageSwitch", $"Direct page navigation switch failed for {pageId}: {ex.Message}. Trying top-right profile switcher...");
            }

            // Strategy 2: Switch via top-right Profile switcher menu
            try
            {
                var profileAvatarSelectors = new[]
                {
                    "svg[aria-label='Your profile']",
                    "div[aria-label='Your profile']",
                    "div[aria-label='Trang cá nhân của bạn']",
                    "div[role='button'][aria-label*='profile']",
                    "div[role='button'][aria-label*='Trang cá nhân']",
                    "div[role='button'][aria-label*='Hồ sơ']"
                };

                IElementHandle? avatarBtn = null;
                foreach (var sel in profileAvatarSelectors)
                {
                    avatarBtn = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                    if (avatarBtn != null && await avatarBtn.IsVisibleAsync().ConfigureAwait(false))
                        break;
                }

                if (avatarBtn != null)
                {
                    await avatarBtn.ClickAsync().ConfigureAwait(false);
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                    // Look for "See all profiles" / "Xem tất cả trang cá nhân"
                    var seeAllBtn = await page.QuerySelectorAsync("div[role='button']:has-text('See all profiles'), div[role='button']:has-text('Xem tất cả trang cá nhân'), div[role='button']:has-text('Xem tất cả trang'), div[role='button']:has-text('See all pages'), span:has-text('See all profiles'), span:has-text('Xem tất cả trang cá nhân')").ConfigureAwait(false);
                    if (seeAllBtn != null)
                    {
                        await seeAllBtn.ClickAsync().ConfigureAwait(false);
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }

                    // Look for target Page in the profile switch list
                    var targetPageEntry = await page.QuerySelectorAsync($"div[role='button']:has-text('{pageId}'), a[href*='{pageId}']").ConfigureAwait(false);
                    if (targetPageEntry != null)
                    {
                        AppLogger.Info("PageSwitch", $"Found page entry {pageId} in profile switcher menu. Clicking...");
                        await Task.WhenAll(
                            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                            targetPageEntry.ClickAsync()
                        ).ConfigureAwait(false);

                        await HandleConfirmModalIfPresentAsync(page).ConfigureAwait(false);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("PageSwitch", $"Profile switcher menu switch failed for {pageId}", ex);
            }

            return false;
        }

        private static async Task HandleConfirmModalIfPresentAsync(IPage page)
        {
            try
            {
                // Chờ hộp thoại xác nhận tối đa 3 giây
                for (int wait = 0; wait < 3; wait++)
                {
                    var confirmModalSelectors = new[]
                    {
                        "div[role='dialog'] div[role='button']:has-text('Chuyển')",
                        "div[role='dialog'] div[role='button']:has-text('Switch')",
                        "div[role='dialog'] div[role='button']:has-text('Chuyển ngay')",
                        "div[role='dialog'] div[role='button']:has-text('Switch now')",
                        "div[role='dialog'] [aria-label='Chuyển']",
                        "div[role='dialog'] [aria-label='Switch']"
                    };

                    bool clicked = false;
                    foreach (var sel in confirmModalSelectors)
                    {
                        var confirmBtn = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                        if (confirmBtn != null && await confirmBtn.IsVisibleAsync().ConfigureAwait(false))
                        {
                            AppLogger.Info("PageSwitch", $"Confirmation dialog detected. Clicking confirm: {sel}");
                            try
                            {
                                await confirmBtn.ClickAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                await confirmBtn.ClickAsync().ConfigureAwait(false);
                            }
                            await Task.Delay(1500).ConfigureAwait(false);
                            clicked = true;
                            break;
                        }
                    }

                    if (clicked) break;
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
            catch { }
        }
    }
}
