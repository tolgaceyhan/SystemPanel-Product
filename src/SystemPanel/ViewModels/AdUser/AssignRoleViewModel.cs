using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SystemPanel.ViewModels.AdUser
{
    public class AssignRoleViewModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }

        [Required(ErrorMessage = "Lütfen bir rol seçiniz.")]
        public string SelectedRole { get; set; }

        public List<SelectListItem> AvailableRoles { get; set; } = new();
    }
}

