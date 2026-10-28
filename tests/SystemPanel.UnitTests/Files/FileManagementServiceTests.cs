using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SystemPanel.Services;

namespace SystemPanel.UnitTests.Files;

public sealed class FileManagementServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sp-files-" + Guid.NewGuid().ToString("N"));
    private readonly FileManagementService _svc;

    public FileManagementServiceTests()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(Path.GetTempPath());
        _svc = new FileManagementService(NullLogger<FileManagementService>.Instance,
            Options.Create(new FileStorageOptions { Root = _root, MaxFileBytes = 1024 }), env);
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("..\\..\\web.config")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    public void Path_components_are_stripped_and_result_stays_in_root(string input)
    {
        Assert.True(_svc.TryResolve(input, out var name, out var full));
        Assert.DoesNotContain("..", name);
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(full));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("file.txt:secret")]
    [InlineData("CON.txt")]
    [InlineData("LPT1")]
    [InlineData("trailingdot.")]
    public void Unsafe_names_are_rejected(string input)
    {
        Assert.False(_svc.TryResolve(input, out _, out _));
    }

    [Fact]
    public async Task Upload_rejects_disallowed_extension_and_oversize()
    {
        Assert.False((await _svc.UploadFileAsync(FormFile("evil.aspx", 10))).Success);
        Assert.False((await _svc.UploadFileAsync(FormFile("big.txt", 4096))).Success);
    }

    [Fact]
    public async Task Upload_never_overwrites_an_existing_file()
    {
        var first = await _svc.UploadFileAsync(FormFile("report.txt", 10));
        var second = await _svc.UploadFileAsync(FormFile("report.txt", 10));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    private static IFormFile FormFile(string name, int size)
    {
        var bytes = Encoding.ASCII.GetBytes(new string('x', size));
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
    }
}
