using MNBEMART.Models;
using MNBEMART.Data;
namespace MNBEMART.Services
{
    public interface IAuditService
    {
        Task LogAsync(int userId, string action, string objectType, string? objectId = null, string? module = null, int? warehouseId = null, string? content = null);
    }

    public class AuditService : IAuditService
    {
        private readonly AppDbContext _db;
        public AuditService(AppDbContext db) { _db = db; }

        public async Task LogAsync(int userId, string action, string objectType, string? objectId = null, string? module = null, int? warehouseId = null, string? content = null)
        {
            var log = new AuditLog
            {
                UserId = userId,
                Action = action,
                ObjectType = objectType,
                ObjectId = objectId ?? string.Empty,
                Timestamp = DateTime.Now,
                Module = module,
                WarehouseId = warehouseId,
                Content = content
            };
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}