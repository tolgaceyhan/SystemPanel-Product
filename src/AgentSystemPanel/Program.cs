using AgentSystemPanel;
using AgentSystemPanel.Collectors;
using AgentSystemPanel.Transport;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Kurulum betiğinin yazdığı makineye özel ayarlar (sunucu adresi, AgentId, DPAPI korumalı anahtar).
// Program klasörü dışında tutulur; agent güncellemelerinde korunur.
var machineSettings = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Agent", "agentsettings.json");
builder.Configuration.AddJsonFile(machineSettings, optional: true, reloadOnChange: false);
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

builder.Build().Run();
