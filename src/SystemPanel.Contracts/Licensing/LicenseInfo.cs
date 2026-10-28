using System;
using System.Collections.Generic;

namespace SystemPanel.Contracts.Licensing;

/// <summary>
/// Lisans içeriği. Üretici (vendor) özel anahtarıyla imzalanır; ürün yalnızca gömülü açık
/// anahtarla doğrular. Lisans çevrimdışı çalışır (kapalı ağ): sunucuya "telefon etme" yoktur.
/// </summary>
public sealed class LicenseInfo
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string LicenseId { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Edition { get; set; } = LicenseEditions.Standard;
    public DateTime IssuedUtc { get; set; }

    /// <summary>Null = süresiz (perpetual) kullanım hakkı.</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>Güncelleme/destek hakkının bittiği tarih (süresiz lisanslarda bakım sözleşmesi).</summary>
    public DateTime? SupportExpiresUtc { get; set; }

    /// <summary>Null = sınırsız.</summary>
    public int? MaxAgents { get; set; }

    public List<string> Features { get; set; } = new();
}

public static class LicenseEditions
{
    public const string Evaluation = "Evaluation";
    public const string Standard = "Standard";
    public const string Enterprise = "Enterprise";
}

/// <summary>Modül anahtarları (ileride modül bazlı lisanslama için).</summary>
public static class LicenseFeatures
{
    public const string ActiveDirectory = "ad";
    public const string Laps = "laps";
    public const string Gpo = "gpo";
    public const string Sccm = "sccm";
    public const string Monitoring = "monitoring";
    public const string Dashboard = "dashboard";
    public const string Sms = "sms";

    public static readonly IReadOnlyList<string> All = new[] { ActiveDirectory, Laps, Gpo, Sccm, Monitoring, Dashboard, Sms };
}
