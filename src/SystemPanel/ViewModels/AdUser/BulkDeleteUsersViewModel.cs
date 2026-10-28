using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.AdUser
{
    public class BulkDeleteUsersViewModel
    {
        [Required]
        [Display(Name = "CSV Dosyası")]
        public IFormFile CsvFile { get; set; }
    }
}
