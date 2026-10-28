using System.Collections.Generic;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface IMapUnitService
    {
        List<MapUnit> GetAllProvinces();

        Task<ProvinceContactInfo> GetProvinceDetailsAsync(string provinceName);
    }
}
