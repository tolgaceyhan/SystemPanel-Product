using System.Security.Cryptography;
using SystemPanel.Contracts.Automation;

// SystemPanel kurum runbook aracı (ADR-002 Faz 4). Kılavuz: docs/RUNBOOK-DEVELOPMENT.md
//
//   sprunbook keygen      --out <klasör>
//   sprunbook fingerprint --public <public.pem>
//   sprunbook new         --id org.<kurum>.<ad> --out <kaynak kökü> [--executor server|agent] [--risk low|medium|high] [--title "<başlık>"]
//   sprunbook lint        --source <runbook klasörü | kaynak kökü>
//   sprunbook sign        --key <private.pem> --source <runbook klasörü> --out <paket.sprb>
//   sprunbook sign-all    --key <private.pem> --source <kaynak kökü> --out <klasör>
//   sprunbook verify      --public <public.pem> --package <paket.sprb>
//
// Varsayılan olarak kurum kuralları uygulanır (kimlik "org." ile başlamalı). Üretici kataloğu --vendor ile imzalanır.
//
// ÖZEL ANAHTAR: repoya, paylaşılan klasöre veya SystemPanel sunucusuna koymayın. İmzalama, panelden ayrı ve
// erişimi kısıtlı bir makinede yapılır; panelde yalnızca AÇIK anahtar kayıtlıdır.

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
        return Usage();

    try
    {
        var options = ParseOptions(args.Skip(1).ToArray());
        return args[0] switch
        {
            "keygen" => KeyGen(options),
            "fingerprint" => Fingerprint(options),
            "new" => New(options),
            "lint" => Lint(options),
            "sign" => Sign(options),
            "sign-all" => SignAll(options),
            "verify" => Verify(options),
            _ => Usage(),
        };
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or CryptographicException or FormatException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("Hata: " + ex.Message);
        return 1;
    }
}

static bool Vendor(Dictionary<string, string> o) => o.ContainsKey("vendor");

static int KeyGen(Dictionary<string, string> o)
{
    var dir = Required(o, "out");
    Directory.CreateDirectory(dir);
    var privatePath = Path.Combine(dir, "runbook-org.private.pem");
    var publicPath = Path.Combine(dir, "runbook-org-public.pem");
    if (File.Exists(privatePath) || File.Exists(publicPath))
        throw new IOException($"{dir} içinde anahtar dosyası zaten var; üzerine yazılmadı.");

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
    RestrictToCurrentUser(privatePath);

    Console.WriteLine($"Özel anahtar : {privatePath}  (GİZLİ: repoya, paylaşılan klasöre veya panel sunucusuna koymayın)");
    Console.WriteLine($"Açık anahtar : {publicPath}");
    Console.WriteLine($"Parmak izi   : {RunbookPackageCodec.Fingerprint(key)}");
    Console.WriteLine("Sonraki adımlar:");
    Console.WriteLine("  1. Açık anahtarı SystemPanel > Otomasyon > Katalog ve anahtarlar ekranından kaydedin (automation.manage).");
    Console.WriteLine("  2. Agent runbook'ları için açık anahtarı agent'a da dağıtın (Install-SystemPanelAgent.ps1 -TrustedRunbookKeyFiles).");
    Console.WriteLine("  3. Özel anahtarı şifreli ve yedekli saklayın; sızarsa paneldeki kaydı hemen iptal edin.");
    return 0;
}

static int Fingerprint(Dictionary<string, string> o)
{
    using var key = RunbookPackageCodec.ImportPublicKeyPem(File.ReadAllText(Required(o, "public")));
    Console.WriteLine(RunbookPackageCodec.Fingerprint(key));
    return 0;
}

static int New(Dictionary<string, string> o)
{
    var id = Required(o, "id");
    var vendor = Vendor(o);
    if (!vendor && !RunbookNamespaces.IsOrganizationId(id))
        throw new ArgumentException($"Kurum runbook kimliği '{RunbookNamespaces.OrganizationPrefix}' ile başlamalı (ör. org.kurum.disk.report).");

    var dir = Path.Combine(Required(o, "out"), id);
    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
        throw new IOException($"{dir} boş değil; üzerine yazılmadı.");

    var (manifest, script) = RunbookTemplate.Create(
        id,
        o.GetValueOrDefault("executor") ?? RunbookExecutors.Server,
        o.GetValueOrDefault("risk") ?? RunbookRisks.Low,
        o.GetValueOrDefault("title"));

    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, RunbookSource.ManifestFile), manifest);
    File.WriteAllText(Path.Combine(dir, RunbookSource.ScriptFile), script);

    Console.WriteLine($"Oluşturuldu: {dir}");
    Console.WriteLine($"  {RunbookSource.ManifestFile}: açıklama, parametreler, risk ve zaman aşımı");
    Console.WriteLine($"  {RunbookSource.ScriptFile}: TODO bölümleri");
    Console.WriteLine($"Ardından: sprunbook lint --source \"{dir}\"");
    return 0;
}

