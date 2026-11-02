using System;
using System.Collections.Generic;

namespace ViaManager.Models
{
    public class ManagedPage
    {
        public string PageId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string ParentViaUid { get; set; } = "";
        public string BusinessManager { get; set; } = "";
        public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;
        public DateTime? VerifiedAt { get; set; }
        public List<VerificationSignal> VerificationSignals { get; set; } = new();
        public string RawCookieSnapshot { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Legacy compatibility
        public string Page_ID { get => PageId; set => PageId = value; }
        public string Via_UID { get => ParentViaUid; set => ParentViaUid = value; }
        public string BM { get => BusinessManager; set => BusinessManager = value; }
        public string Session_Data { get => RawCookieSnapshot; set => RawCookieSnapshot = value; }
    }
}
