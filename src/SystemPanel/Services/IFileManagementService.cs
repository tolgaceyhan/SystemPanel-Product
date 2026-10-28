using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SystemPanel.ViewModels.Files;

namespace SystemPanel.Services
{
    public interface IFileManagementService
    {
        List<FileItemViewModel> GetAllFiles();
        bool UploadFile(IFormFile file);
        Task<bool> UploadFileAsync(IFormFile file);
        bool DeleteFile(string fileName);
        (Stream Stream, string ContentType, string SafeName)? GetFileStream(string fileName);
    }
}
