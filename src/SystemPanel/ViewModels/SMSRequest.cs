using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels
{
    public class SMSRequest
    {
        [Required, Phone, Display(Name = "Telefon")]
        public string Phone { get; set; } = "";

        [Required, StringLength(480), Display(Name = "Mesaj")]
        public string Message { get; set; } = "";
    }
}
