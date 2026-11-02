using System;
using System.Collections.Generic;

namespace ViaManager.Models
{
    public class PageSwitchResult
    {
        public bool IsSuccess { get; }
        public string PageId { get; }
        public string PageName { get; }
        public VerificationStatus VerificationStatus { get; }
        public PageSessionSnapshot? Snapshot { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<VerificationSignal> Signals { get; }

        public PageSwitchResult(
            bool isSuccess,
            string pageId,
            string pageName,
            VerificationStatus verificationStatus,
            PageSessionSnapshot? snapshot,
            string errorMessage = "",
            IEnumerable<VerificationSignal>? signals = null)
        {
            IsSuccess = isSuccess;
            PageId = pageId;
            PageName = pageName;
            VerificationStatus = verificationStatus;
            Snapshot = snapshot;
            ErrorMessage = errorMessage;
            Signals = new List<VerificationSignal>(signals ?? Array.Empty<VerificationSignal>()).AsReadOnly();
        }

        public static PageSwitchResult Success(
            string pageId,
            string pageName,
            PageSessionSnapshot snapshot,
            IEnumerable<VerificationSignal> signals)
        {
            return new PageSwitchResult(true, pageId, pageName, VerificationStatus.Verified, snapshot, "", signals);
        }

        public static PageSwitchResult Failed(
            string pageId,
            string pageName,
            string errorMessage,
            VerificationStatus status = VerificationStatus.Failed,
            IEnumerable<VerificationSignal>? signals = null)
        {
            return new PageSwitchResult(false, pageId, pageName, status, null, errorMessage, signals);
        }
    }
}
