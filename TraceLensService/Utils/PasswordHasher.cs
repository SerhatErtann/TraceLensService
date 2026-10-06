using System.Security.Cryptography;
using System.Text;

namespace TraceLensService.Utils
{
    /// <summary>
    /// Şifre özeti: PBKDF2-HMAC-SHA256, 600.000 tur, kullanıcı başına rastgele 16 bayt tuz (OWASP önerisi).
    /// Biçim: <c>pbkdf2-sha256$turSayısı$tuz$özet</c> (base64). Şifrenin kendisi hiçbir yerde saklanmaz.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";
        private const int Iterations = 600_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        // Olmayan kullanıcıyla girişte de aynı süre harcansın (yanıt süresinden kullanıcı adı tahmin edilemesin)
        private static readonly Lazy<string> Dummy = new(() => Hash(Guid.NewGuid().ToString()));

        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string? stored)
        {
            string[] parts = (stored ?? Dummy.Value).Split('$');
            if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations))
                return false;

            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected) && stored is not null;
        }

        /// <summary>Oturum cookie'sine yazılan kısa iz: şifre değişince eski oturumlar geçersiz olur.</summary>
        public static string Stamp(string passwordHash) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];
    }
}
