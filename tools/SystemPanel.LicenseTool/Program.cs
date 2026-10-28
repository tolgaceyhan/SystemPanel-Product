using System.Globalization;
using System.Security.Cryptography;
using SystemPanel.Contracts.Licensing;

// SystemPanel lisans aracı (yalnızca üretici kullanır).
//
//   splicense keygen --out <klasör>
//   splicense issue  --key <private.pem> --customer "<Kurum>" --out <dosya.lic>
//                    [--edition Standard] [--expires 2027-12-31] [--support 2027-12-31]
//                    [--max-agents 100] [--features ad,laps,gpo] [--id <lisans-no>]
//   splicense verify --public <public.pem> --license <dosya.lic>
//
// Runbook paketleri (ADR-002; lisans anahtarından AYRI bir anahtar çifti kullanılır):
//   splicense runbook-keygen   --out <klasör>
//   splicense runbook-sign     --key <runbook-signing.private.pem> --source <runbook klasörü> --out <paket.sprb>
//   splicense runbook-sign-all --key <runbook-signing.private.pem> --source runbooks/src --out <klasör>
//   splicense runbook-verify   --public <public.pem> --package <paket.sprb>
//
// ÖZEL ANAHTAR: repoya eklemeyin, paylaşmayın, şifreli ve yedekli bir ortamda (tercihen HSM / çevrimdışı
// makine) saklayın. Kaybolursa yeni lisans üretilemez; sızarsa herkes lisans üretebilir.

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
        return Usage();

    var options = ParseOptions(args.Skip(1).ToArray());
    try
    {
        return args[0] switch
        {
            "keygen" => KeyGen(options),
            "issue" => Issue(options),
            "verify" => Verify(options),
            "runbook-keygen" => RunbookKeyGen(options),
            "runbook-sign" => RunbookSign(options),
            "runbook-sign-all" => RunbookSignAll(options),
            "runbook-verify" => RunbookVerify(options),
            _ => Usage(),
        };
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or CryptographicException or FormatException)
    {
        Console.Error.WriteLine("Hata: " + ex.Message);
        return 1;
    }
}

static int KeyGen(Dictionary<string, string> o)
{
    var dir = Required(o, "out");
    Directory.CreateDirectory(dir);
    var privatePath = Path.Combine(dir, "license-signing.private.pem");
    var publicPath = Path.Combine(dir, "license-public.pem");
    if (File.Exists(privatePath))
        throw new IOException($"{privatePath} zaten var; üzerine yazılmadı.");

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine($"Özel anahtar : {privatePath}  (GİZLİ — repoya eklemeyin)");
    Console.WriteLine($"Açık anahtar : {publicPath}");
    Console.WriteLine("Açık anahtarı src/SystemPanel/Licensing/license-public.pem olarak kopyalayıp ürünü derleyin.");
    return 0;
}

static int Issue(Dictionary<string, string> o)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Required(o, "key")));

    var license = new LicenseInfo
    {
        LicenseId = o.GetValueOrDefault("id") ?? $"SP-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetHexString(8)}",
        Customer = Required(o, "customer"),
        Edition = o.GetValueOrDefault("edition") ?? LicenseEditions.Standard,
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = Date(o, "expires"),
        SupportExpiresUtc = Date(o, "support"),
        MaxAgents = o.TryGetValue("max-agents", out var m) ? int.Parse(m, CultureInfo.InvariantCulture) : null,
        Features = (o.GetValueOrDefault("features") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
    };

    var unknown = license.Features.Except(LicenseFeatures.All).ToList();
    if (unknown.Count > 0)
        throw new ArgumentException("Bilinmeyen modül: " + string.Join(", ", unknown));

    var outPath = Required(o, "out");
    File.WriteAllText(outPath, LicenseCodec.Sign(license, key));
    Console.WriteLine($"Lisans yazıldı: {outPath} ({license.LicenseId}, {license.Customer})");
    return 0;
}

static int Verify(Dictionary<string, string> o)
{
    using var key = LicenseCodec.ImportPublicKeyPem(File.ReadAllText(Required(o, "public")));
    if (!LicenseCodec.TryVerify(File.ReadAllText(Required(o, "license")), key, out var license, out var error))
    {
        Console.Error.WriteLine("GEÇERSİZ: " + error);
        return 2;
    }

    var status = LicenseEvaluator.Evaluate(license!, DateTime.UtcNow);
    Console.WriteLine($"{status.State}: {license!.LicenseId} | {license.Customer} | {license.Edition} | bitiş {license.ExpiresUtc?.ToString("yyyy-MM-dd") ?? "süresiz"} | agent {license.MaxAgents?.ToString() ?? "sınırsız"}");
    return status.State == LicenseState.Valid ? 0 : 3;
}

