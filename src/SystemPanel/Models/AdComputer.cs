using System;
using System.ComponentModel;

namespace SystemPanel.Models
{
    public class AdComputer
    {
        public string Cn { get; set; }
        public string Name { get; set; }
        public string OperatingSystem { get; set; }
        public string OperatingSystemVersion { get; set; }
        public string Description { get; set; }
        public string DistinguishedName { get; set; }
        public DateTime? LastLogon { get; set; }
        public bool IsEnabled { get; set; }

        public string DnsHostName { get; set; } // Added this property

        // Geriye dönük uyumluluk alias'ları (GET/SET -> yeni alanlara yönlendirir)
        [Obsolete("Kullanım dışı. 'Name' kullanın.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string ComputerName
        {
            get => Name;
            set => Name = value;
        }


        [Obsolete("Kullanım dışı. 'LastLogon' kullanın.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public DateTime? LastLogonDate
        {
            get => LastLogon;
            set => LastLogon = value;
        }
    }
}
