using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SystemPanel.ViewModels.UserManagement;

namespace SystemPanel.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserManagementService(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<UserListViewModel> GetPagedUsersAsync(
            int page,
            string search,
            string sort,
            string dir,
            int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (string.IsNullOrWhiteSpace(sort)) sort = "UserName";
            if (string.IsNullOrWhiteSpace(dir)) dir = "asc";

            var query = _userManager.Users.AsQueryable();

            // --- Search ---
            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                query = query.Where(u =>
                    u.UserName.Contains(s) ||
                    u.Email.Contains(s) ||
                    u.PhoneNumber.Contains(s)
                );
            }

            // --- Sorting ---
            bool asc = dir.Equals("asc", StringComparison.OrdinalIgnoreCase);

            query = sort switch
            {
                "Email" => asc ? query.OrderBy(u => u.Email) : query.OrderByDescending(u => u.Email),
                "PhoneNumber" => asc ? query.OrderBy(u => u.PhoneNumber) : query.OrderByDescending(u => u.PhoneNumber),
                "Status" => asc ? query.OrderBy(u => u.LockoutEnd.HasValue) : query.OrderByDescending(u => u.LockoutEnd.HasValue),
                _ => asc ? query.OrderBy(u => u.UserName) : query.OrderByDescending(u => u.UserName)
            };

            // --- Paging ---
            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // --- Convert to VM ---
            var items = new System.Collections.Generic.List<UserListItemViewModel>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);

                items.Add(new UserListItemViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    Roles = roles.ToList(),
                    LockoutEnd = u.LockoutEnd
                });
            }

            // --- RETURN burada (foreach DIŞINDA) ---
            return new UserListViewModel
            {
                Users = items,
                Search = search ?? "",
                Sort = sort,
                Dir = dir,
                CurrentPage = page,
                PageSize = pageSize,
                TotalItems = totalCount
            };
        }
    }
}
