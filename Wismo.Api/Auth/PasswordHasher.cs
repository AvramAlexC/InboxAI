using System.Security.Cryptography;

namespace Wismo.Api.Auth;

public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string hash, string salt);
}

public sealed class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        byte[] saltBytes;
        byte[] hashBytes;

        try
        {
            saltBytes = Convert.FromBase64String(salt);
            hashBytes = Convert.FromBase64String(hash);
        }
        catch
        {
            return false;
        }

        var computed = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, hashBytes.Length);
        return CryptographicOperations.FixedTimeEquals(computed, hashBytes);
    }
}
