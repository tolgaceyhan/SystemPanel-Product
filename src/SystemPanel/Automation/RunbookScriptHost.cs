using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.Automation
{
    public sealed record RunbookScriptResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    /// <summary>Doğrulanmış bir runbook paketini çalıştırır. Testlerde sahte uygulama kullanılır.</summary>
    public interface IRunbookScriptHost
    {
        Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct);
    }

    /// <summary>
    /// Windows PowerShell ile çalıştırma. Betik, yalnızca yöneticilerin ve uygulama kimliğinin erişebildiği
    /// çalışma klasörüne geçici olarak yazılır, yazılan içeriğin özeti manifestle yeniden karşılaştırılır ve
    /// <c>-File</c> + ayrı argümanlarla çalıştırılır (değerler kod olarak yorumlanmaz). Dosya sonra silinir.
    /// </summary>
    public sealed class PowerShellRunbookScriptHost : IRunbookScriptHost
    {
        private readonly AutomationOptions _options;
        private readonly ILogger<PowerShellRunbookScriptHost> _logger;

        public PowerShellRunbookScriptHost(IOptions<AutomationOptions> options, ILogger<PowerShellRunbookScriptHost> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct)
        {
            var workDir = _options.ResolveWorkDirectory();
            EnsureWorkDirectory(workDir);

            var scriptPath = Path.Combine(workDir, $"{package.Manifest.Id}-{package.Manifest.ScriptSha256[..12]}-{Guid.NewGuid():N}.ps1");
            // UTF-8 BOM: Windows PowerShell 5.1 BOM'suz dosyayı sistem kod sayfasıyla okur.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(package.Script)).ToArray();
            await File.WriteAllBytesAsync(scriptPath, bytes, ct);

            try
            {
                var written = await File.ReadAllBytesAsync(scriptPath, ct);
                var hash = RunbookPackageCodec.Sha256Hex(written.Skip(3).ToArray());
                if (hash != package.Manifest.ScriptSha256)
                    throw new InvalidOperationException("Yazılan betik doğrulanamadı (özet uyuşmuyor).");

                var psi = new ProcessStartInfo
                {
                    FileName = _options.PowerShellPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workDir,
                };
                foreach (var arg in BuildArguments(scriptPath, parameters, dryRun))
                    psi.ArgumentList.Add(arg);

                using var process = new Process { StartInfo = psi };
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);

                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(timeout);

                var timedOut = false;
                try
                {
                    await process.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    _logger.LogWarning("Runbook zaman aşımına uğradı: {Runbook}", package.Manifest.Id);
                }

                var stdout = await SafeAwait(stdoutTask);
                var stderr = await SafeAwait(stderrTask);
                return new RunbookScriptResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
            }
            finally
            {
                try { File.Delete(scriptPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Geçici runbook betiği silinemedi: {Path}", scriptPath);
                }
            }
        }

        /// <summary>Argüman listesi (birim testli). Değerler binder'da doğrulanmıştır.</summary>
        internal static IReadOnlyList<string> BuildArguments(string scriptPath, BoundRunbookParameters parameters, bool dryRun)
        {
            var args = new List<string>
            {
                "-NoLogo", "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-File", scriptPath,
            };

            foreach (var (name, value) in parameters.Values)
            {
                args.Add("-" + name);
                args.Add(value);
            }
            foreach (var name in parameters.Switches)
                args.Add("-" + name);
            if (dryRun)
                args.Add("-" + RunbookManifestValidator.DryRunParameter);

            return args;
        }

        private static void EnsureWorkDirectory(string path)
        {
            var created = !Directory.Exists(path);
            Directory.CreateDirectory(path);
            if (!created || !OperatingSystem.IsWindows())
                return;

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            var current = WindowsIdentity.GetCurrent().User;
            if (current != null)
                security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }

        private static async Task<string> SafeAwait(Task<string> task)
        {
            try { return await task; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { return string.Empty; }
        }
    }
}
