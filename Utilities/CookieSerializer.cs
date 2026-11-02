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
        /// VERBATIM with ZERO MUTATION. Absolute Rule: Never fake, mutate, synthesize, or infer cookies.
        /// </summary>
        public static string SerializeVerbatim(IEnumerable<BrowserContextCookiesResult> cookies)
        {
            if (cookies == null)
                return string.Empty;

            var parts = new List<string>();
            foreach (var cookie in cookies)
            {
                if (string.IsNullOrWhiteSpace(cookie.Name))
                    continue;

                // Absolute fidelity: Exactly name=value returned by the browser session
                parts.Add($"{cookie.Name.Trim()}={cookie.Value ?? string.Empty}");
            }

            return string.Join("; ", parts);
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
