using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.Automation
{
    /// <summary>Doğrulanmış bir runbook paketini çalıştırır. Testlerde sahte uygulama kullanılır.</summary>
    public interface IRunbookScriptHost
    {
        Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct);
    }

    /// <summary>Sunucu yürütücüsü: Windows PowerShell ile web uygulamasının kimliğinde çalıştırır (bkz. <see cref="RunbookProcessRunner"/>).</summary>
    public sealed class PowerShellRunbookScriptHost : IRunbookScriptHost
    {
        private readonly AutomationOptions _options;
        private readonly ILogger<PowerShellRunbookScriptHost> _logger;

        public PowerShellRunbookScriptHost(IOptions<AutomationOptions> options, ILogger<PowerShellRunbookScriptHost> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task<RunbookScriptResult> RunAsync(VerifiedRunbookPackage package, BoundRunbookParameters parameters, bool dryRun, TimeSpan timeout, CancellationToken ct)
            => RunbookProcessRunner.RunAsync(package, parameters, dryRun, timeout, _options.PowerShellPath, _options.ResolveWorkDirectory(),
                message => _logger.LogWarning("{Message}", message), ct);

        internal static IReadOnlyList<string> BuildArguments(string scriptPath, BoundRunbookParameters parameters, bool dryRun)
            => RunbookProcessRunner.BuildArguments(scriptPath, parameters, dryRun);
    }
}
