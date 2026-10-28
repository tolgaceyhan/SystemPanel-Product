using System;
using System.Collections.Generic;
using SystemPanel.Models;

namespace SystemPanel.ViewModels.Server
{
    public class ServerDetailViewModel
    {
        // -----------------------------
        // AD BİLGİLERİ
        // -----------------------------
        public string Name { get; set; } = string.Empty;
        public string? OperatingSystem { get; set; }
        public string? Description { get; set; }
        public string? DistinguishedName { get; set; }
        public DateTime? LastLogonDate { get; set; }

        // -----------------------------
        // ANLIK METRİKLER
        // -----------------------------
        public int CpuUsage { get; set; }
        public int RamUsage { get; set; }

        // -----------------------------
        // ZAMAN SERİSİ CPU/RAM GRAFİKLERİ
        // -----------------------------
        public List<ServerMetricHistory> MetricHistory { get; set; } = new();

        // -----------------------------
        // ÇOKLU DİSK TARİHÇESİ (C:, D:, E:)
        // -----------------------------
        public Dictionary<string, List<ServerDiskHistory>> DiskHistory { get; set; } = new();
    }
}
