namespace TraceLensService.Models.DbModels
{
    /// <summary>tracelens_users tablosundaki bir dashboard kullanıcısı. Şifre düz değil, PBKDF2 özeti olarak tutulur.</summary>
    public sealed record UserRecord(string Username, string PasswordHash, DateTime CreatedAt);
}
