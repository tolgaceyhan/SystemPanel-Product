using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemPanel.Contracts.Licensing;

/// <summary>
/// Lisans dosyası biçimi (JSON): { "alg": "ES256", "payload": base64(UTF-8 JSON), "signature": base64 }.
/// İmza: ECDSA P-256 / SHA-256, IEEE P1363 biçimi, payload baytları üzerinde.
/// </summary>
public static class LicenseCodec
{
    public const string Algorithm = "ES256";
    public const int MaxFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed class Envelope
    {
        public string Alg { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public static string Sign(LicenseInfo license, ECDsa privateKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(license, JsonOptions);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.Serialize(new Envelope
        {
            Alg = Algorithm,
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(signature),
        }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
    }

    /// <summary>İmzayı ve biçimi doğrular. Süre kontrolü YAPMAZ (bkz. <see cref="LicenseEvaluator"/>).</summary>
    public static bool TryVerify(string text, ECDsa publicKey, out LicenseInfo? license, out string? error)
    {
        license = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
        {
            error = "Lisans dosyası boş veya çok büyük.";
            return false;
        }

        Envelope? envelope;
        byte[] payload, signature;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(text, JsonOptions);
            if (envelope == null || envelope.Alg != Algorithm)
            {
                error = "Desteklenmeyen lisans biçimi.";
                return false;
            }
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            error = "Lisans dosyası okunamadı.";
            return false;
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            error = "Lisans imzası geçersiz.";
            return false;
        }

        try
        {
            license = JsonSerializer.Deserialize<LicenseInfo>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            license = null;
        }

        if (license == null || license.FormatVersion != LicenseInfo.CurrentFormatVersion ||
            string.IsNullOrWhiteSpace(license.LicenseId) || string.IsNullOrWhiteSpace(license.Customer))
        {
            license = null;
            error = "Lisans içeriği geçersiz.";
            return false;
        }

        return true;
    }

    public static ECDsa ImportPublicKeyPem(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }
}
