using System;
using System.Collections.Generic;

namespace SystemPanel.Contracts.Automation;

/// <summary>
/// Runbook paketinin tanımı (ADR-002). Paket = manifest + tek bir PowerShell betiği + imza.
/// Betik değiştirilirse <see cref="ScriptSha256"/> tutmaz; manifest değiştirilirse imza tutmaz.
/// </summary>
public sealed class RunbookManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Ör. "ad.user.offboard". Küçük harf, nokta ile ayrılmış.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>SemVer (ör. "1.2.0").</summary>
    public string Version { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Şimdilik yalnızca <see cref="RunbookExecutors.Server"/> (ADR-002 Faz 1).</summary>
    public string Executor { get; set; } = RunbookExecutors.Server;

    public string TargetType { get; set; } = RunbookTargetTypes.None;

    public string Risk { get; set; } = RunbookRisks.High;

    /// <summary>Betik -DryRun ile çağrıldığında değişiklik yapmadan planı döndürür.</summary>
    public bool SupportsDryRun { get; set; }

    /// <summary>Aynı parametrelerle tekrar çalıştırılması güvenli mi (yeniden deneme için).</summary>
    public bool Idempotent { get; set; }

    public int TimeoutSeconds { get; set; } = 300;

    public List<RunbookParameter> Parameters { get; set; } = new();

    /// <summary>Betik içeriğinin SHA-256 özeti (küçük harf hex). İmzalama sırasında doldurulur.</summary>
    public string ScriptSha256 { get; set; } = string.Empty;
}

public sealed class RunbookParameter
{
    /// <summary>PowerShell parametre adı (ör. "UserDn"). "DryRun" ayrılmıştır.</summary>
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = RunbookParameterTypes.String;

    /// <summary>Formda görünen ad.</summary>
    public string? Label { get; set; }

    public string? Help { get; set; }

    public bool Required { get; set; } = true;

    public string? Default { get; set; }

    /// <summary>string: en fazla uzunluk (varsayılan 256).</summary>
    public int? MaxLength { get; set; }

    /// <summary>string: değerin TAMAMI bu düzenli ifadeye uymalı.</summary>
    public string? Pattern { get; set; }

    /// <summary>int: aralık.</summary>
    public long? Min { get; set; }
    public long? Max { get; set; }

    /// <summary>enum: izin verilen değerler.</summary>
    public List<string> Values { get; set; } = new();

    /// <summary>ad-dn: değer, talep eden (ve onaylayan) kullanıcının organizasyonel kapsamında olmalı.</summary>
    public bool InScope { get; set; } = true;
}

public static class RunbookExecutors
{
    public const string Server = "server";
    public static readonly IReadOnlyList<string> All = new[] { Server };
}

public static class RunbookTargetTypes
{
    public const string None = "none";
    public const string AdUser = "ad-user";
    public const string AdComputer = "ad-computer";
    public const string AdOu = "ad-ou";
    public static readonly IReadOnlyList<string> All = new[] { None, AdUser, AdComputer, AdOu };
}

public static class RunbookRisks
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public static readonly IReadOnlyList<string> All = new[] { Low, Medium, High };
}

public static class RunbookParameterTypes
{
    public const string String = "string";
    public const string Int = "int";
    public const string Bool = "bool";
    public const string Enum = "enum";
    public const string AdDn = "ad-dn";
    public const string Hostname = "hostname";
    public static readonly IReadOnlyList<string> All = new[] { String, Int, Bool, Enum, AdDn, Hostname };
}
