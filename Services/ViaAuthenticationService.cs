using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Models;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public interface IViaAuthenticationService
    {
        Task<OperationResult<string>> EnsureAuthenticatedAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            CancellationToken cancellationToken = default);
    }

    public class ViaAuthenticationService : IViaAuthenticationService
    {
        public async Task<OperationResult<string>> EnsureAuthenticatedAsync(
            IBrowserProfileSession session,
            ViaAccount account,
            CancellationToken cancellationToken = default)
        {
            var page = session.Page;
            var context = session.Context;

            AppLogger.Info("ViaAuth", $"Checking authentication for UID: {account.Uid}");

            // Step 1: Check existing cookies in context
            var existingCookies = await context.CookiesAsync().ConfigureAwait(false);
            var cUserCookie = existingCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase));

            if (cUserCookie != null && !string.IsNullOrWhiteSpace(cUserCookie.Value))
            {
                AppLogger.Info("ViaAuth", $"Active session found in context (c_user={cUserCookie.Value}). Verifying session...");
                await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);

                if (await IsLoggedInAsync(page).ConfigureAwait(false))
                {
                    AppLogger.Info("ViaAuth", $"Session valid for UID {account.Uid}");
                    return OperationResult<string>.Ok(cUserCookie.Value, "Authenticated via persistent session.");
                }
            }

            // Step 2: If cookie is available in account record, inject it
            if (!string.IsNullOrWhiteSpace(account.Cookie))
            {
                AppLogger.Info("ViaAuth", $"Injecting stored cookies for UID: {account.Uid}");
                var parsed = CookieSerializer.ParseCookieHeader(account.Cookie);
                var playwrightCookies = parsed.Select(kvp => new Cookie
                {
                    Name = kvp.Key,
                    Value = kvp.Value,
                    Domain = ".facebook.com",
                    Path = "/",
                    Secure = true,
                    SameSite = SameSiteAttribute.None
                }).ToList();

                if (playwrightCookies.Count > 0)
                {
                    await context.AddCookiesAsync(playwrightCookies).ConfigureAwait(false);
                    await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);

                    if (await IsLoggedInAsync(page).ConfigureAwait(false))
                    {
                        var updatedCookies = await context.CookiesAsync().ConfigureAwait(false);
                        var activeCUser = updatedCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value ?? account.Uid;
                        return OperationResult<string>.Ok(activeCUser, "Authenticated via imported cookie.");
                    }
                }
            }

            // Step 3: Password + 2FA Login Flow
            if (!string.IsNullOrWhiteSpace(account.Password))
            {
                AppLogger.Info("ViaAuth", $"Attempting credential login for UID: {account.Uid}");
                await page.GotoAsync("https://www.facebook.com/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);

                var emailInput = await page.QuerySelectorAsync("input#email, input[name='email']").ConfigureAwait(false);
                var passInput = await page.QuerySelectorAsync("input#pass, input[name='pass']").ConfigureAwait(false);

                if (emailInput != null && passInput != null)
                {
                    await emailInput.FillAsync(account.Uid).ConfigureAwait(false);
                    await passInput.FillAsync(account.Password).ConfigureAwait(false);

                    var loginBtn = await page.QuerySelectorAsync("button#loginbutton, button[name='login']").ConfigureAwait(false);
                    if (loginBtn != null)
                    {
                        await Task.WhenAll(
                            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                            loginBtn.ClickAsync()
                        ).ConfigureAwait(false);
                    }

                    // Check for 2FA requirement
                    var twoFaInput = await page.QuerySelectorAsync("input#approvals_code, input[name='approvals_code']").ConfigureAwait(false);
                    if (twoFaInput != null && !string.IsNullOrWhiteSpace(account.TwoFactorSecret))
                    {
                        AppLogger.Info("ViaAuth", $"2FA challenge detected for {account.Uid}. Generating TOTP code...");
                        string totp = TotpGenerator.GenerateTotp(account.TwoFactorSecret);
                        if (!string.IsNullOrEmpty(totp))
                        {
                            await twoFaInput.FillAsync(totp).ConfigureAwait(false);
                            var submitBtn = await page.QuerySelectorAsync("button#checkpointSubmitButton").ConfigureAwait(false);
                            if (submitBtn != null)
                            {
                                await Task.WhenAll(
                                    page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 15000 }).ContinueWith(_ => { }),
                                    submitBtn.ClickAsync()
                                ).ConfigureAwait(false);
                            }
                        }
                    }

                    if (await IsLoggedInAsync(page).ConfigureAwait(false))
                    {
                        var newCookies = await context.CookiesAsync().ConfigureAwait(false);
                        var activeCUser = newCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value ?? account.Uid;
                        return OperationResult<string>.Ok(activeCUser, "Authenticated via credentials.");
                    }
                }
            }

            // Check if account is checkpointed
            if (page.Url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn("ViaAuth", $"Account {account.Uid} reached checkpoint.");
                return OperationResult<string>.Fail("Account reached checkpoint.", "CHECKPOINT");
            }

            return OperationResult<string>.Fail($"Unable to authenticate UID {account.Uid}.", "AUTH_FAILED");
        }

        private static async Task<bool> IsLoggedInAsync(IPage page)
        {
            try
            {
                if (page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                    page.Url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check for primary FB navigation signals
                var profileAvatar = await page.QuerySelectorAsync("svg[aria-label='Your profile'], div[aria-label='Your profile'], div[aria-label='Trang cá nhân của bạn']").ConfigureAwait(false);
                if (profileAvatar != null)
                    return true;

                var feed = await page.QuerySelectorAsync("div[role='feed'], div[role='main']").ConfigureAwait(false);
                return feed != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
