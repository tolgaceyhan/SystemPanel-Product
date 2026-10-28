using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Models;

namespace SystemPanel.Data
{

    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Veritabanındaki tabloları burada tanımlıyoruz
        public DbSet<AdUser> AdUsers { get; set; }

        public DbSet<Announcements> Announcements { get; set; }  // Announcements tablosu
        public DbSet<SccmLog> SccmLogs { get; set; } // Diğer tablolar
        public DbSet<Sms> Smsler { get; set; }
        public DbSet<SmsGidenler> SmsGidenler { get; set; }
        public DbSet<MapUnit> MapUnits { get; set; }
        public DbSet<Module> Modules { get; set; }
        public DbSet<AlarmLog> AlarmLogs { get; set; }
        public DbSet<ServerInfo> ServerInfos { get; set; }
        public DbSet<ServerLog> ServerLogs { get; set; }
        public DbSet<ServerMetricHistory> ServerMetricHistory { get; set; }
        public DbSet<ServerDiskHistory> ServerDiskHistory { get; set; }
        public DbSet<AdLogEntry> AdLogs { get; set; }
        public DbSet<LatestServerMetrics> LatestServerMetrics => Set<LatestServerMetrics>();
 
        // 🔥 AUTO-HEAL
        public DbSet<AutoHealState> AutoHealStates { get; set; }
        public DbSet<AutoHealLog> AutoHealLogs { get; set; }
        public DbSet<AutoHealSettings> AutoHealSettings { get; set; }

        public DbSet<SystemDashboardLink> SystemDashboardLinks { get; set; }

        // Güvenlik / denetim
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

        // Agent kayıtları
        public DbSet<Agent> Agents => Set<Agent>();



        // API AJAN ICIN

        // LogEntries tablosu
        public DbSet<LogEntry> LogEntries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Identity composite PK ayarların aynen kalsın...
            modelBuilder.Entity<IdentityUserLogin<string>>(entity =>
            {
                entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });
            });

            modelBuilder.Entity<IdentityUserRole<string>>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.RoleId });
            });

            modelBuilder.Entity<IdentityUserToken<string>>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });
            });

            modelBuilder.Entity<MapUnit>().HasNoKey();
            modelBuilder.Entity<Sms>().HasKey(s => s.Id);
            modelBuilder.Entity<SmsGidenler>().HasKey(sg => sg.Id);

            // 🔹 PERFORMANS İÇİN ÖNEMLİ INDEXLER
            modelBuilder.Entity<ServerMetricHistory>()
      .HasIndex(s => s.CreatedAt);

            modelBuilder.Entity<AlarmLog>()
                .HasIndex(a => a.CreatedAt);

            modelBuilder.Entity<AuditEvent>(e =>
            {
                e.HasIndex(a => a.TimestampUtc);
                e.HasIndex(a => new { a.Action, a.TimestampUtc });
                e.HasIndex(a => a.ActorUserName);
                e.HasIndex(a => new { a.ResourceType, a.ResourceId });
            });

            modelBuilder.Entity<Agent>(e =>
            {
                e.HasIndex(a => a.Hostname).IsUnique();
                e.HasIndex(a => a.LastSeenUtc);
            });

            modelBuilder.Entity<RolePermission>()
                .HasIndex(r => new { r.RoleName, r.Permission })
                .IsUnique();


        }


    } 
}
