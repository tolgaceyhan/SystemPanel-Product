using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Models;

namespace SystemPanel.Services
{
    public interface IAdComputerService
    {
        // AD Computer (detay, LAPS, DN, vb.)
        Task<AdComputer?> GetComputerByNameAsync(string name, CancellationToken ct = default);

        Task<List<AdComputer>> GetAllComputersAsync(string? searchTerm = null, CancellationToken ct = default);

        Task<List<AdComputer>> GetComputersByOuAsync(string ou, string? searchTerm = null, CancellationToken ct = default);

        Task<List<string>> GetAllServerNamesAsync(CancellationToken ct = default);

        Task<string> GetComputerDistinguishedNameAsync(string computerName, CancellationToken ct = default);

        /// <summary>LAPS parolasını okur. Yalnızca AdComputerOperations üzerinden (izin + kapsam + audit) çağrılmalıdır.</summary>
        Task<SystemPanel.Services.ActiveDirectory.LapsResult> ReadLapsAsync(string computerDn, CancellationToken ct = default);

        // 👇 Server discovery (monitoring için)
        Task<List<AdServer>> DiscoverServersAsync(string? ou, string? ipPrefix);

        Task<AdServer?> GetServerByNameAsync(string name);
    }

}