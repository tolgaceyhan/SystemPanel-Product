using System.Threading.Tasks;

namespace SystemPanel.Services
{
    public interface ISmsService
    {
        Task SendAsync(string phone, string message);
    }
}
