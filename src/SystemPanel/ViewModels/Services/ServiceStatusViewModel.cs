using System.ComponentModel.DataAnnotations;
using SystemPanel.Models;

namespace SystemPanel.ViewModels
{
    public class ServiceStatusViewModel
    {
        [Required(ErrorMessage = "Servisler listesi zorunludur.")]
        public List<ServiceStatus> Services { get; set; }  // Servislerin listesini tutar
    }
}
