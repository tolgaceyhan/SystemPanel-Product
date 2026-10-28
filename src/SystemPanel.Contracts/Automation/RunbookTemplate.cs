using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SystemPanel.Contracts.Automation;

/// <summary>
/// Yeni runbook iskeleti (<c>sprunbook new</c>). Üretilen manifest ve betik denetimden (lint) hatasız geçer;
/// geliştirici yalnızca açıklamayı, parametreleri ve betik gövdesini doldurur.
/// </summary>
public static class RunbookTemplate
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static (string ManifestJson, string Script) Create(string id, string executor, string risk, string? title = null)
    {
        if (!RunbookExecutors.All.Contains(executor))
            throw new ArgumentException("executor: server veya agent olmalı.");
        if (!RunbookRisks.All.Contains(risk))
            throw new ArgumentException("risk: low, medium veya high olmalı.");

        var agent = executor == RunbookExecutors.Agent;
        var dryRun = risk != RunbookRisks.Low;
        var manifest = new
        {
            id,
            version = "1.0.0",
            title = title ?? id,
            description = "TODO: Runbook'un ne yaptığını, neyi değiştirdiğini ve önizlemenin ne gösterdiğini yazın.",
            executor,
            targetType = agent ? RunbookTargetTypes.Server : RunbookTargetTypes.AdOu,
            risk,
            supportsDryRun = dryRun,
            idempotent = false,
            timeoutSeconds = 120,
            parameters = new List<object>
            {
                agent
                    ? new { name = "Name", type = RunbookParameterTypes.String, label = "Ad", required = true, maxLength = 80, pattern = "[A-Za-z0-9_.-]{1,80}", help = "TODO" }
                    : new { name = "SearchBase", type = RunbookParameterTypes.AdDn, label = "Arama kökü (OU)", required = true, maxLength = 512, pattern = (string?)null, help = "Kapsamınızdaki bir OU" },
            },
        };

        var parameter = agent ? "Name" : "SearchBase";
        var paramLine = agent
            ? "    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Name"
            : "    [Parameter(Mandatory = $true)][string]$SearchBase";

        var script = $$"""
            <#
                SystemPanel runbook: {{id}} ({{executor}}, risk {{risk}})
                TODO: kısa açıklama.
                Kurallar (docs/RUNBOOK-DEVELOPMENT.md):
                  - Parametreler yalnızca param() bloğundan gelir; manifestteki adlarla aynı olmalı.
                  - Dinamik kod çalıştırma, başka süreç başlatma ve ağdan içerik indirme yasaktır (imzalama aracı reddeder).
                  - Sonuç stdout'a tek bir JSON nesnesi olarak yazılır. Parola/anahtar değerleri çıktıya yazılmaz.{{(dryRun ? "\n      - -DryRun: HİÇBİR değişiklik yapmadan ne yapılacağını JSON olarak döndürür." : string.Empty)}}
            #>
            [CmdletBinding()]
            param(
            {{paramLine}}{{(dryRun ? ",\n    [switch]$DryRun" : string.Empty)}}
            )

            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [Text.Encoding]::UTF8

            # TODO: hedefi oku ve planı hazırla (değişiklik yapmadan).
            $plan = [pscustomobject]@{
                target = ${{parameter}}
                actions = @('TODO')
            }

            """;

        if (dryRun)
        {
            script += """
                if ($DryRun) {
                    [pscustomobject]@{ dryRun = $true; plan = $plan } | ConvertTo-Json -Depth 4
                    exit 0
                }

                # TODO: değişikliği uygula. Hata durumunda istisna fırlatın; çıkış kodu 0 dışı olur ve çalıştırma Failed olarak kaydedilir.

                """;
        }

        script += """
            [pscustomobject]@{ dryRun = $false; result = $plan } | ConvertTo-Json -Depth 4

            """;

        return (JsonSerializer.Serialize(manifest, JsonOptions) + "\n", script.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
