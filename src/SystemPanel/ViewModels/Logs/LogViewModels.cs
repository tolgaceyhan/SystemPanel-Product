using System;
using System.Collections.Generic;
using SystemPanel.Models;

namespace SystemPanel.ViewModels.Logs
{
    // NOT: Bu klasör, .gitignore'daki genel "[Ll]ogs/" kalıbı yüzünden hiç commit
    // edilmemişti. Sınıflar LogsController/LogService'in kullandığı alanlara göre
    // yeniden oluşturuldu. Geliştirici makinesindeki asıl dosyalar bulunursa
    // karşılaştırılmalıdır (bkz. docs/CODEBASE_AUDIT.md §21).

    public class FilterLogsViewModel
    {
        public string? MessageContains { get; set; }
        public string? Level { get; set; }
        public string? User { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public List<LogEntry> Logs { get; set; } = new();
    }

    public class ClearLogsViewModel
    {
        public bool Confirm { get; set; }

        // "All" veya LogEntry.Level değeri (Info, Warning, Error...)
        public string? LogType { get; set; } = "All";

        public DateTime? BeforeDate { get; set; }
    }

    public class LogDetailViewModel
    {
        public int Id { get; set; }
        public string? Level { get; set; }
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
        public string? User { get; set; }
        public string? IP { get; set; }
    }

    public class RelatedLogsViewModel
    {
        public LogEntry CurrentLog { get; set; } = null!;
        public IEnumerable<LogEntry> RelatedLogs { get; set; } = Array.Empty<LogEntry>();
    }
}
