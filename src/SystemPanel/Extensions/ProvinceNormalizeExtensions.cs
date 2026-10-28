namespace SystemPanel.Extensions
{
    public static class ProvinceNormalizeExtensions
    {
        public static string ToSvgId(this string value)
        {
            return value
                .ToLowerInvariant()
                .Replace("ı", "i")
                .Replace("İ", "i")
                .Replace("ş", "s")
                .Replace("ğ", "g")
                .Replace("ö", "o")
                .Replace("ü", "u")
                .Replace("ç", "c")
                .Replace(" ", "");
        }
    }
}
