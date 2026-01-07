using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MNBEMART.Services
{
    // Simple PBKDF2 password hasher with self-describing format:
    // PBKDF2$<iterations>$<saltBase64>$<hashBase64>
    public static class PasswordHasher
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16;   // 128-bit
        private const int KeySize  = 32;   // 256-bit
        private static readonly Regex Format = new Regex(@"^PBKDF2\$(\d+)\$([A-Za-z0-9+/=]+)\$([A-Za-z0-9+/=]+)$", RegexOptions.Compiled);

        public static string Hash(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize);
            return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string stored, out bool isLegacyPlain)
        {
            isLegacyPlain = false;
            if (string.IsNullOrWhiteSpace(stored)) return false;

            var m = Format.Match(stored);
            if (!m.Success)
            {
                // Legacy plain-text fallback
                isLegacyPlain = true;
                return string.Equals(password, stored);
            }

            var iterations = int.Parse(m.Groups[1].Value);
            var salt = Convert.FromBase64String(m.Groups[2].Value);
            var hash = Convert.FromBase64String(m.Groups[3].Value);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(hash.Length);
            return CryptographicOperations.FixedTimeEquals(key, hash);
        }

        public static bool IsHashed(string stored) => !string.IsNullOrWhiteSpace(stored) && Format.IsMatch(stored);
    }
}