using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SystemPanel.Auditing;

namespace SystemPanel.Automation
{
    /// <summary>
    /// Runbook çıktısını saklamadan önce maskeler ve kısaltır. JSON çıktıda hassas anahtar adlı alanlar
    /// (password, secret, token, laps...) "***" olur. Düz metinde "parola=..." benzeri ifadeler maskelenir.
    /// </summary>
    public static class RunbookOutputSanitizer
    {
        private static readonly Regex InlineSecret = new(
            "(?i)(password|passwd|pwd|secret|token|api[_-]?key|parola|şifre)[\"']?\\s*[:=]\\s*[\"']?[^\\s\"',;}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Sanitize(string? output, int maxBytes)
        {
            if (string.IsNullOrEmpty(output))
                return string.Empty;

            var text = output.Trim();
            if (text.StartsWith('{') || text.StartsWith('['))
            {
                try
                {
                    var node = JsonNode.Parse(text);
                    if (node != null)
                    {
                        Mask(node);
                        text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    }
                }
                catch (JsonException)
                {
                    // Düz metin olarak devam.
                }
            }

            text = InlineSecret.Replace(text, m => m.Groups[1].Value + "=***");
            return TruncateUtf8(text, maxBytes);
        }

        private static void Mask(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(kv => kv.Key).ToList())
                    {
                        if (AuditRedactor.IsSensitiveKey(key))
                            obj[key] = "***";
                        else if (obj[key] is { } child)
                            Mask(child);
                    }
                    break;
                case JsonArray arr:
                    foreach (var item in arr)
                        if (item != null) Mask(item);
                    break;
            }
        }

        private static string TruncateUtf8(string text, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
                return text;

            var marker = "\n… (çıktı kısaltıldı)";
            var budget = maxBytes - Encoding.UTF8.GetByteCount(marker);
            var sb = new StringBuilder();
            var used = 0;
            foreach (var ch in text)
            {
                var size = Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > budget) break;
                sb.Append(ch);
                used += size;
            }
            return sb + marker;
        }
    }
}
