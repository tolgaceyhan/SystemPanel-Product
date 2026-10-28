using System;
using System.DirectoryServices;

namespace SystemPanel.Configuration
{
    public class LdapOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 389;
        public bool UseSsl { get; set; } = false;
        public string BaseDn { get; set; } = "";

        /// <summary>
        /// Boş bırakılırsa LDAP bağlantıları uygulamanın çalıştığı hesapla (IIS uygulama havuzu /
        /// gMSA) yapılır; önerilen yöntem budur (bkz. docs/SECURITY.md).
        /// </summary>
        public string BindUser { get; set; } = "";

        /// <summary>"__ENV__" ise LDAP_BIND_PASSWORD ortam değişkeninden okunur.</summary>
        public string BindPassword { get; set; } = "";

        public int ClientTimeoutSeconds { get; set; } = 15;
        public int ServerTimeLimitSeconds { get; set; } = 10;
        public string ReferralChasing { get; set; } = "All";
        public int MaxRetries { get; set; } = 2;

        public string GetPasswordFromEnvironmentIfNeeded()
            => BindPassword == "__ENV__"
               ? (Environment.GetEnvironmentVariable("LDAP_BIND_PASSWORD") ?? "")
               : BindPassword;

        public string BuildLdapPath(string? relativeDn = null)
        {
            var scheme = UseSsl ? "LDAPS" : "LDAP";
            var dn = string.IsNullOrWhiteSpace(relativeDn) ? BaseDn : relativeDn;
            return string.IsNullOrWhiteSpace(Host)
                ? $"LDAP://{dn}"
                : $"{scheme}://{Host}:{Port}/{dn}";
        }

        /// <summary>
        /// Tüm AD bağlantıları için tek giriş noktası: yapılandırılmış hesap varsa onunla, yoksa
        /// servis kimliğiyle (null kimlik bilgisi) bağlanır. Her zaman Kerberos/NTLM (Secure) kullanır.
        /// </summary>
        public DirectoryEntry CreateDirectoryEntry(string? dnOrLdapPath = null)
        {
            var path = dnOrLdapPath != null && dnOrLdapPath.StartsWith("LDAP", StringComparison.OrdinalIgnoreCase)
                ? dnOrLdapPath
                : BuildLdapPath(dnOrLdapPath);

            var auth = AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing;
            if (UseSsl) auth |= AuthenticationTypes.SecureSocketsLayer;

            return string.IsNullOrWhiteSpace(BindUser)
                ? new DirectoryEntry(path, null, null, auth)
                : new DirectoryEntry(path, BindUser, GetPasswordFromEnvironmentIfNeeded(), auth);
        }
    }
}
