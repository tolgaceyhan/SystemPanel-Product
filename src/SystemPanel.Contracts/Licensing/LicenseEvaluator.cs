using System;

namespace SystemPanel.Contracts.Licensing;

public enum LicenseState
{
    /// <summary>Ürüne doğrulama anahtarı gömülmemiş (geliştirme yapısı). Kısıtlama uygulanmaz.</summary>
    NotConfigured,
    /// <summary>Lisans dosyası yok.</summary>
    Missing,
    /// <summary>İmza veya içerik geçersiz.</summary>
    Invalid,
    Valid,
    /// <summary>Süresi doldu, ek süre (grace) içinde: tüm özellikler çalışır, uyarı gösterilir.</summary>
    GracePeriod,
    /// <summary>Süresi ve ek süresi doldu.</summary>
    Expired,
}

public sealed record LicenseStatus(LicenseState State, LicenseInfo? License, string? Message, DateTime? GraceEndsUtc = null)
{
    /// <summary>Lisans kurallarının (ör. agent sınırı) lisanstaki değerlere göre uygulandığı durumlar.</summary>
    public bool IsLicensed => State is LicenseState.Valid or LicenseState.GracePeriod;

    public bool ShowWarning => State is not (LicenseState.Valid or LicenseState.NotConfigured);
}

public static class LicenseEvaluator
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    public static LicenseStatus Evaluate(LicenseInfo license, DateTime nowUtc)
    {
        if (license.ExpiresUtc is not { } expires || nowUtc <= expires)
            return new LicenseStatus(LicenseState.Valid, license, null);

        var graceEnds = expires + GracePeriod;
        return nowUtc <= graceEnds
            ? new LicenseStatus(LicenseState.GracePeriod, license, $"Lisans süresi {expires:yyyy-MM-dd} tarihinde doldu. Ek süre {graceEnds:yyyy-MM-dd} tarihinde bitiyor.", graceEnds)
            : new LicenseStatus(LicenseState.Expired, license, $"Lisans süresi {expires:yyyy-MM-dd} tarihinde doldu.", graceEnds);
    }
}
