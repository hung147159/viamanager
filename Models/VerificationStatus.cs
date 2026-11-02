using System;

namespace ViaManager.Models
{
    public enum VerificationStatus
    {
        Unverified = 0,
        Verified = 1,
        Failed = 2,
        Ambiguous = 3
    }

    public record VerificationSignal(
        string SignalType,
        string Value,
        DateTime Timestamp,
        bool IsSatisfied,
        string Details = ""
    );
}
