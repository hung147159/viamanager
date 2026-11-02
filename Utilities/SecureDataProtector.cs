using System;
using System.Security.Cryptography;
using System.Text;

namespace ViaManager.Utilities
{
    public static class SecureDataProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ViaManager_Entropy_v1_Salt");

        /// <summary>
        /// Encrypts plaintext string using Windows DPAPI (CurrentUser scope).
        /// Returns base64-encoded encrypted string prefixed with "ENC:".
        /// </summary>
        public static string Protect(string plaintext, DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            // If already encrypted, return as is
            if (plaintext.StartsWith("ENC:", StringComparison.Ordinal))
                return plaintext;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, scope);
                return "ENC:" + Convert.ToBase64String(cipherBytes);
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback for non-Windows environments if any
                return plaintext;
            }
            catch (Exception)
            {
                // If encryption fails, do not corrupt data
                return plaintext;
            }
        }

        /// <summary>
        /// Decrypts encrypted string (prefixed with "ENC:") using Windows DPAPI.
        /// If not prefixed with "ENC:", returns the string as plaintext (backwards compatibility).
        /// </summary>
        public static string Unprotect(string cipherText, DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            if (!cipherText.StartsWith("ENC:", StringComparison.Ordinal))
                return cipherText; // Not encrypted, return plain

            try
            {
                string rawBase64 = cipherText[4..];
                byte[] cipherBytes = Convert.FromBase64String(rawBase64);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, scope);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                // Decryption failure (e.g. copied to different machine / user)
                return "[ENCRYPTED_DATA_LOCKED]";
            }
        }

        /// <summary>
        /// Masks sensitive secrets for UI display and safe logging.
        /// </summary>
        public static string MaskSecret(string? secret, int visiblePrefix = 2, int visibleSuffix = 2)
        {
            if (string.IsNullOrEmpty(secret))
                return string.Empty;

            if (secret.StartsWith("ENC:", StringComparison.Ordinal))
                return "ENC:********";

            if (secret.Length <= (visiblePrefix + visibleSuffix))
                return new string('*', secret.Length);

            return $"{secret[..visiblePrefix]}{new string('*', secret.Length - visiblePrefix - visibleSuffix)}{secret[^visibleSuffix..]}";
        }
    }
}
