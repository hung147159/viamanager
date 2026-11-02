using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Playwright;

namespace ViaManager.Models
{
    public class PageSessionSnapshot
    {
        public string PageId { get; }
        public string ParentViaUid { get; }
        public DateTime CapturedAt { get; }
        public string RawCookieHeader { get; }
        public IReadOnlyList<BrowserContextCookiesResult> Cookies { get; }
        public bool IsVerified { get; }
        public IReadOnlyList<VerificationSignal> VerificationSignals { get; }

        public PageSessionSnapshot(
            string pageId,
            string parentViaUid,
            string rawCookieHeader,
            IEnumerable<BrowserContextCookiesResult> cookies,
            bool isVerified,
            IEnumerable<VerificationSignal> signals)
        {
            if (string.IsNullOrWhiteSpace(pageId))
                throw new ArgumentException("PageId cannot be null or empty.", nameof(pageId));
            if (string.IsNullOrWhiteSpace(parentViaUid))
                throw new ArgumentException("ParentViaUid cannot be null or empty.", nameof(parentViaUid));
            if (string.IsNullOrWhiteSpace(rawCookieHeader))
                throw new ArgumentException("RawCookieHeader cannot be empty. Zero synthetic cookie invariant.", nameof(rawCookieHeader));

            PageId = pageId.Trim();
            ParentViaUid = parentViaUid.Trim();
            CapturedAt = DateTime.UtcNow;
            RawCookieHeader = rawCookieHeader;
            Cookies = new ReadOnlyCollection<BrowserContextCookiesResult>(new List<BrowserContextCookiesResult>(cookies ?? Array.Empty<BrowserContextCookiesResult>()));
            IsVerified = isVerified;
            VerificationSignals = new ReadOnlyCollection<VerificationSignal>(new List<VerificationSignal>(signals ?? Array.Empty<VerificationSignal>()));
        }
    }
}
