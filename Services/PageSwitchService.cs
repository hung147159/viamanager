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

            // Strategy 1: Navigate directly to the page URL
            try
            {
                await page.GotoAsync($"https://www.facebook.com/{pageId}", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 25000
                }).ConfigureAwait(false);

                // Wait briefly for banner or switch button to render
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

                var switchSelectors = new[]
                {
                    "div[role='button']:has-text('Switch now')",
                    "div[role='button']:has-text('Chuyển ngay')",
                    "div[aria-label='Switch']",
                    "div[aria-label='Chuyển']",
                    "div[aria-label='Chuyển trang']",
                    "div[role='button']:has-text('Switch')",
                    "div[role='button']:has-text('Đổi')",
                    "a:has-text('Switch now')",
                    "a:has-text('Chuyển ngay')"
                };

                foreach (var selector in switchSelectors)
                {
                    var btn = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
                    if (btn != null && await btn.IsVisibleAsync().ConfigureAwait(false))
                    {
                        AppLogger.Info("PageSwitch", $"Found switch button with selector: {selector}. Clicking...");
                        
                        try
                        {
                            await Task.WhenAll(
                                page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                                btn.ClickAsync()
                            ).ConfigureAwait(false);
                        }
                        catch
                        {
                            await btn.ClickAsync().ConfigureAwait(false);
                        }

                        // Wait for any switch confirmation modal if present
                        await HandleConfirmModalIfPresentAsync(page).ConfigureAwait(false);
                        return true;
                    }
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
                    "div[role='button'][aria-label*='profile']"
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
                    var seeAllBtn = await page.QuerySelectorAsync("div[role='button']:has-text('See all profiles'), div[role='button']:has-text('Xem tất cả trang cá nhân')").ConfigureAwait(false);
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
                var confirmModalSelectors = new[]
                {
                    "div[role='dialog'] div[role='button']:has-text('Switch')",
                    "div[role='dialog'] div[role='button']:has-text('Chuyển')",
                    "div[role='dialog'] div[role='button']:has-text('Switch now')",
                    "div[role='dialog'] div[role='button']:has-text('Chuyển ngay')"
                };

                foreach (var sel in confirmModalSelectors)
                {
                    var confirmBtn = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                    if (confirmBtn != null && await confirmBtn.IsVisibleAsync().ConfigureAwait(false))
                    {
                        AppLogger.Info("PageSwitch", $"Confirmation dialog detected. Clicking confirm: {sel}");
                        await Task.WhenAll(
                            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                            confirmBtn.ClickAsync()
                        ).ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
