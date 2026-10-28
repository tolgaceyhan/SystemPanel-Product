using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.AdUser
{
    public class BulkCreateUsersViewModel
    {
        [Required]
        [Display(Name = "CSV Dosyası")]
        public IFormFile CsvFile { get; set; }
    }
}
