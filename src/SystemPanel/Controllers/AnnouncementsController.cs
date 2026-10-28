using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SystemPanel.Models;
using SystemPanel.Services;
using SystemPanel.ViewModels.Announcements;
using X.PagedList.Mvc.Core;
using X.PagedList;
using X.PagedList.Extensions;
using SystemPanel.Security;
using SystemPanel.Enums;



namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.AnnouncementsView)]
    public class AnnouncementsController : BaseController

    {
        private readonly IAnnouncementsService _announcementsService;

        public AnnouncementsController(
            ILogger<AnnouncementsController> logger,
            IAnnouncementsService announcementsService)
            : base(logger)
        {
            _announcementsService = announcementsService;
        }

        private void LoadTypes()
        {
            ViewBag.Types = Enum.GetNames(typeof(AnnouncementType));
        }

        // LIST + FİLTRE + PAGING
        public async Task<IActionResult> List(string searchText, string typeFilter, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 10)
        {
            var q = _announcementsService.Query();

            if (!string.IsNullOrWhiteSpace(searchText))
                q = q.Where(d =>
                    d.Turu.Contains(searchText) ||
                    d.Birimi.Contains(searchText) ||
                    d.Aciklama.Contains(searchText) ||
                    d.Detay.Contains(searchText)
                );

            if (!string.IsNullOrWhiteSpace(typeFilter))
                q = q.Where(d => d.Turu == typeFilter);

            q = q.OrderByDescending(d => d.KayitTarihi);

            var paged = q.ToPagedList(page, pageSize);

            return View(paged);
        }
        // CREATE
        [HttpGet]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public IActionResult Create()
        {
            LoadTypes();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> Create(CreateAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                LoadTypes();
                return View(model);
            }

            try
            {
                await _announcementsService.AddAsync(model);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.File), ex.Message);
                LoadTypes();
                return View(model);
            }

            TempData["Message"] = "Duyuru başarıyla eklendi.";
            return RedirectToAction(nameof(List));
        }

        // EDIT
        [HttpGet]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _announcementsService.GetByIdAsync(id);
            if (entity == null) return NotFound();

            var model = new EditAnnouncementViewModel
            {
                Id = entity.Id,
                Turu = entity.Turu,
                Birimi = entity.Birimi,
                Aciklama = entity.Aciklama,
                Detay = entity.Detay
            };

            LoadTypes();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> Edit(EditAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                LoadTypes();
                return View(model);
            }

            try
            {
                await _announcementsService.UpdateAsync(model);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.File), ex.Message);
                LoadTypes();
                return View(model);
            }

            TempData["Message"] = "Duyuru güncellendi.";
            return RedirectToAction(nameof(List));
        }

        // DETAILS (Admin + LocalAdmin görebilir)
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var entity = await _announcementsService.GetByIdAsync(id);
            if (entity == null)
            {
                TempData["Error"] = "Duyuru bulunamadı.";
                return RedirectToAction(nameof(List));
            }
            return View(entity);
        }

        // DELETE
        [HttpGet]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _announcementsService.GetByIdAsync(id);
            if (entity == null)
            {
                TempData["Error"] = "Silinecek duyuru bulunamadı.";
                return RedirectToAction(nameof(List));
            }
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            await _announcementsService.DeleteAsync(id);
            TempData["Message"] = "Duyuru silindi.";
            return RedirectToAction(nameof(List));
        }

        // ÇOKLU SİLME
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AnnouncementsManage)]
        public async Task<IActionResult> DeleteSelected(int[] ids)
        {
            if (ids != null && ids.Length > 0)
            {
                await _announcementsService.DeleteRangeAsync(ids);
                TempData["Message"] = $"{ids.Length} duyuru silindi.";
            }

            return RedirectToAction(nameof(List));
        }

        // API – JSON Export
        [HttpGet("/api/announcements")]
        public async Task<IActionResult> ExportJson()
        {
            var data = await _announcementsService.GetAllAsync();
            return Ok(data);
        }

        // API – CSV Export
        [HttpGet("/api/announcements/csv")]
        public async Task<IActionResult> ExportCsv()
        {
            var data = await _announcementsService.GetAllAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Id,Turu,Birimi,Aciklama,Detay,KayitTarihi");

            foreach (var d in data)
            {
                sb.AppendLine($"{d.Id},{d.Turu},{d.Birimi},{d.Aciklama},{d.Detay},{d.KayitTarihi:yyyy-MM-dd HH:mm:ss}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "duyurular.csv");
        }
    }
}
