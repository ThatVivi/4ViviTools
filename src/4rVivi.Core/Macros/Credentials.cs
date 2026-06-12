using System.Security.Cryptography;
using System.Text;

namespace FourRVivi.Core.Macros;

/// <summary>DPAPI-encrypted credential blob (per Windows user). Never stores plaintext.</summary>
public sealed class Credentials
{
    public string? UserCipher { get; set; }
    public string? PassCipher { get; set; }

    public void Set(string user, string pass)
    {
        UserCipher = Protect(user);
        PassCipher = Protect(pass);
    }

    public (string user, string pass) Get() => (Unprotect(UserCipher), Unprotect(PassCipher));

    private static string Protect(string s)
    {
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }
    private static string Unprotect(string? c)
    {
        if (string.IsNullOrEmpty(c)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(c), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
