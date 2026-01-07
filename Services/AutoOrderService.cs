using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Services
{
    public class AutoOrderService : IAutoOrderService
    {
        private readonly AppDbContext _context;
        private readonly IDocumentNumberingService _documentNumbering;
        private readonly ILogger<AutoOrderService> _logger;
        private readonly IEmailService? _emailService;
        private readonly IServiceProvider _serviceProvider;

        public AutoOrderService(
            AppDbContext context,
            IDocumentNumberingService documentNumbering,
            ILogger<AutoOrderService> logger,
            IServiceProvider serviceProvider)
        {
            _context = context;
            _documentNumbering = documentNumbering;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<List<int>> CheckLowStockAndCreateRequests()
        {
            var createdRequestIds = new List<int>();

            try
            {
                // Get all materials with MinimumStock set and calculate current stock
                var stockGroups = await _context.Stocks
                    .Include(s => s.Material)
                    .ThenInclude(m => m.Supplier)
                    .Where(s => s.Material.MinimumStock != null && 
                               s.Material.MinimumStock > 0)
                    .GroupBy(s => s.MaterialId)
                    .Select(g => new
                    {
                        MaterialId = g.Key,
                        Material = g.First().Material,
                        CurrentStock = g.Sum(x => x.Quantity)
                    })
                    .ToListAsync();

                _logger.LogInformation($"Checking {stockGroups.Count} materials with MinimumStock set");
                
                // Log details for debugging
                foreach (var item in stockGroups)
                {
                    _logger.LogInformation($"Material {item.Material.Code} ({item.Material.Name}): CurrentStock={item.CurrentStock}, MinimumStock={item.Material.MinimumStock}");
                }

                // Filter in memory to ensure correct comparison with nullable decimal
                var lowStockMaterials = stockGroups
                    .Where(x => x.Material.MinimumStock.HasValue && 
                                x.CurrentStock < x.Material.MinimumStock.Value)
                    .ToList();

                if (!lowStockMaterials.Any())
                {
                    _logger.LogInformation("No low stock materials found. All materials are above minimum stock level.");
                    return createdRequestIds;
                }

                _logger.LogInformation($"Found {lowStockMaterials.Count} materials with low stock (CurrentStock < MinimumStock)");

                // Create low stock alert notifications
                if (_serviceProvider != null)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                        
                        if (notificationService != null)
                        {
                            // Get admin users to notify
                            var adminUserIds = await _context.Users
                                .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                                .Select(u => u.Id)
                                .ToListAsync();

                            if (adminUserIds.Any())
                            {
                                var title = $"Cảnh báo tồn kho thấp: {lowStockMaterials.Count} vật tư";
                                var message = $"Hệ thống phát hiện {lowStockMaterials.Count} vật tư có tồn kho dưới mức tối thiểu";

                                await notificationService.CreateNotificationForUsersAsync(
                                    NotificationType.LowStockAlert,
                                    0,
                                    title,
                                    message,
                                    adminUserIds,
                                    NotificationPriority.High
                                );
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating low stock alert notifications");
                    }
                }

                // Get system user (ID 1) as auto-requestor
                var systemUserId = 1;

                // Group by preferred supplier (or null if no preference)
                var groupedBySupplier = lowStockMaterials
                    .GroupBy(x => x.Material.PreferredSupplierId ?? 0)
                    .ToList();

                foreach (var supplierGroup in groupedBySupplier)
                {
                    // Generate request number
                    var requestNumber = await _documentNumbering.NextAsync("PR");

                    // Create purchase request
                    var request = new PurchaseRequest
                    {
                        RequestNumber = requestNumber,
                        RequestDate = DateTime.Now,
                        RequestedById = systemUserId,
                        Status = PRStatus.Pending,
                        IsAutoGenerated = true,
                        Notes = "Tự động tạo từ hệ thống - Vật tư dưới mức tồn tối thiểu",
                        CreatedAt = DateTime.Now
                    };

                    // Add details
                    foreach (var item in supplierGroup)
                    {
                        var reorderQty = item.Material.ReorderQuantity ?? 
                                        (item.Material.MinimumStock.Value - item.CurrentStock);

                        var detail = new PurchaseRequestDetail
                        {
                            MaterialId = item.MaterialId,
                            RequestedQuantity = reorderQty,
                            CurrentStock = item.CurrentStock,
                            MinimumStock = item.Material.MinimumStock.Value,
                            PreferredSupplierId = item.Material.PreferredSupplierId,
                            EstimatedPrice = item.Material.PurchasePrice,
                            Notes = $"Tồn hiện tại: {item.CurrentStock}, Tối thiểu: {item.Material.MinimumStock}"
                        };

                        request.Details.Add(detail);
                    }

                    await _context.PurchaseRequests.AddAsync(request);
                    await _context.SaveChangesAsync();

                    createdRequestIds.Add(request.Id);
                    _logger.LogInformation($"Created auto purchase request {requestNumber} with {request.Details.Count} items");

                    // Send email notification and create in-app notifications for auto-generated PR
                    if (_serviceProvider != null)
                    {
                        try
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var emailService = scope.ServiceProvider.GetService<IEmailService>();
                            var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                            
                            // Reload request with includes
                            var requestForNotifications = await _context.PurchaseRequests
                                .Include(r => r.RequestedBy)
                                .Include(r => r.Details)
                                    .ThenInclude(d => d.Material)
                                .FirstOrDefaultAsync(r => r.Id == request.Id);

                            if (requestForNotifications != null)
                            {
                                // Send email notification
                                if (emailService != null)
                                {
                                    var materialNames = requestForNotifications.Details
                                        .Where(d => d.Material != null)
                                        .Select(d => d.Material!.Name)
                                        .ToList();
                                    var totalQty = requestForNotifications.Details.Sum(d => d.RequestedQuantity);
                                    var requestedByName = requestForNotifications.RequestedBy?.FullName ?? "System";

                                    await emailService.SendPurchaseRequestNotificationAsync(
                                        requestForNotifications.Id,
                                        requestForNotifications.RequestNumber,
                                        requestedByName,
                                        materialNames,
                                        totalQty
                                    );
                                }

                                // Create in-app notifications for admins and approvers
                                if (notificationService != null)
                                {
                                    var notifyUserIds = new List<int>();
                                    
                                    // Get Admin users (always try this as it's simpler)
                                    try
                                    {
                                        var adminUserIds = await _context.Users
                                            .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                                            .Select(u => u.Id)
                                            .ToListAsync();
                                        notifyUserIds.AddRange(adminUserIds);
                                    }
                                    catch (Exception adminEx)
                                    {
                                        _logger.LogWarning(adminEx, "Error getting admin users for notification");
                                    }

                                    // Get users with PurchaseRequests.Approve permission (may fail if UserRoles table doesn't exist)
                                    try
                                    {
                                        var approverUserIds = await (from u in _context.Users
                                                                    where u.IsActive
                                                                    join ur in _context.UserRoles on u.Id equals ur.UserId
                                                                    join rp in _context.RolePermissions on ur.RoleId equals rp.RoleId
                                                                    join p in _context.Permissions on rp.PermissionId equals p.Id
                                                                    where p.Module == "PurchaseRequests" && rp.CanApprove
                                                                    select u.Id)
                                                                    .Distinct()
                                                                    .ToListAsync();
                                        notifyUserIds.AddRange(approverUserIds);
                                    }
                                    catch (Exception approverEx)
                                    {
                                        _logger.LogWarning(approverEx, "Error getting approver users for notification - UserRoles table may not exist");
                                        // Continue with just admin notifications
                                    }

                                    // Remove duplicates
                                    notifyUserIds = notifyUserIds.Distinct().ToList();

                                    if (notifyUserIds.Any())
                                    {
                                        var requestedByName = requestForNotifications.RequestedBy?.FullName ?? "System";
                                        var title = $"Đề xuất đặt hàng tự động: {requestNumber}";
                                        var message = $"Hệ thống tự động tạo - {requestForNotifications.Details.Count} vật tư";

                                        await notificationService.CreateNotificationForUsersAsync(
                                            NotificationType.PurchaseRequest,
                                            request.Id,
                                            title,
                                            message,
                                            notifyUserIds,
                                            NotificationPriority.Normal
                                        );
                                        
                                        _logger.LogInformation($"Created notifications for auto-generated PR {requestNumber} for {notifyUserIds.Count} users");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending notifications for auto-generated PR {RequestNumber}", requestNumber);
                        }
                    }
                }

                return createdRequestIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckLowStockAndCreateRequests");
                return createdRequestIds;
            }
        }

        public async Task<int> GetLowStockCount()
        {
            // Get all materials with MinimumStock set and calculate current stock
            var stockGroups = await _context.Stocks
                .Include(s => s.Material)
                .Where(s => s.Material.MinimumStock != null && 
                           s.Material.MinimumStock > 0)
                .GroupBy(s => s.MaterialId)
                .Select(g => new
                {
                    MaterialId = g.Key,
                    Material = g.First().Material,
                    CurrentStock = g.Sum(x => x.Quantity)
                })
                .ToListAsync();

            // Filter in memory to ensure correct comparison with nullable decimal
            var count = stockGroups
                .Where(x => x.Material.MinimumStock.HasValue && 
                            x.CurrentStock < x.Material.MinimumStock.Value)
                .Count();

            return count;
        }
    }
}

