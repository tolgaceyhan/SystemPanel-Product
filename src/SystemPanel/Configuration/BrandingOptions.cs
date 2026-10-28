namespace SystemPanel.Configuration
{
    /// <summary>
    /// Arayüzde görünen ürün adı ve logo. Kod/namespace adı (SystemPanel) teknik addır; ticari
    /// marka değişirse yalnızca bu ayar değişir.
    /// </summary>
    public sealed class BrandingOptions
    {
        public const string SectionName = "Branding";

        public string ProductName { get; set; } = "SystemPanel";

        /// <summary>Üst bar ve menü logosu (web kökünden yol).</summary>
        public string LogoPath { get; set; } = "/img/systempanellogo.png";

        /// <summary>Giriş sayfası logosu.</summary>
        public string LoginLogoPath { get; set; } = "/img/systempanel-logo.svg";
    }
}
