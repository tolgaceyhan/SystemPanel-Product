
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SystemPanel.Data
{
    public static class SeedData
    {
        // İlk yönetici hesabı yalnızca açıkça yapılandırılmışsa oluşturulur.
        // Parola kaynak kodda veya appsettings.json'da TUTULMAZ; ortam değişkeni
        // (Bootstrap__AdminPassword) veya korumalı yapılandırma ile verilir.
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("SystemPanel.Bootstrap");

            string[] roles = { "Admin", "LocalAdmin", "Operator", "Helpdesk", "Monitoring" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            var adminUserName = config["Bootstrap:AdminUserName"];
            var adminPassword = config["Bootstrap:AdminPassword"];

            if (string.IsNullOrWhiteSpace(adminUserName) || string.IsNullOrWhiteSpace(adminPassword))
                return;

            var adminUser = await userManager.FindByNameAsync(adminUserName);
            if (adminUser == null)
            {
                adminUser = new IdentityUser
                {
                    UserName = adminUserName,
                    Email = config["Bootstrap:AdminEmail"],
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                    logger?.LogWarning("İlk yönetici hesabı oluşturuldu ({User}). Bootstrap:AdminPassword değerini yapılandırmadan kaldırın.", adminUserName);
                }
                else
                {
                    // Parola politikası (12+ karakter, büyük/küçük harf, rakam) karşılanmamış olabilir. Parola loglanmaz.
                    logger?.LogError("İlk yönetici hesabı oluşturulamadı: {Errors}", string.Join("; ", System.Linq.Enumerable.Select(result.Errors, e => e.Code)));
                }
            }
        }
    }
}
