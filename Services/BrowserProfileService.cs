using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public interface IBrowserProfileService
    {
        Task<IBrowserProfileSession> AcquireSessionAsync(string viaUid, bool isHeadless = true, CancellationToken cancellationToken = default);
    }

    public interface IBrowserProfileSession : IAsyncDisposable
    {
        string ViaUid { get; }
        IBrowserContext Context { get; }
        IPage Page { get; }
    }

    public class BrowserProfileService : IBrowserProfileService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyedLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _baseProfilesDirectory;
        private readonly bool _allowInsecureFlags;

        public BrowserProfileService(string? baseProfilesDirectory = null, bool allowInsecureFlags = false)
        {
            _baseProfilesDirectory = string.IsNullOrWhiteSpace(baseProfilesDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserProfiles")
                : baseProfilesDirectory;
            _allowInsecureFlags = allowInsecureFlags;

            if (!Directory.Exists(_baseProfilesDirectory))
            {
                Directory.CreateDirectory(_baseProfilesDirectory);
            }
        }

        public async Task<IBrowserProfileSession> AcquireSessionAsync(string viaUid, bool isHeadless = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(viaUid))
                throw new ArgumentException("ViaUid cannot be empty.", nameof(viaUid));

            var sem = KeyedLocks.GetOrAdd(viaUid, _ => new SemaphoreSlim(1, 1));
            
            AppLogger.Debug("BrowserProfileService", $"Acquiring profile lock for UID: {viaUid}");
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string profilePath = Path.Combine(_baseProfilesDirectory, viaUid);
                if (!Directory.Exists(profilePath))
                {
                    Directory.CreateDirectory(profilePath);
                }

                var playwright = await Playwright.CreateAsync().ConfigureAwait(false);

                // Configure arguments safely. Insecure flags are excluded by default for security.
                var args = new System.Collections.Generic.List<string>
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-notifications",
                    "--disable-infobars"
                };

                if (_allowInsecureFlags)
                {
                    AppLogger.Warn("BrowserProfileService", $"Launching UID {viaUid} with insecure flags requested.");
                    args.Add("--no-sandbox");
                    args.Add("--disable-setuid-sandbox");
                    args.Add("--ignore-certificate-errors");
                }

                var contextOptions = new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = isHeadless, // Respect caller configuration
                    Args = args,
                    ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    Locale = "vi-VN",
                    TimezoneId = "Asia/Ho_Chi_Minh"
                };

                AppLogger.Info("BrowserProfileService", $"Launching persistent context for UID {viaUid} (Headless: {isHeadless})");
                var context = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, contextOptions).ConfigureAwait(false);
                var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync().ConfigureAwait(false);

                return new BrowserProfileSession(viaUid, profilePath, sem, playwright, context, page);
            }
            catch (Exception ex)
            {
                sem.Release();
                AppLogger.Error("BrowserProfileService", $"Failed to initialize browser session for UID {viaUid}", ex);
                throw;
            }
        }

        private sealed class BrowserProfileSession : IBrowserProfileSession
        {
            private readonly string _viaUid;
            private readonly string _profilePath;
            private readonly SemaphoreSlim _lock;
            private readonly IPlaywright _playwright;
            private readonly IBrowserContext _context;
            private readonly IPage _page;
            private int _disposed;

            public string ViaUid => _viaUid;
            public IBrowserContext Context => _context;
            public IPage Page => _page;

            public BrowserProfileSession(
                string viaUid,
                string profilePath,
                SemaphoreSlim @lock,
                IPlaywright playwright,
                IBrowserContext context,
                IPage page)
            {
                _viaUid = viaUid;
                _profilePath = profilePath;
                _lock = @lock;
                _playwright = playwright;
                _context = context;
                _page = page;
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                try
                {
                    AppLogger.Debug("BrowserProfileService", $"Closing browser context for UID {_viaUid}");
                    await _context.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("BrowserProfileService", $"Error closing context for {_viaUid}: {ex.Message}");
                }
                finally
                {
                    try { _playwright.Dispose(); } catch { }
                    _lock.Release();
                    AppLogger.Debug("BrowserProfileService", $"Released profile lock for UID {_viaUid}");
                }
            }
        }
    }
}
