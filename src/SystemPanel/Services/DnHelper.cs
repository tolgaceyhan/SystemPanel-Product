using System;

namespace SystemPanel.Services
{
    public static class DnHelper
    {
        public static bool IsSameDn(string dn1, string dn2)
            => Normalize(dn1).Equals(Normalize(dn2), System.StringComparison.OrdinalIgnoreCase);

        public static bool IsUnderDn(string childDn, string parentDn)
        {
            var child = Normalize(childDn);
            var parent = Normalize(parentDn);
            return child.Length > parent.Length
                   && child.EndsWith("," + parent, System.StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string dn)
            => (dn ?? string.Empty).Trim();
    }
}
