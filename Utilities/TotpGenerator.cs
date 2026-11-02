using System;
using System.Security.Cryptography;

namespace ViaManager.Utilities
{
    public static class TotpGenerator
    {
        public static string GenerateTotp(string base32Secret)
        {
            if (string.IsNullOrWhiteSpace(base32Secret))
                return string.Empty;

            try
            {
                string cleanSecret = base32Secret.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
                byte[] key = Base32Decode(cleanSecret);
                if (key.Length == 0)
                    return string.Empty;

                long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
                byte[] timeBytes = BitConverter.GetBytes(timeStep);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(timeBytes);

                using var hmac = new HMACSHA1(key);
                byte[] hash = hmac.ComputeHash(timeBytes);

                int offset = hash[^1] & 0x0F;
                int binaryCode = ((hash[offset] & 0x7F) << 24)
                               | ((hash[offset + 1] & 0xFF) << 16)
                               | ((hash[offset + 2] & 0xFF) << 8)
                               | (hash[offset + 3] & 0xFF);

                int otp = binaryCode % 1_000_000;
                return otp.ToString("D6");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] Base32Decode(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var output = new System.Collections.Generic.List<byte>();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in base32)
            {
                if (c == '=')
                    break;

                int val = alphabet.IndexOf(c);
                if (val < 0)
                    continue;

                buffer = (buffer << 5) | val;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    output.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }

            return output.ToArray();
        }
    }
}
