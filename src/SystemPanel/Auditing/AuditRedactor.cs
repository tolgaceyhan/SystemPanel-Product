using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SystemPanel.Auditing
{
    /// <summary>
    /// Audit'e yazılacak nesneleri JSON'a çevirir ve hassas alanları maskeler.
    /// Anahtar adı password/pwd/secret/token/key/laps/credential içeren her alan
    /// (iç içe nesneler ve diziler dahil) "***" ile değiştirilir.
    /// Bu bir güvenlik ağıdır: çağıranlar hassas değerleri zaten hiç vermemelidir.
    /// </summary>
    public static class AuditRedactor
    {
        public const string Mask = "***";
        public const int MaxJsonLength = 16_000;

        private static readonly Regex SensitiveKey = new(
            // Bilerek geniş: "AgentKey", "ApiKey", "newPassword", "lapsPassword" ... (fazla maskeleme kabul edilebilir).
            @"pass|pwd|secret|token|key|laps|credential|cookie|authorization",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static bool IsSensitiveKey(string key) => SensitiveKey.IsMatch(key);

        public static string? ToRedactedJson(object? value)
        {
            if (value is null)
                return null;

            JsonNode? node = value switch
            {
                JsonNode n => n.DeepClone(),
                string s => JsonValue.Create(s),
                _ => JsonSerializer.SerializeToNode(value, value.GetType(), JsonOptions),
            };

            if (node is null)
                return null;

            Redact(node);

            var json = node.ToJsonString(JsonOptions);
            return json.Length <= MaxJsonLength ? json : json.Substring(0, MaxJsonLength) + "…(truncated)";
        }

        private static void Redact(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        if (IsSensitiveKey(key))
                        {
                            obj[key] = Mask;
                        }
                        else if (obj[key] is JsonNode child)
                        {
                            Redact(child);
                        }
                    }
                    break;

                case JsonArray arr:
                    foreach (var item in arr)
                    {
                        if (item is not null) Redact(item);
                    }
                    break;
            }
        }

        public static string? Truncate(string? value, int max)
            => value is null || value.Length <= max ? value : value.Substring(0, max);
    }
}
