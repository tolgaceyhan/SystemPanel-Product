using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.ViewModels.Files;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.FilesView)]
    public class FileManagementController : Controller
    {
        private readonly FileManagementService _fileService;
        private readonly IAuditService _audit;
        private readonly ILogger<FileManagementController> _logger;

        public FileManagementController(
            FileManagementService fileService,
            IAuditService audit,
            ILogger<FileManagementController> logger)
        {
            _fileService = fileService;
            _audit = audit;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                return View(_fileService.GetAllFiles());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching files.");
                TempData["ErrorMessage"] = "Dosyalar alınırken bir hata oluştu.";
                return View(new List<FileItemViewModel>());
            }
        }

        [HttpGet]
        public IActionResult Download(string fileName)
        {
            var dl = _fileService.GetFileStream(fileName);
            if (dl == null) return NotFound();

            return File(dl.Value.Stream, dl.Value.ContentType ?? "application/octet-stream", dl.Value.SafeName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.FilesUpload)]
        [RequestSizeLimit(110 * 1024 * 1024)]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Yüklenecek dosya seçilmedi.";
                return RedirectToAction(nameof(Index));
            }

            FileOperationResult result;
            try
            {
                result = await _fileService.UploadFileAsync(file);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Error uploading file.");
                result = FileOperationResult.Fail("Dosya kaydedilemedi.");
            }

            await _audit.RecordAsync(new AuditRecord(AuditActions.FileUpload)
            {
                ResourceType = "File",
                ResourceName = result.FileName ?? Path.GetFileName(file.FileName),
                Success = result.Success,
                ErrorMessage = result.Error,
                Metadata = new { OriginalName = Path.GetFileName(file.FileName), file.Length },
            });

            TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
                result.Success ? $"Dosya yüklendi: {result.FileName}" : result.Error;

            return RedirectToAction(nameof(Index)); // PRG
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.FilesDelete)]
        public async Task<IActionResult> DeleteFile(string fileName)
        {
            bool ok;
            try
            {
                ok = _fileService.DeleteFile(fileName);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Error deleting file.");
                ok = false;
            }

            await _audit.RecordAsync(new AuditRecord(AuditActions.FileDelete)
            {
                ResourceType = "File",
                ResourceName = Path.GetFileName(fileName ?? string.Empty),
                Success = ok,
            });

            TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok ? "Dosya silindi." : "Dosya silinemedi.";
            return RedirectToAction(nameof(Index));
        }
    }
}
