using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Management;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class SccmLogService
    {
        private readonly string _sqlConnectionString;
        private readonly string? _wmiUsername;
        private readonly string? _wmiPassword;

        private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;

        /// <summary>WQL dize sabiti için kaçış (\ ve ').</summary>
        internal static string EscapeWql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

        public SccmLogService(IConfiguration configuration)
        {
            _sqlConnectionString =
                $"Server={configuration["SCCM:SqlServer"]};" +
                $"Database={configuration["SCCM:Database"]};" +
                $"User Id={configuration["SCCM:SqlUser"]};" +
                $"Password={configuration["SCCM:SqlPassword"]};" +
                $"TrustServerCertificate=True;Encrypt=False;";

            // appsettings anahtarları WmiUsername/WmiPassword'dür; eski kod var olmayan
            // SCCM:Username/Password anahtarlarını okuyordu (WMI uygulama kimliğiyle çalışıyordu).
            // Boş bırakılırsa uygulama havuzu / servis hesabı (gMSA önerilir) kullanılır.
            _wmiUsername = NullIfEmpty(configuration["SCCM:WmiUsername"] ?? configuration["SCCM:Username"]);
            _wmiPassword = NullIfEmpty(configuration["SCCM:WmiPassword"] ?? configuration["SCCM:Password"]);
        }

        // =====================================================
        // 🔴 CANLI WMI – TEK DP DETAIL
        // =====================================================
        public SccmDpServerInfo GetDpDetails(string serverName)
        {
            var dp = new SccmDpServerInfo
            {
                ServerName = serverName
            };

            try
            {
                var options = new ConnectionOptions
                {
                    Username = _wmiUsername,
                    Password = _wmiPassword,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy,
                    EnablePrivileges = true
                };

                var host = serverName.Split('.')[0];
                var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
                scope.Connect();

                var serviceNames = new[]
                {
                    "CcmExec",
                    "SMS_EXECUTIVE",
                    "WDS",
                    "WDSServer"
                };

                foreach (var svcName in serviceNames)
                {
                    using var searcher = new ManagementObjectSearcher(
                        scope,
                        new ObjectQuery(
                            $"SELECT Name, State, Status FROM Win32_Service WHERE Name='{EscapeWql(svcName)}'"));

                    foreach (ManagementObject svc in searcher.Get())
                    {
                        dp.Services.Add(new ServiceStatus
                        {
                            ServiceName = svc["Name"]?.ToString(),
                            State = svc["State"]?.ToString(),
                            RawStatus = svc["Status"]?.ToString()
                        });
                    }
                }

                // IP ADDRESS
                using var ipSearcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery(
                        "SELECT IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"));

                foreach (ManagementObject ip in ipSearcher.Get())
                {
                    if (ip["IPAddress"] is string[] ips)
                    {
                        dp.IpAddress = ips.FirstOrDefault(x =>
                            x.Contains(".") && !x.StartsWith("169.254"));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                dp.Services.Clear();
                dp.Services.Add(new ServiceStatus
                {
                    ServiceName = "WMI",
                    State = "ERROR",
                    RawStatus = "FAIL",
                    Note = ex.Message
                });
            }

            return dp;
        }

        // =====================================================
        // 🔧 SERVICE CONTROL
        // =====================================================
        public void ControlService(string serverName, string serviceName, bool start)
        {
            var options = new ConnectionOptions
            {
                Username = _wmiUsername,
                Password = _wmiPassword,
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            };

            var host = serverName.Split('.')[0];
            var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    $"SELECT * FROM Win32_Service WHERE Name='{EscapeWql(serviceName)}'"));

            foreach (ManagementObject svc in searcher.Get())
            {
                svc.InvokeMethod(start ? "StartService" : "StopService", null);
            }
        }
    }
}
