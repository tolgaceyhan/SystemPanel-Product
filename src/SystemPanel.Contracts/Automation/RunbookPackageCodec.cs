using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemPanel.Contracts.Automation;

/// <summary>Doğrulanmış (imzası ve betik özeti tutan) runbook paketi.</summary>
public sealed record VerifiedRunbookPackage(RunbookManifest Manifest, string Script, string KeyFingerprint, string ManifestJson);

/// <summary>
/// Runbook paket dosyası (.sprb) biçimi:
/// <code>{ "format": "sprb/1", "keyId": "&lt;anahtar parmak izi&gt;", "manifest": base64(UTF-8 JSON), "script": base64(UTF-8), "signature": base64 }</code>
/// İmza ECDSA P-256 / SHA-256 (IEEE P1363) ile MANIFEST baytları üzerindedir. Manifest betiğin SHA-256 özetini
/// içerdiğinden betik de dolaylı olarak imzalanmış olur.
/// </summary>
public static class RunbookPackageCodec
{
    public const string Format = "sprb/1";
    public const int MaxPackageBytes = 512 * 1024;
    public const int MaxScriptBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Envelope
    {
        public string Format { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public string Manifest { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>Betiğin özetini manifeste yazar, manifesti doğrular ve imzalar.</summary>
    public static string Sign(RunbookManifest manifest, string script, ECDsa privateKey)
    {
        var scriptBytes = Encoding.UTF8.GetBytes(script);
        if (scriptBytes.Length > MaxScriptBytes)
            throw new ArgumentException("Betik çok büyük.");

        manifest.ScriptSha256 = Sha256Hex(scriptBytes);
        var errors = RunbookManifestValidator.Validate(manifest);
        if (errors.Count > 0)
            throw new ArgumentException("Manifest geçersiz: " + string.Join(" ", errors));

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var signature = privateKey.SignData(manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return JsonSerializer.Serialize(new Envelope
        {
            Format = Format,
            KeyId = Fingerprint(privateKey),
            Manifest = Convert.ToBase64String(manifestBytes),
            Script = Convert.ToBase64String(scriptBytes),
            Signature = Convert.ToBase64String(signature),
        }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
    }

    /// <summary>Yalnızca zarfı okur ve imzalayan anahtarın parmak izini döndürür (imza DOĞRULANMAZ).</summary>
    public static bool TryReadKeyId(string text, out string? keyId, out string? error)
    {
        keyId = null;
        if (!TryParseEnvelope(text, out var envelope, out error))
            return false;
        keyId = envelope!.KeyId;
        return true;
    }

    /// <summary>İmzayı, betik özetini ve manifest yapısını doğrular.</summary>
    public static bool TryVerify(string text, ECDsa publicKey, out VerifiedRunbookPackage? package, out string? error)
    {
        package = null;
        if (!TryParseEnvelope(text, out var envelope, out error))
            return false;

        byte[] manifestBytes, scriptBytes, signature;
        try
        {
            manifestBytes = Convert.FromBase64String(envelope!.Manifest);
            scriptBytes = Convert.FromBase64String(envelope.Script);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException)
        {
            error = "Paket bozuk (base64).";
            return false;
        }

        if (!string.Equals(envelope.KeyId, Fingerprint(publicKey), StringComparison.OrdinalIgnoreCase))
        {
            error = "Paket bu anahtarla imzalanmamış.";
            return false;
        }

        if (!publicKey.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            error = "Paket imzası geçersiz.";
            return false;
        }

        RunbookManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RunbookManifest>(manifestBytes, JsonOptions);
        }
        catch (JsonException)
        {
            manifest = null;
        }

        if (manifest == null)
        {
            error = "Manifest okunamadı.";
            return false;
        }

        var errors = RunbookManifestValidator.Validate(manifest);
        if (errors.Count > 0)
        {
            error = "Manifest geçersiz: " + string.Join(" ", errors);
            return false;
        }

        if (scriptBytes.Length > MaxScriptBytes || !string.Equals(Sha256Hex(scriptBytes), manifest.ScriptSha256, StringComparison.Ordinal))
        {
            error = "Betik özeti manifestle uyuşmuyor.";
            return false;
        }

        package = new VerifiedRunbookPackage(manifest, Encoding.UTF8.GetString(scriptBytes), envelope.KeyId.ToLowerInvariant(), Encoding.UTF8.GetString(manifestBytes));
        return true;
    }

    /// <summary>Açık anahtarın (SubjectPublicKeyInfo DER) SHA-256 özeti, küçük harf hex.</summary>
    public static string Fingerprint(ECDsa key)
        => Sha256Hex(key.ExportSubjectPublicKeyInfo());

    public static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static ECDsa ImportPublicKeyPem(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    private static bool TryParseEnvelope(string text, out Envelope? envelope, out string? error)
    {
        envelope = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxPackageBytes)
        {
            error = "Paket boş veya çok büyük.";
            return false;
        }

        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(text, JsonOptions);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope == null || envelope.Format != Format || string.IsNullOrWhiteSpace(envelope.KeyId))
        {
            error = "Desteklenmeyen paket biçimi.";
            return false;
        }

        return true;
    }
}
