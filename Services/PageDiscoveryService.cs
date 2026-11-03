using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ViaManager.Utilities;

namespace ViaManager.Services
{
    public record DiscoveredPageCandidate(string PageId, string Name);

    public interface IPageDiscoveryService
    {
        Task<List<DiscoveredPageCandidate>> DiscoverPagesAsync(
            IPage page,
            string currentViaUid,
            CancellationToken cancellationToken = default);
    }

    public class PageDiscoveryService : IPageDiscoveryService
    {
        public async Task<List<DiscoveredPageCandidate>> DiscoverPagesAsync(
            IPage page,
            string currentViaUid,
            CancellationToken cancellationToken = default)
        {
            AppLogger.Info("PageDiscovery", $"Discovering pages for VIA UID: {currentViaUid}");

            try
            {
                await page.GotoAsync("https://www.facebook.com/pages/?category=your_pages", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                }).ConfigureAwait(false);

                // Wait for page list container or timeout gracefully
                try
                {
                    await page.WaitForSelectorAsync("div[role='main']", new() { Timeout = 10000 }).ConfigureAwait(false);
                }
                catch { }

                // Cuộn trang từ từ xuống cuối để tải toàn bộ danh sách Page (Lazy Loading)
                long prevH = 0;
                int unchanged = 0;
                int scrolls = 0;
                while (unchanged < 2 && scrolls++ < 40)
                {
                    long curH = await page.EvaluateAsync<long>(@"() => {
                        window.scrollTo(0, document.body.scrollHeight || document.documentElement.scrollHeight);
                        const mainEl = document.querySelector('div[role=""main""]') || document.scrollingElement || document.documentElement;
                        if (mainEl && mainEl.scrollHeight > mainEl.clientHeight) {
                            mainEl.scrollTop = mainEl.scrollHeight;
                        }
                        return Math.max(document.body.scrollHeight || 0, document.documentElement.scrollHeight || 0, mainEl ? mainEl.scrollHeight : 0);
                    }").ConfigureAwait(false);

                    if (curH == prevH)
                    {
                        unchanged++;
                    }
                    else
                    {
                        unchanged = 0;
                        prevH = curH;
                    }

                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }

                // Script to accurately extract Page IDs and names from the DOM and Facebook data layer
                const string extractionScript = @"
                    () => {
                        const results = [];
                        const seenIds = new Set();

                        // 1. Scan links for /pages/ or /profile.php?id= or direct vanity URLs
                        const excludeKeywords = /friends|watch|marketplace|groups|gaming|bookmarks|events|saved|memories|messages|notifications|settings|privacy|policies|help|create|invites|category=your_pages|ads|business|login|recover|user|\/posts\/|\/photos\/|\/videos\/|story_fbid|set=a\.|fbid=|\/stories\/|\/permalink\//i;
                        const links = Array.from(document.querySelectorAll('a[href]'));
                        for (const a of links) {
                            const href = a.getAttribute('href') || '';
                            if (!href || excludeKeywords.test(href)) continue;
                            const text = (a.innerText || '').trim();

                            let pageId = null;

                            // Pattern: /profile.php?id=1000...
                            const profileMatch = href.match(/profile\.php\?id=(\d{10,})/);
                            if (profileMatch) {
                                pageId = profileMatch[1];
                            }

                            // Pattern: /pages/.../1000...
                            if (!pageId) {
                                const pagesMatch = href.match(/\/pages\/[^\/]+\/(\d{10,})/);
                                if (pagesMatch) {
                                    pageId = pagesMatch[1];
                                }
                            }

                            // Pattern: vanity URL with delegate_page_id or target_id
                            if (!pageId) {
                                const targetMatch = href.match(/[?&](?:page_id|delegate_page_id|target_id)=(\d{10,})/);
                                if (targetMatch) {
                                    pageId = targetMatch[1];
                                }
                            }

                            // CẤM TUYỆT ĐỐI lấy các chuỗi số dài >= 17 chữ số bắt đầu bằng 122 (post_id / photo_id)
                            if (pageId && pageId.length >= 17 && pageId.startsWith('122')) {
                                pageId = null;
                            }
                            if (pageId && pageId.length > 16 && !pageId.startsWith('615')) {
                                pageId = null;
                            }

                            if (pageId) {
                                let nameCandidate = text;
                                if (!nameCandidate || nameCandidate === 'Chuyển' || nameCandidate === 'Switch' || nameCandidate === 'Xem' || nameCandidate === 'View' || /^\d+$/.test(nameCandidate)) {
                                    const card = a.closest('div[role=\'listitem\'], div[role=\'article\'], div[data-visualcompletion]');
                                    if (card) {
                                        const h = card.querySelector('span[dir=\'auto\'], strong, h2');
                                        if (h && h.innerText && h.innerText.trim()) {
                                            nameCandidate = h.innerText.trim();
                                        }
                                    }
                                }

                                if (!seenIds.has(pageId)) {
                                    seenIds.add(pageId);
                                    results.push({ id: pageId, name: nameCandidate || pageId });
                                } else if (nameCandidate && nameCandidate !== pageId && !/^\d+$/.test(nameCandidate)) {
                                    const existing = results.find(r => r.id === pageId);
                                    if (existing && (/^\d+$/.test(existing.name) || !existing.name)) {
                                        existing.name = nameCandidate;
                                    }
                                }
                            }
                        }

                        // 2. Scan script tags for internal Relay/GraphQL preloaded page payloads
                        const scripts = Array.from(document.querySelectorAll('script'));
                        for (const s of scripts) {
                            const content = s.textContent || '';
                            if (content.includes('page_id') || content.includes('delegate_page_id')) {
                                const matches = content.matchAll(/""(?:delegate_)?page_id""\s*:\s*""(\d{10,})""/g);
                                for (const m of matches) {
                                    const pid = m[1];
                                    if (pid && !seenIds.has(pid)) {
                                        seenIds.add(pid);
                                        results.push({ id: pid, name: pid });
                                    }
                                }
                            }
                        }

                        return results;
                    }
                ";

                var rawJson = await page.EvaluateAsync<string>($@"
                    () => JSON.stringify(({extractionScript})())
                ").ConfigureAwait(false);

                var candidates = new List<DiscoveredPageCandidate>();
                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string id = elem.GetProperty("id").GetString() ?? "";
                        string name = elem.GetProperty("name").GetString() ?? id;

                        // Filter condition: Numeric ID, length >= 10, MUST NOT be VIA's own UID, KHÔNG PHẢI ID bài viết 18 chữ số đầu 122...
                        if (!string.IsNullOrWhiteSpace(id) &&
                            id.Length >= 10 &&
                            Regex.IsMatch(id, @"^\d+$") &&
                            !(id.Length >= 17 && id.StartsWith("122")) &&
                            (id.Length <= 16 || id.StartsWith("615")) &&
                            !id.Equals(currentViaUid, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!candidates.Any(c => c.PageId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                            {
                                candidates.Add(new DiscoveredPageCandidate(id, name));
                            }
                        }
                    }
                }

                AppLogger.Info("PageDiscovery", $"Discovered {candidates.Count} candidate pages for UID {currentViaUid}");
                return candidates;
            }
            catch (Exception ex)
            {
                AppLogger.Error("PageDiscovery", $"Failed to discover pages for UID {currentViaUid}", ex);
                return new List<DiscoveredPageCandidate>();
            }
        }
    }
}
