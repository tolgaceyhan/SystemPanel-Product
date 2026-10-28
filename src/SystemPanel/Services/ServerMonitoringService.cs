using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SystemPanel.Models;
using SystemPanel.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Configuration;

namespace SystemPanel.Services
{
    public class ServerMonitoringService
    {
        private readonly ILogger<ServerMonitoringService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;

        public ServerMonitoringService(
            ILogger<ServerMonitoringService> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
        }

        public List<string> GetSavedServerNames()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return context.ServerInfos.Select(s => s.Name).ToList();
        }

        public List<ServerInfo> GetAllServerInfo()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return context.ServerInfos.ToList();
        }

        public void AddServersFromAdIfNotExist(List<string> adServerNames)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = context.ServerInfos.Select(s => s.Name).ToList();

            var newServers = adServerNames.Except(existing).ToList();
            foreach (var name in newServers)
            {
                context.ServerInfos.Add(new ServerInfo
                {
                    Name = name,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            context.SaveChanges();
        }

        public List<ServerMetrics> GetAllMetrics(List<string> serverNames)
        {
            var list = new List<ServerMetrics>();
            foreach (var name in serverNames)
            {
                var m = GetServerMetrics(name);
                list.Add(m);
            }
            return list;
        }

        public ServerMetrics GetServerMetrics(string serverName)
        {
            var metrics = new ServerMetrics
            {
                ServerName = serverName,
                CpuUsage = 0,
                RamUsage = 0,
                DiskUsage = 0,
            };

            try
            {
                var username = _configuration["WinRM:Username"];
                var password = _configuration["WinRM:Password"];

                string script = $@"
                    $cpu = Get-CimInstance Win32_Processor -ComputerName {serverName} | Measure-Object -Property LoadPercentage -Average | Select -ExpandProperty Average
                    $os = Get-CimInstance Win32_OperatingSystem -ComputerName {serverName}
                    $total = $os.TotalVisibleMemorySize
                    $free = $os.FreePhysicalMemory
                    $ram = (($total - $free) / $total) * 100

                    $disks = Get-CimInstance Win32_LogicalDisk -ComputerName {serverName} -Filter 'DriveType=3'
                    $diskUsage = 0
                    foreach ($d in $disks) {{
                        if ($d.Size -ne $null -and $d.FreeSpace -ne $null) {{
                            $diskUsage += (($d.Size - $d.FreeSpace) / $d.Size) * 100
                        }}
                    }}
                    $disk = if ($disks.Count -gt 0) {{ $diskUsage / $disks.Count }} else {{ 0 }}

                    [PSCustomObject]@{{
                        CpuUsage = [math]::Round($cpu, 0)
                        RamUsage = [math]::Round($ram, 0)
                        DiskUsage = [math]::Round($disk, 0)
                    }} | ConvertTo-Json
                ";

                WSManConnectionInfo connectionInfo = new WSManConnectionInfo(
                    new Uri($"http://{serverName}:5985/wsman"),
                    "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                    new PSCredential(username, CreateSecureString(password))
                )
                {
                    SkipCACheck = true,
                    SkipCNCheck = true,
                    OperationTimeout = 4 * 60 * 1000,
                    OpenTimeout = 1 * 60 * 1000
                };

                using var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
                runspace.Open();

                using var pipeline = runspace.CreatePipeline();
                pipeline.Commands.AddScript(script);
                var results = pipeline.Invoke();

                if (results.Count > 0)
                {
                    var json = results[0].ToString();
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<ServerMetrics>(json);
                    if (parsed != null)
                    {
                        metrics.CpuUsage = parsed.CpuUsage;
                    metrics.RamUsage = parsed.RamUsage;
                    metrics.DiskUsage = parsed.DiskUsage;
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WinRM metrik çekim hatası: {serverName}");
            }

            return metrics;
        }

        private System.Security.SecureString CreateSecureString(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                throw new ArgumentNullException(nameof(plain), "The input string cannot be null or empty.");

            var secure = new System.Security.SecureString();
            foreach (char c in plain)
                secure.AppendChar(c);
            secure.MakeReadOnly();
            return secure;
        }
    }
}
