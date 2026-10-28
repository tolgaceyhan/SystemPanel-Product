using Microsoft.Data.SqlClient;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class SccmSnapshotService
    {
        private readonly string _conn;

        public SccmSnapshotService(IConfiguration cfg)
        {
            _conn = cfg.GetConnectionString("DefaultConnection");
        }

        // ============================
        // READ SNAPSHOTS (UI SAFE)
        // ============================
        public List<SccmDpSnapshot> GetDpSnapshots()
        {
            var list = new List<SccmDpSnapshot>();

            try
            {
                using var conn = new SqlConnection(_conn);
                conn.Open();

                using var cmd = new SqlCommand(@"
                    SELECT 
                        ServerName,
                        SiteCode,
                        IpAddress,
                        TotalServices,
                        RunningServices,
                        CriticalDownCount,
                        HealthScore,
                        LastUpdated
                    FROM SccmDpSnapshot
                    ORDER BY ServerName
                ", conn);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new SccmDpSnapshot
                    {
                        ServerName = r.GetString(0),
                        SiteCode = r.IsDBNull(1) ? "" : r.GetString(1),
                        IpAddress = r.IsDBNull(2) ? "" : r.GetString(2),
                        TotalServices = r.GetInt32(3),
                        RunningServices = r.GetInt32(4),
                        CriticalDownCount = r.GetInt32(5),
                        HealthScore = r.GetInt32(6),
                        LastUpdated = r.GetDateTime(7)
                    });
                }
            }
            catch
            {
                // UI asla çökmez
                return new List<SccmDpSnapshot>();
            }

            return list;
        }
    }
}
