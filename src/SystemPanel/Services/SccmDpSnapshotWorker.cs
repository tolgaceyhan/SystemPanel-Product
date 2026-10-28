using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using SystemPanel.Services;
using SystemPanel.Models;

public class SccmDpSnapshotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;

    public SccmDpSnapshotWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("SCCM SNAPSHOT WORKER STARTED");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var snapshotService =
                    scope.ServiceProvider.GetRequiredService<SccmSnapshotService>();

                Console.WriteLine("WORKER: Reading DP snapshot source...");
                var dps = snapshotService.GetDpSnapshots();

                Console.WriteLine($"WORKER: DP COUNT = {dps.Count}");

                WriteSnapshot(dps);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SNAPSHOT WORKER ERROR: " + ex);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private void WriteSnapshot(List<SccmDpSnapshot> dps)
    {
        using var conn = new SqlConnection(
            _cfg.GetConnectionString("DefaultConnection"));

        conn.Open();

        foreach (var dp in dps)
        {
            try
            {
                using var cmd = new SqlCommand(@"
MERGE SccmDpSnapshot AS t
USING (SELECT @ServerName AS ServerName) AS s
ON t.ServerName = s.ServerName
WHEN MATCHED THEN UPDATE SET
    SiteCode=@SiteCode,
    IpAddress=@Ip,
    TotalServices=@Total,
    RunningServices=@Running,
    CriticalDownCount=@Critical,
    HealthScore=@Health,
    LastUpdated=GETDATE()
WHEN NOT MATCHED THEN INSERT
(ServerName,SiteCode,IpAddress,TotalServices,RunningServices,CriticalDownCount,HealthScore,LastUpdated)
VALUES
(@ServerName,@SiteCode,@Ip,@Total,@Running,@Critical,@Health,GETDATE());
", conn);

                cmd.Parameters.AddWithValue("@ServerName", dp.ServerName);
                cmd.Parameters.AddWithValue("@SiteCode", dp.SiteCode ?? "");
                cmd.Parameters.AddWithValue("@Ip", dp.IpAddress ?? "");
                cmd.Parameters.AddWithValue("@Total", dp.TotalServices);
                cmd.Parameters.AddWithValue("@Running", dp.RunningServices);
                cmd.Parameters.AddWithValue("@Critical", dp.CriticalDownCount);
                cmd.Parameters.AddWithValue("@Health", dp.HealthScore);

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SNAPSHOT WRITE FAILED ({dp.ServerName}): {ex.Message}");
            }
        }
    }
}
