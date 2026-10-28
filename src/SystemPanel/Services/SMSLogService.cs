// Dosya: Services/SmsLogService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface ISmsLogService
    {
        Task LogAsync(string icerik, string? gidenGrup, IEnumerable<AdUserDto> users);
    }

    public class SmsLogService : ISmsLogService
    {
        private readonly ApplicationDbContext _db;

        public SmsLogService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task LogAsync(string icerik, string? gidenGrup, IEnumerable<AdUserDto> users)
        {
            var sms = new Sms
            {
                Icerik = icerik,
                GidenGrup = gidenGrup,
                Tarih = DateTime.Now
            };

            _db.Smsler.Add(sms);
            await _db.SaveChangesAsync();

            foreach (var u in users)
            {
                _db.SmsGidenler.Add(new SmsGidenler
                {
                    SmsId = sms.Id,
                    Kime = u.DisplayName,
                    Telefon = u.Phone,
                    Sicil = int.TryParse(u.Sicil, out var sicil) ? sicil : null,
                    Tarih = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