static int Lint(Dictionary<string, string> o)
{
    var source = Required(o, "source");
    var dirs = File.Exists(Path.Combine(source, RunbookSource.ManifestFile)) ? new[] { source } : RunbookSource.FindDirectories(source).ToArray();
    if (dirs.Length == 0)
        throw new IOException($"{source}: runbook bulunamadı ({RunbookSource.ManifestFile} içeren klasör yok).");

    var failed = 0;
    foreach (var dir in dirs)
    {
        var findings = RunbookSource.Lint(RunbookSource.Load(dir), organization: !Vendor(o));
        var errors = findings.Count(f => f.Severity == RunbookLintSeverity.Error);
        Console.WriteLine($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(dir))}: {(errors == 0 ? "geçti" : "HATALI")} ({errors} hata, {findings.Count - errors} uyarı)");
        foreach (var finding in findings)
            Console.WriteLine("  " + finding);
        if (errors > 0)
            failed++;
    }

    return failed == 0 ? 0 : 2;
}

static int Sign(Dictionary<string, string> o)
{
    using var key = LoadPrivateKey(Required(o, "key"));
    var source = RunbookSource.Load(Required(o, "source"));
    var outPath = Required(o, "out");
    var package = RunbookSource.Sign(source, key, organization: !Vendor(o), out var findings);
    WriteWarnings(findings);
    File.WriteAllText(outPath, package);
    Console.WriteLine($"İmzalandı: {source.Manifest.Id}@{source.Manifest.Version} -> {outPath} (anahtar {RunbookPackageCodec.Fingerprint(key)[..16]}…)");
    return 0;
}

static int SignAll(Dictionary<string, string> o)
{
    using var key = LoadPrivateKey(Required(o, "key"));
    var outDir = Required(o, "out");
    var dirs = RunbookSource.FindDirectories(Required(o, "source"));

    // Önce hepsi denetlenir: biri bile hatalıysa hiçbir paket yazılmaz (yarım katalog oluşmaz).
    var packages = new List<(string Name, string Text, RunbookManifest Manifest)>();
    foreach (var dir in dirs)
    {
        var source = RunbookSource.Load(dir);
        var text = RunbookSource.Sign(source, key, organization: !Vendor(o), out var findings);
        WriteWarnings(findings, source.Manifest.Id);
        packages.Add((Path.GetFileName(dir), text, source.Manifest));
    }

    Directory.CreateDirectory(outDir);
    foreach (var (name, text, manifest) in packages)
    {
        File.WriteAllText(Path.Combine(outDir, name + ".sprb"), text);
        Console.WriteLine($"İmzalandı: {manifest.Id}@{manifest.Version}");
    }
    Console.WriteLine($"{packages.Count} runbook imzalandı -> {outDir}");
    return packages.Count > 0 ? 0 : 1;
}

static int Verify(Dictionary<string, string> o)
{
    using var key = RunbookPackageCodec.ImportPublicKeyPem(File.ReadAllText(Required(o, "public")));
    if (!RunbookPackageCodec.TryVerify(File.ReadAllText(Required(o, "package")), key, out var pkg, out var error))
    {
        Console.Error.WriteLine("GEÇERSİZ: " + error);
        return 2;
    }

    var m = pkg!.Manifest;
    Console.WriteLine($"Geçerli: {m.Id}@{m.Version} | {m.Executor} | risk {m.Risk} | önizleme {(m.SupportsDryRun ? "var" : "yok")} | betik sha256 {m.ScriptSha256}");
    foreach (var p in m.Parameters)
        Console.WriteLine($"  -{p.Name} ({p.Type}{(p.Required ? ", zorunlu" : string.Empty)})");
    return 0;
}

static ECDsa LoadPrivateKey(string path)
{
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(path));
    if (key.KeySize != 256)
        throw new ArgumentException("Yalnızca ECDSA P-256 anahtarları desteklenir.");
    return key;
}

static void WriteWarnings(IReadOnlyList<RunbookLintFinding> findings, string? prefix = null)
{
    foreach (var finding in findings.Where(f => f.Severity == RunbookLintSeverity.Warning))
        Console.WriteLine((prefix == null ? "  " : $"  {prefix}: ") + finding);
}

/// <summary>Unix'te özel anahtarı yalnızca sahibinin okuyabileceği şekilde kısıtlar (Windows'ta klasör ACL'ine güvenilir).</summary>
static void RestrictToCurrentUser(string path)
{
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}

static string Required(Dictionary<string, string> o, string name)
    => o.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) && v != "true" ? v : throw new ArgumentException($"--{name} gerekli.");

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
        sprunbook keygen      --out <klasör>
        sprunbook fingerprint --public <public.pem>
        sprunbook new         --id org.<kurum>.<ad> --out <kaynak kökü> [--executor server|agent] [--risk low|medium|high] [--title "<başlık>"]
        sprunbook lint        --source <runbook klasörü | kaynak kökü> [--vendor]
        sprunbook sign        --key <private.pem> --source <runbook klasörü> --out <paket.sprb> [--vendor]
        sprunbook sign-all    --key <private.pem> --source <kaynak kökü> --out <klasör> [--vendor]
        sprunbook verify      --public <public.pem> --package <paket.sprb>

        Kılavuz: docs/RUNBOOK-DEVELOPMENT.md
        """);
    return 1;
}
