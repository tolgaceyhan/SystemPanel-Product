using System;

namespace SystemPanel.Models
{
    public class AdUser
    {
        public string Id { get; set; }

        public string DistinguishedName { get; set; }
        public string SamAccountName { get; set; }
        public string UserPrincipalName { get; set; }

        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public string GivenName { get; set; }
        public string Surname { get; set; }

        public string Mail { get; set; }
        public string MailNickname { get; set; }
        public string Email { get; set; }

        public string Mobile { get; set; }
        public string TelephoneNumber { get; set; }

        public string Department { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// Kullanıcı hesabı aktif mi? (ACCOUNTDISABLE biti kapalıysa true)
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Hesap kilitli mi (lockoutTime != 0) – opsiyonel, bazı yerlerde sadece okunuyor.
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Kullanıcının OU path'i (OU=...,OU=...,DC=...,DC=...)
        /// </summary>
        public string OuPath { get; set; }

        /// <summary>
        /// AD adminCount=1: hesap şu anda veya geçmişte ayrıcalıklı bir gruba (Domain Admins vb.)
        /// üye olmuş ve AdminSDHolder ile korunuyor. Global kapsamı olmayan kullanıcılar bu hesapları değiştiremez.
        /// </summary>
        /// (AD'den okunan çalışma zamanı bilgisidir; AdUsers tablosuna YAZILMAZ.)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsPrivileged { get; set; }

        public DateTime? WhenCreated { get; set; }
        public DateTime? WhenChanged { get; set; }
    }
}
