// Credentials.cs — DPAPI-encrypted login storage for 4rVivi auto-reconnect.
// The password is NEVER written in plaintext and NEVER stored inside a macro file.
// It's encrypted with the Windows user account key (ProtectedData), so the blob is
// useless on another machine/account. .NET Framework 4.x. Add reference: System.Security. MIT.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace _4rVivi.Macros
{
    public sealed class Credentials
    {
        public string Username = "";
        private byte[] _encPassword;     // DPAPI blob, serialized as base64

        // Extra entropy so the blob can't be decrypted by other apps running as the same user.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("4rVivi::login::v1");

        public void SetPassword(string plain)
        {
            _encPassword = string.IsNullOrEmpty(plain)
                ? null
                : ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        }

        /// <summary>Decrypt on demand, only at the moment of typing it into the login screen.</summary>
        public string GetPassword()
        {
            if (_encPassword == null) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(_encPassword, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; } // blob from another machine/account -> unreadable by design
        }

        public bool HasPassword => _encPassword != null;

        // ---- persistence (base64 of the encrypted blob; plaintext never touches disk) ----
        public void Save(string path)
        {
            string b64 = _encPassword == null ? "" : Convert.ToBase64String(_encPassword);
            File.WriteAllText(path, Username + "\n" + b64, Encoding.UTF8);
        }

        public static Credentials Load(string path)
        {
            var c = new Credentials();
            if (!File.Exists(path)) return c;
            var parts = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            c.Username = parts.Length > 0 ? parts[0] : "";
            if (parts.Length > 1 && parts[1].Trim().Length > 0)
                c._encPassword = Convert.FromBase64String(parts[1].Trim());
            return c;
        }
    }
}
