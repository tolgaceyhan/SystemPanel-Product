using System.Collections.Generic;

namespace SystemPanel.Services
{
    /// <summary>"SCCM" yapılandırma bölümünün sır içermeyen kısmı.</summary>
    public sealed class SccmOptions
    {
        public const string SectionName = "SCCM";

        /// <summary>Uzaktan başlatılabilir/durdurulabilir servisler (allowlist).</summary>
        public List<string> ControllableServices { get; set; } = new() { "CcmExec", "SMS_EXECUTIVE", "WDSServer" };
    }
}
