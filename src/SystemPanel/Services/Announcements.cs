using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.ViewModels.Announcements;

namespace SystemPanel.Services
{
    public class AnnouncementsService : IAnnouncementsService
    {
        private readonly ApplicationDbContext _context;

        public AnnouncementsService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Ekler wwwroot altında statik olarak servis edilir: HTML/SVG/JS gibi tarayıcıda çalışabilen
        // türler stored-XSS'e yol açar. Yalnızca belge ve görsel türlerine izin verilir.
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip"
        };

        public const long MaxAttachmentBytes = 20 * 1024 * 1024;

        private static async Task<string> SaveAttachmentAsync(Microsoft.AspNetCore.Http.IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                throw new InvalidOperationException("Bu dosya türüne izin verilmiyor.");
            if (file.Length <= 0 || file.Length > MaxAttachmentBytes)
                throw new InvalidOperationException("Dosya boş veya izin verilen boyuttan büyük (20 MB).");

            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "announcements");
            Directory.CreateDirectory(uploadsRoot);

            var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            await using (var stream = new FileStream(Path.Combine(uploadsRoot, fileName), FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }

            return "/uploads/announcements/" + fileName;
        }

        public IQueryable<Announcements> Query()
        {
            return _context.Announcements.AsQueryable();
        }

        public async Task<int> CountAsync()
        {
            return await _context.Announcements.CountAsync();
        }

        public async Task<int> TodayCountAsync()
        {
            var today = DateTime.Now.Date;
            return await _context.Announcements
                .Where(d => d.KayitTarihi.Date == today)
                .CountAsync();
        }

        public async Task<Announcements?> GetLastAsync()
        {
            return await _context.Announcements
                .OrderByDescending(d => d.KayitTarihi)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Announcements>> GetAllAsync()
        {
            return await _context.Announcements
                .OrderByDescending(d => d.KayitTarihi)
                .ToListAsync();
        }

        public async Task<Announcements?> GetByIdAsync(int id)
        {
            return await _context.Announcements.FindAsync(id);
        }

        public async Task AddAsync(CreateAnnouncementViewModel model)
        {
            string? filePath = null;

            if (model.File != null)
            {
                filePath = await SaveAttachmentAsync(model.File);
            }

            var entity = new Announcements
            {
                Turu = model.Turu,
                Birimi = model.Birimi,
                Aciklama = model.Aciklama,
                Detay = model.Detay,
                FilePath = filePath,
                KayitTarihi = DateTime.Now
            };

            _context.Announcements.Add(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(EditAnnouncementViewModel model)
        {
            var existing = await _context.Announcements.FindAsync(model.Id);
            if (existing == null) return;

            existing.Turu = model.Turu;
            existing.Birimi = model.Birimi;
            existing.Aciklama = model.Aciklama;
            existing.Detay = model.Detay;

            if (model.File != null)
            {
                existing.FilePath = await SaveAttachmentAsync(model.File);
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _context.Announcements.FindAsync(id);
            if (existing != null)
            {
                _context.Announcements.Remove(existing);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteRangeAsync(int[] ids)
        {
            var items = await _context.Announcements
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            _context.Announcements.RemoveRange(items);
            await _context.SaveChangesAsync();
        }
    }
}
