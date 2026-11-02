using System;

namespace ViaManager.Models
{
    public class ViaAccount
    {
        public string Uid { get; set; } = "";
        public string Password { get; set; } = "";
        public string TwoFactorSecret { get; set; } = "";
        public string Cookie { get; set; } = "";
        public string Token { get; set; } = "";
        public string Email { get; set; } = "";
        public string EmailPassword { get; set; } = "";
        public string Status { get; set; } = "live";
        public string Role { get; set; } = "Run";
        public string AccountName { get; set; } = "";
        public string BusinessManager { get; set; } = "Bmv1";
        public string BusinessManagerMail { get; set; } = "";
        public DateTime? LastRunTime { get; set; }
        public int PageCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Legacy compatibility properties
        public string ID { get => Uid; set => Uid = value; }
        public string PASS { get => Password; set => Password = value; }
        public string Ma2FA { get => TwoFactorSecret; set => TwoFactorSecret = value; }
        public string MAIL { get => Email; set => Email = value; }
        public string PASS_MAIL { get => EmailPassword; set => EmailPassword = value; }
        public string TrangThai { get => Status; set => Status = value; }
        public string VaiTro { get => Role; set => Role = value; }
        public string TenTaiKhoan { get => AccountName; set => AccountName = value; }
        public string BM { get => BusinessManager; set => BusinessManager = value; }
        public string BM_Mail { get => BusinessManagerMail; set => BusinessManagerMail = value; }

        public string GetMaskedPassword() => string.IsNullOrEmpty(Password) ? "" : "********";
        public string GetMasked2FA() => string.IsNullOrEmpty(TwoFactorSecret) ? "" : (TwoFactorSecret.Length > 4 ? $"{TwoFactorSecret[..2]}****{TwoFactorSecret[^2..]}" : "****");
    }
}
