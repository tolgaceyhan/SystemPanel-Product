using System;
using System.Collections.Generic;

namespace SystemPanel.Configuration
{
    /// <summary>
    /// Kuruma özgü AD yapısı. Önceden kodda sabitti (kök OU, kullanıcı OU'su, il yönetici grubu sonekleri,
    /// "tüm iller" grubu ...). Değerler appsettings.{Environment}.json'da verilir; boş bırakılırsa
    /// ilgili özellik çalışmaz ama güvenli tarafta kalır (ör. kapsam türetilemez → erişim yok).
    /// </summary>
    public sealed class ActiveDirectoryOptions
    {
        public const string SectionName = "ActiveDirectory";

        /// <summary>Kimlik doğrulama domain'i (FQDN veya NetBIOS).</summary>
        public string Domain { get; set; } = string.Empty;

        public ScopeDerivationOptions Scope { get; set; } = new();

        public DirectoryLayoutOptions Directory { get; set; } = new();

        /// <summary>
        /// Paneldeki hiçbir kullanıcının üyelik ekleyip çıkaramayacağı ek gruplar
        /// (Domain Admins, Enterprise Admins gibi yerleşik ayrıcalıklı gruplar her zaman korunur).
        /// </summary>
        public List<string> ProtectedGroups { get; set; } = new();
    }

    /// <summary>
    /// LocalAdmin / Operator kapsamının (ou:root) kullanıcının kendi DN'inden türetilmesi.
    /// Örnek DN: CN=Ali,OU=KULLANICILAR,OU=ANKARA,OU=IL,DC=corp  veya  CN=Ali,OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp
    /// AnchorOu = "IL", BranchOus = ["KULLANICILAR","BILGISAYARLAR"] ise il = ANKARA ve kapsam kökleri:
    ///   OU=ANKARA,OU=IL,DC=corp ; OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp ; OU=ANKARA,OU=BILGISAYARLAR,OU=IL,DC=corp
    /// (Var olmayan kökler hiçbir nesne içermez; önceki kod var olmayan sentetik bir OU üretip
    /// bind hatasında tüm domain'e düşüyordu.)
    /// </summary>
    public sealed class ScopeDerivationOptions
    {
        /// <summary>İl OU'larının altında bulunduğu OU'nun adı (RDN değeri). Boşsa kapsam türetilmez.</summary>
        public string AnchorOu { get; set; } = string.Empty;

        /// <summary>Anchor ile il arasında (veya il altında) bulunabilen dal OU adları.</summary>
        public List<string> BranchOus { get; set; } = new();

        /// <summary>İl yöneticisi gruplarının sAMAccountName soneki (ör. "_Local_Admins"). sp:province için.</summary>
        public string LocalAdminGroupSuffix { get; set; } = string.Empty;

        /// <summary>İl kapsamı VERMEYEN (tüm iller için) gruplar.</summary>
        public List<string> ExcludedLocalAdminGroups { get; set; } = new();
    }

    /// <summary>SMS ve rehber ekranlarının kullandığı OU yerleşimi (AdDirectoryService).</summary>
    public sealed class DirectoryLayoutOptions
    {
        /// <summary>İl yönetici gruplarının bulunduğu OU DN'i.</summary>
        public string LocalAdminGroupsOu { get; set; } = string.Empty;

        /// <summary>Kullanıcı il OU'larının bulunduğu kök OU DN'i.</summary>
        public string UsersBaseOu { get; set; } = string.Empty;

        /// <summary>Toplu SMS'e dahil edilmeyecek OU adları (ör. onaysız kullanıcılar).</summary>
        public List<string> ExcludedUserOus { get; set; } = new();

        /// <summary>"Şube müdürleri" listesi için AD title değeri.</summary>
        public string BranchManagerTitle { get; set; } = string.Empty;
    }

    /// <summary>AD grup adı → SystemPanel rol adı (çoklu). Anahtarlar rol adlarıdır.</summary>
    public sealed class RoleMappingOptions : Dictionary<string, string[]>
    {
        public const string SectionName = "RoleMapping";

        public RoleMappingOptions() : base(StringComparer.OrdinalIgnoreCase) { }
    }
}
