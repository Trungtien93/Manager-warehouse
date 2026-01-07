using System.Threading.Tasks;

namespace MNBEMART.Services
{
    public interface IPermissionService
    {
        Task<bool> HasAsync(int userId, string module, string actionKey);
    }
}