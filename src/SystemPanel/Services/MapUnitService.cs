using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public class MapUnitService : IMapUnitService
    {
        private readonly List<MapUnit> _provinces = new()
        {
            new MapUnit { ProvinceName = "Istanbul", ProvinceId = "34" },
            new MapUnit { ProvinceName = "Ankara",  ProvinceId = "06" },
            new MapUnit { ProvinceName = "Izmir",   ProvinceId = "35" }
        };

        public List<MapUnit> GetAllProvinces() => _provinces;

       
        // ✅ YENİ: ProvinceContactInfo
        public Task<ProvinceContactInfo> GetProvinceDetailsAsync(string provinceName)
            => Task.FromResult(new ProvinceContactInfo
            {
                ProvinceName = provinceName,
                Email = "province@example.com",
                PhoneNumber = "555-1234"
            });
    }
}
