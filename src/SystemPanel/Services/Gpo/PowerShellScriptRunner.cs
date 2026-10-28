using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SystemPanel.Services.Gpo
{
    public sealed record PowerShellScriptResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    public interface IPowerShellScriptRunner
    {
        /// <summary>
        /// <paramref name="scriptFileName"/> betiğini (yalnızca scripts/ klasöründeki, kodda sabit adlar)
        /// adlandırılmış parametrelerle çalıştırır. Parametreler <c>-File</c> ile ayrı argüman olarak
        /// geçirilir; PowerShell bunları KOD olarak değerlendirmez (önceki <c>-Command "..."</c> enjeksiyonu kapandı).
        /// </summary>
        Task<PowerShellScriptResult> RunAsync(
            string scriptFileName,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken ct = default);
    }

    public sealed class PowerShellScriptRunner : IPowerShellScriptRunner
    {
        private static readonly Regex ParameterName = new("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.Compiled);
        private static readonly Regex ScriptFileName = new(@"^[A-Za-z0-9\-]+\.ps1$", RegexOptions.Compiled);

        private readonly GpoOptions _options;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PowerShellScriptRunner> _logger;

        public PowerShellScriptRunner(IOptions<GpoOptions> options, IWebHostEnvironment env, ILogger<PowerShellScriptRunner> logger)
        {
            _options = options.Value;
            _env = env;
            _logger = logger;
        }

        public async Task<PowerShellScriptResult> RunAsync(
            string scriptFileName,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken ct = default)
        {
            var scriptPath = ResolveScriptPath(scriptFileName);

            var psi = new ProcessStartInfo
            {
                FileName = _options.PowerShellPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var arg in BuildArguments(scriptPath, parameters))
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.ScriptTimeoutSeconds, 5, 600)));

            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); } catch { /* süreç zaten bitmiş olabilir */ }
                _logger.LogWarning("PowerShell betiği zaman aşımına uğradı: {Script}", scriptFileName);
            }

            string stdout = await SafeAwait(stdoutTask);
            string stderr = await SafeAwait(stderrTask);

            return new PowerShellScriptResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
        }

        private static async Task<string> SafeAwait(Task<string> task)
        {
            try { return await task; } catch { return string.Empty; }
        }

        private string ResolveScriptPath(string scriptFileName)
        {
            if (!ScriptFileName.IsMatch(scriptFileName))
                throw new ArgumentException("Geçersiz betik adı.", nameof(scriptFileName));

            var root = string.IsNullOrWhiteSpace(_options.ScriptsDirectory)
                ? Path.Combine(_env.ContentRootPath, "scripts")
                : Path.GetFullPath(_options.ScriptsDirectory, _env.ContentRootPath);

            var path = Path.GetFullPath(Path.Combine(root, scriptFileName));
            if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Betik yolu izin verilen klasörün dışında.", nameof(scriptFileName));

            if (!File.Exists(path))
                throw new FileNotFoundException("GPO betiği bulunamadı.", scriptFileName);

            return path;
        }

        /// <summary>Argüman listesini üretir (birim testlerinde doğrulanır).</summary>
        internal static IReadOnlyList<string> BuildArguments(string scriptPath, IReadOnlyDictionary<string, string> parameters)
        {
            var args = new List<string>
            {
                "-NoLogo", "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-File", scriptPath,
            };

            foreach (var (name, value) in parameters)
            {
                if (!ParameterName.IsMatch(name))
                    throw new ArgumentException($"Geçersiz parametre adı: {name}");

                ValidateValue(name, value);
                args.Add("-" + name);
                args.Add(value);
            }

            return args;
        }

        internal static void ValidateValue(string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"{name} boş olamaz.");

            // "-" ile başlayan değer PowerShell tarafından başka bir parametre adı sanılabilir;
            // tırnak ve kontrol karakterleri powershell.exe komut satırı ayrıştırmasını etkiler.
            if (value[0] == '-' || value.Any(c => c == '"' || char.IsControl(c)) || value.Length > 2048)
                throw new ArgumentException($"{name} geçersiz karakter içeriyor.");
        }
    }

    public sealed class GpoOptions
    {
        public const string SectionName = "Gpo";

        /// <summary>GroupPolicy modülü Windows PowerShell 5.1 gerektirir.</summary>
        public string PowerShellPath { get; set; } = "powershell.exe";

        /// <summary>Boşsa ContentRoot/scripts.</summary>
        public string? ScriptsDirectory { get; set; }

        public int ScriptTimeoutSeconds { get; set; } = 120;

        /// <summary>Boşsa %ProgramData%\SystemPanel\GpoBackups.</summary>
        public string? BackupRoot { get; set; }

        /// <summary>GPO silme. Varsayılan KAPALI (geri alınamaz işlem).</summary>
        public bool AllowDelete { get; set; }

        /// <summary>Yedekten içe aktarma. Varsayılan KAPALI (mevcut GPO ayarlarının üzerine yazar).</summary>
        public bool AllowImport { get; set; }

        public long MaxImportBytes { get; set; } = 50 * 1024 * 1024;
    }
}
