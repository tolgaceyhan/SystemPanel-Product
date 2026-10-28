using AgentSystemPanel;
using AgentSystemPanel.Collectors;
using AgentSystemPanel.Transport;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Kurulum betiğinin yazdığı makineye özel ayarlar (sunucu adresi, AgentId, DPAPI korumalı anahtar).
// Program klasörü dışında tutulur; agent güncellemelerinde korunur.
builder.Configuration.AddJsonFile(AgentPaths.MachineSettingsFile, optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables(prefix: "SYSTEMPANEL_AGENT_");

builder.Services.AddWindowsService(o => o.ServiceName = "SystemPanelAgent");
if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog(new Microsoft.Extensions.Logging.EventLog.EventLogSettings { SourceName = "SystemPanelAgent" });

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddHttpClient(AgentApiClient.HttpClientName, (sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 2, 120));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SystemPanel-Agent/2");
});

builder.Services.AddSingleton<AgentApiClient>();
builder.Services.AddSingleton(sp => new MetricBuffer(sp.GetRequiredService<IOptions<AgentOptions>>().Value.BufferMaxItems));
builder.Services.AddSingleton<IMetricCollector, CpuCollector>();
builder.Services.AddSingleton<IMetricCollector, MemoryCollector>();
builder.Services.AddSingleton<IMetricCollector, DiskCollector>();
builder.Services.AddHostedService<AgentWorker>();

// Runbook işleri (ADR-002 Faz 2). Agent:Automation:Enabled=false (varsayılan) ise çalışan hemen durur.
builder.Services.AddSingleton<AgentSystemPanel.Automation.IAgentScriptRunner, AgentSystemPanel.Automation.PowerShellAgentScriptRunner>();
builder.Services.AddHostedService<AgentSystemPanel.Automation.AgentCommandWorker>();

builder.Build().Run();
