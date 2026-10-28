using System;
using System.ComponentModel.DataAnnotations;

namespace SystemPanel.ViewModels.AdUser
{
    public class EditAdUserViewModel
    {
        // ======================================================
        // *** IDENTIFICATION ***
        // ======================================================

        [Required]
        public string Id { get; set; }  // DistinguishedName

        public string DistinguishedName { get; set; }

        // ======================================================
        // *** ACCOUNT CORE ***
        // ======================================================

        [Display(Name = "Kullanıcı Adı (sAMAccountName)")]
        public string SamAccountName { get; set; }

        [Display(Name = "UPN")]
        public string UserPrincipalName { get; set; }

        [Display(Name = "Görünen Ad")]
        public string DisplayName { get; set; }

        [Display(Name = "Ad")]
        public string GivenName { get; set; }

        [Display(Name = "Soyad")]
        public string Surname { get; set; }

        // ======================================================
        // *** CONTACT INFO ***
        // ======================================================

        [EmailAddress]
        [Display(Name = "E-Posta")]
        public string Email { get; set; }

        [Display(Name = "Mail")]
        public string Mail { get; set; }

        [Display(Name = "Mail Nickname (Alias)")]
        public string MailNickname { get; set; }

        [Display(Name = "Telefon")]
        public string TelephoneNumber { get; set; }

        [Display(Name = "Mobil")]
        public string Mobile { get; set; }

        // ======================================================
        // *** ORGANIZATIONAL ***
        // ======================================================

        [Display(Name = "Departman")]
        public string Department { get; set; }

        [Display(Name = "Açıklama")]
        public string Description { get; set; }

        [Display(Name = "OU Yolu")]
        public string OuPath { get; set; }

        // ======================================================
        // *** ACCOUNT STATUS ***
        // ======================================================

        [Display(Name = "Aktif")]
        public bool Enabled { get; set; }

        [Display(Name = "Kilitli mi?")]
        public bool IsLocked { get; set; }

        // ======================================================
        // *** METADATA ***
        // ======================================================

        [Display(Name = "Oluşturulma Tarihi")]
        public DateTime? WhenCreated { get; set; }

        [Display(Name = "Son Değişiklik")]
        public DateTime? WhenChanged { get; set; }
    }
}
