using System.Security.Cryptography;
using SystemPanel.Contracts.Automation;

namespace SystemPanel.UnitTests.Automation;

/// <summary>runbooks/examples: kurum kurallarıyla (org. ön eki) hatasız denetlenir ve imzalanabilir.</summary>
public sealed class ExampleRunbooksTests
{
    private static string ExamplesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SystemPanel.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "runbooks", "examples");
    }

    public static IEnumerable<object[]> Examples()
        => RunbookSource.FindDirectories(ExamplesRoot()).Select(d => new object[] { Path.GetFileName(d) });

    [Fact]
    public void ExampleLibrary_CoversBothExecutorsAndRiskLevels()
    {
        var manifests = RunbookSource.FindDirectories(ExamplesRoot()).Select(d => RunbookSource.Load(d).Manifest).ToList();
        Assert.True(manifests.Count >= 4);
        Assert.Contains(manifests, m => m.Executor == RunbookExecutors.Agent);
        Assert.Contains(manifests, m => m.Executor == RunbookExecutors.Server);
        Assert.Contains(manifests, m => m.Risk == RunbookRisks.Medium && m.SupportsDryRun);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Example_PassesOrganizationLint_WithoutFindings(string name)
    {
        var source = RunbookSource.Load(Path.Combine(ExamplesRoot(), name));
        Assert.StartsWith("org.example.", source.Manifest.Id);
        Assert.Empty(RunbookSource.Lint(source, organization: true).Select(f => f.ToString()));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Example_CanBeSignedAndVerified(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = RunbookSource.Sign(RunbookSource.Load(Path.Combine(ExamplesRoot(), name)), key, organization: true, out _);
        Assert.True(RunbookPackageCodec.TryVerify(package, key, out var verified, out var error), error);
        Assert.Equal(name, verified!.Manifest.Id);
    }

    [Fact]
    public void BuiltInCatalog_DoesNotUseOrganizationNamespace()
    {
        var builtIns = Path.Combine(Path.GetDirectoryName(ExamplesRoot())!, "src");
        Assert.All(RunbookSource.FindDirectories(builtIns), d => Assert.False(RunbookNamespaces.IsOrganizationId(Path.GetFileName(d))));
    }
}
