using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ViaManager.Utilities
{
    public static class CookieSerializer
    {
        /// <summary>
        /// Serializes browser cookies returned directly from Playwright BrowserContext.CookiesAsync()
        /// VERBATIM with ZERO MUTATION in canonical HTTP 'Cookie' header format.
        /// Absolute Rule: Never fake, mutate, synthesize, or infer cookies.
        /// Filters for Facebook domain cookies, deduplicates by name, and arranges in standard HTTP header layout.
        /// </summary>
        public static string SerializeVerbatim(IEnumerable<BrowserContextCookiesResult> cookies)
        {
            if (cookies == null)
                return string.Empty;

            // 1. Filter for Facebook cookies if domains are populated (exclude foreign tracking cookies)
            var fbCookies = cookies
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                            (string.IsNullOrEmpty(c.Domain) || c.Domain.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var sourceList = fbCookies.Count > 0 ? fbCookies : cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();

            // 2. Deduplicate by Name (preserving first non-empty value, matching HTTP Cookie header specification)
            var cookieDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in sourceList)
            {
                string name = c.Name.Trim();
                string val = c.Value ?? string.Empty;

                if (!cookieDict.ContainsKey(name) || (string.IsNullOrEmpty(cookieDict[name]) && !string.IsNullOrEmpty(val)))
                {
                    cookieDict[name] = val;
                }
            }

            // 3. Canonical HTTP Cookie header order (standard Facebook session header layout: sb, datr, ps_l, ps_n, wl_cbv, c_user, i_user, dpr, presence, fr, xs, wd...)
            var preferredOrder = new[]
            {
                "sb", "datr", "ps_l", "ps_n", "wl_cbv", "c_user", "i_user", "dpr", "presence", "fr", "xs", "wd"
            };

            var orderedParts = new List<string>();
            var remainingKeys = new HashSet<string>(cookieDict.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var key in preferredOrder)
            {
                if (cookieDict.TryGetValue(key, out var val))
                {
                    orderedParts.Add($"{key}={val}");
                    remainingKeys.Remove(key);
                }
            }

            // Append any remaining Facebook cookies
            foreach (var key in remainingKeys)
            {
                orderedParts.Add($"{key}={cookieDict[key]}");
            }

            return string.Join("; ", orderedParts);
        }

        /// <summary>
        /// Parses a cookie header string into key-value pairs without any alterations or inferences.
        /// </summary>
        public static Dictionary<string, string> ParseCookieHeader(string cookieHeader)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(cookieHeader))
                return dict;

            var tokens = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawToken in tokens)
            {
                var token = rawToken.Trim();
                if (string.IsNullOrEmpty(token))
                    continue;

                int eqIdx = token.IndexOf('=');
                if (eqIdx <= 0)
                    continue;

                string key = token[..eqIdx].Trim();
                string value = token[(eqIdx + 1)..].Trim();

                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                {
                    dict[key] = value;
                }
            }

            return dict;
        }

        /// <summary>
        /// Extracts a specific cookie value by key without modifying the source string.
        /// </summary>
        public static string ExtractValue(string cookieHeader, string key)
        {
            if (string.IsNullOrWhiteSpace(cookieHeader) || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var dict = ParseCookieHeader(cookieHeader);
            return dict.TryGetValue(key, out var val) ? val : string.Empty;
        }

        /// <summary>
        /// Checks if a cookie string contains synthetic or tampered data (e.g. fabricated GUIDs, injected fake markers).
        /// </summary>
        public static bool ContainsSyntheticMarker(string cookieHeader)
        {
            if (string.IsNullOrWhiteSpace(cookieHeader))
                return false;

            // Check for Guid placeholder patterns commonly used in fake generators
            if (Regex.IsMatch(cookieHeader, @"xs=[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}", RegexOptions.IgnoreCase))
                return true;

            // Check for fake token prefixes
            if (cookieHeader.Contains("fake_", StringComparison.OrdinalIgnoreCase) ||
                cookieHeader.Contains("synthesized_", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