static int RunbookKeyGen(Dictionary<string, string> o)
{
    var dir = Required(o, "out");
    Directory.CreateDirectory(dir);
    var privatePath = Path.Combine(dir, "runbook-signing.private.pem");
    var publicPath = Path.Combine(dir, "runbook-vendor-public.pem");
    if (File.Exists(privatePath))
        throw new IOException($"{privatePath} zaten var; üzerine yazılmadı.");

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine($"Özel anahtar : {privatePath}  (GİZLİ — repoya eklemeyin)");
    Console.WriteLine($"Açık anahtar : {publicPath}  (parmak izi {SystemPanel.Contracts.Automation.RunbookPackageCodec.Fingerprint(key)})");
    Console.WriteLine("Üretici kataloğu için: açık anahtarı src/SystemPanel/Automation/runbook-vendor-public.pem olarak ekleyin.");
    Console.WriteLine("Kurum anahtarı için: açık anahtarı SystemPanel > Otomasyon > Katalog ve anahtarlar ekranından kaydedin.");
    return 0;
}

static int RunbookSign(Dictionary<string, string> o)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Required(o, "key")));
    var outPath = Required(o, "out");
    var (id, version) = SignRunbookDirectory(Required(o, "source"), key, outPath);
    Console.WriteLine($"İmzalandı: {id}@{version} -> {outPath}");
    return 0;
}

static int RunbookSignAll(Dictionary<string, string> o)
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Required(o, "key")));
    var outDir = Required(o, "out");
    Directory.CreateDirectory(outDir);

    var count = 0;
    foreach (var dir in Directory.GetDirectories(Required(o, "source")).OrderBy(d => d, StringComparer.Ordinal))
    {
        if (!File.Exists(Path.Combine(dir, "manifest.json")))
            continue;
        var name = Path.GetFileName(dir);
        var (id, version) = SignRunbookDirectory(dir, key, Path.Combine(outDir, name + ".sprb"));
        Console.WriteLine($"İmzalandı: {id}@{version}");
        count++;
    }
    Console.WriteLine($"{count} runbook imzalandı -> {outDir}");
    return count > 0 ? 0 : 1;
}

static (string Id, string Version) SignRunbookDirectory(string dir, ECDsa key, string outPath)
{
    var manifest = System.Text.Json.JsonSerializer.Deserialize<SystemPanel.Contracts.Automation.RunbookManifest>(
        File.ReadAllText(Path.Combine(dir, "manifest.json")),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new ArgumentException($"{dir}: manifest okunamadı.");
    var script = File.ReadAllText(Path.Combine(dir, "run.ps1"));
    File.WriteAllText(outPath, SystemPanel.Contracts.Automation.RunbookPackageCodec.Sign(manifest, script, key));
    return (manifest.Id, manifest.Version);
}

static int RunbookVerify(Dictionary<string, string> o)
{
    using var key = SystemPanel.Contracts.Automation.RunbookPackageCodec.ImportPublicKeyPem(File.ReadAllText(Required(o, "public")));
    if (!SystemPanel.Contracts.Automation.RunbookPackageCodec.TryVerify(File.ReadAllText(Required(o, "package")), key, out var pkg, out var error))
    {
        Console.Error.WriteLine("GEÇERSİZ: " + error);
        return 2;
    }
    Console.WriteLine($"Geçerli: {pkg!.Manifest.Id}@{pkg.Manifest.Version} | risk {pkg.Manifest.Risk} | betik sha256 {pkg.Manifest.ScriptSha256}");
    return 0;
}

static DateTime? Date(Dictionary<string, string> o, string name)
    => o.TryGetValue(name, out var v)
        ? DateTime.SpecifyKind(DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).AddTicks(-1), DateTimeKind.Utc)
        : null;

static string Required(Dictionary<string, string> o, string name)
    => o.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"--{name} gerekli.");

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Beklenmeyen argüman: " + args[i]);
        var name = args[i][2..];
        result[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
    }
    return result;
}

static int Usage()
{
    Console.WriteLine("""
        splicense keygen --out <klasör>
        splicense issue  --key <private.pem> --customer "<Kurum>" --out <dosya.lic> [--edition Standard|Enterprise|Evaluation]
                         [--expires yyyy-MM-dd] [--support yyyy-MM-dd] [--max-agents N] [--features ad,laps,gpo,sccm,monitoring,dashboard,sms] [--id <no>]
        splicense verify --public <public.pem> --license <dosya.lic>
        splicense runbook-keygen   --out <klasör>
        splicense runbook-sign     --key <private.pem> --source <runbook klasörü> --out <paket.sprb>
        splicense runbook-sign-all --key <private.pem> --source runbooks/src --out <klasör>
        splicense runbook-verify   --public <public.pem> --package <paket.sprb>
        """);
    return 1;
}
