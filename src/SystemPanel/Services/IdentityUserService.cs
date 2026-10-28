using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class IdentityUserService
    {
        private readonly UserManager<IdentityUser> _userManager;

        public IdentityUserService(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        // Tüm Identity kullanıcılarını listeleme
        public List<IdentityUser> GetAllIdentityUsers()
        {
            return _userManager.Users.ToList();
        }

        // Yeni kullanıcı oluşturma
        public async Task<bool> CreateUserAsync(string username, string email, string password)
        {
            var user = new IdentityUser
            {
                UserName = username,
                Email = email
            };

            var result = await _userManager.CreateAsync(user, password);
            return result.Succeeded;
        }

        // Kullanıcı silme
        public async Task<bool> DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var result = await _userManager.DeleteAsync(user);
            return result.Succeeded;
        }

        // Kullanıcı detaylarını getirme
        public async Task<IdentityUser> GetUserByIdAsync(string userId)
        {
            return await _userManager.FindByIdAsync(userId);
        }

        // Kullanıcı düzenleme
        public async Task<bool> UpdateUserAsync(string userId, string email, string username)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            user.Email = email;
            user.UserName = username;

            var result = await _userManager.UpdateAsync(user);
            return result.Succeeded;
        }
    }
}
