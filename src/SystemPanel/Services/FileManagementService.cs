using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.ViewModels.Files;

namespace SystemPanel.Services
{
    public sealed class FileStorageOptions
    {
        public const string SectionName = "FileStorage";

        /// <summary>Dosyaların tutulduğu klasör. Boşsa (geriye uyumluluk) ContentRoot/UploadedFiles.</summary>
        public string? Root { get; set; }

        public long MaxFileBytes { get; set; } = 100 * 1024 * 1024;

        /// <summary>İzin verilen uzantılar (küçük harf, noktalı). Boş liste = hepsi reddedilir.</summary>
        public List<string> AllowedExtensions { get; set; } = new()
        {
            ".txt", ".log", ".csv", ".pdf", ".zip", ".7z", ".cab", ".msi", ".msu", ".ps1", ".xml", ".json",
            ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg",
        };
    }

    /// <summary>
    /// Yönetim dosyaları. Kullanıcıdan gelen ad yalnızca dosya adı olarak kullanılır
    /// (klasör/yol bileşenleri atılır), hedef her zaman kök klasörün içinde olmalıdır ve
    /// mevcut dosyanın üzerine sessizce yazılmaz.
    /// </summary>
    public class FileManagementService : IFileManagementService
    {
        private readonly string _fileDirectory;
        private readonly FileStorageOptions _options;
        private readonly ILogger<FileManagementService> _logger;
        private readonly FileExtensionContentTypeProvider _contentTypes = new();

        public FileManagementService(
            ILogger<FileManagementService> logger,
            IOptions<FileStorageOptions> options,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _options = options.Value;
            _fileDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.Root)
                ? Path.Combine(env.ContentRootPath, "UploadedFiles")
                : _options.Root);

            Directory.CreateDirectory(_fileDirectory);
        }

        public List<FileItemViewModel> GetAllFiles()
        {
            return Directory.GetFiles(_fileDirectory)
                .Select(p => new FileInfo(p))
                .OrderBy(i => i.Name)
                .Select(i => new FileItemViewModel { Name = i.Name, Size = i.Length })
                .ToList();
        }

        public bool UploadFile(IFormFile file) => UploadFileAsync(file).GetAwaiter().GetResult().Success;

        async Task<bool> IFileManagementService.UploadFileAsync(IFormFile file) => (await UploadFileAsync(file)).Success;

        public async Task<FileOperationResult> UploadFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return FileOperationResult.Fail("Dosya boş.");

            if (file.Length > _options.MaxFileBytes)
                return FileOperationResult.Fail($"Dosya izin verilen boyuttan büyük ({_options.MaxFileBytes / (1024 * 1024)} MB).");

            if (!TryResolve(file.FileName, out var safeName, out var path))
                return FileOperationResult.Fail("Geçersiz dosya adı.");

            var ext = Path.GetExtension(safeName).ToLowerInvariant();
            if (!_options.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return FileOperationResult.Fail($"'{ext}' uzantısına izin verilmiyor.");

            // Aynı adlı dosya varsa üzerine yazmak yerine benzersiz ad üret.
            if (File.Exists(path))
            {
                var stem = Path.GetFileNameWithoutExtension(safeName);
                safeName = $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                path = Path.Combine(_fileDirectory, safeName);
            }

            await using (var fs = new FileStream(path, FileMode.CreateNew))
            {
                await file.CopyToAsync(fs);
            }

            return FileOperationResult.Ok(safeName);
        }

        public bool DeleteFile(string fileName)
        {
            if (!TryResolve(fileName, out _, out var path) || !File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }

        public (Stream Stream, string ContentType, string SafeName)? GetFileStream(string fileName)
        {
            if (!TryResolve(fileName, out var safeName, out var path) || !File.Exists(path))
                return null;

            if (!_contentTypes.TryGetContentType(safeName, out var contentType))
                contentType = "application/octet-stream";

            return (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), contentType, safeName);
        }

        /// <summary>
        /// Kullanıcı girdisini güvenli dosya adına çevirir; sonuç yolun kök klasör içinde
        /// olduğunu doğrular. Path traversal (../), mutlak yol, alternatif veri akışı (:)
        /// ve Windows'a özgü ayrılmış adlar reddedilir.
        /// </summary>
        internal bool TryResolve(string? input, out string safeName, out string fullPath)
        {
            safeName = string.Empty;
            fullPath = string.Empty;

            var name = Path.GetFileName((input ?? string.Empty).Replace('\\', '/')).Trim();
            if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Length > 200)
                return false;

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':') || name.EndsWith('.') )
                return false;

            var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                return false;

            var candidate = Path.GetFullPath(Path.Combine(_fileDirectory, name));
            if (!string.Equals(Path.GetDirectoryName(candidate), _fileDirectory, StringComparison.OrdinalIgnoreCase))
                return false;

            safeName = name;
            fullPath = candidate;
            return true;
        }
    }

    public sealed record FileOperationResult(bool Success, string? FileName, string? Error)
    {
        public static FileOperationResult Ok(string fileName) => new(true, fileName, null);
        public static FileOperationResult Fail(string error) => new(false, null, error);
    }
}
