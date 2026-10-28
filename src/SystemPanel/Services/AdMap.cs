using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;

namespace SystemPanel.Services
{
    internal static class AdMap
    {
        public static string GetProp(SearchResult sr, string name)
        {
            try
            {
                if (sr.Properties.Contains(name) && sr.Properties[name].Count > 0)
                    return sr.Properties[name][0]?.ToString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        public static bool GetEnabledFromUserAccountControl(SearchResult sr)
        {
            try
            {
                var uacStr = GetProp(sr, "userAccountControl");
                if (int.TryParse(uacStr, out var uac))
                {
                    const int ADS_UF_ACCOUNTDISABLE = 0x0002;
                    return (uac & ADS_UF_ACCOUNTDISABLE) == 0;
                }
            }
            catch { }
            return true;
        }
    }
}
