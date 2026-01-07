
using Microsoft.EntityFrameworkCore;
using MNBEMART.Models;

namespace MNBEMART.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<StockLot> StockLots => Set<StockLot>();
        public DbSet<StockIssueAllocation> StockIssueAllocations => Set<StockIssueAllocation>();
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<UserWarehouse> UserWarehouses { get; set; }
        public DbSet<Material> Materials { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<MaterialSpecification> MaterialSpecifications { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<StockReceipt> StockReceipts { get; set; }
        public DbSet<StockIssue> StockIssues { get; set; }
        public DbSet<StockReceiptDetail> StockReceiptDetails { get; set; }
        public DbSet<StockIssueDetail> StockIssueDetails { get; set; }
        public DbSet<StockTransfer> StockTransfers { get; set; }
        public DbSet<StockTransferDetail> StockTransferDetails { get; set; }
        public DbSet<Stock> Stocks { get; set; }
        public DbSet<StockBalance> StockBalances { get; set; }
        //public decimal Quantity { get; set; }

        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();

        

        // DbSets mới
        public DbSet<DocumentNumbering> DocumentNumberings => Set<DocumentNumbering>();
        // StockAdjustment đã bị vô hiệu hóa - chức năng không còn sử dụng
        // public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
        // public DbSet<StockAdjustmentDetail> StockAdjustmentDetails => Set<StockAdjustmentDetail>();
        // StockCount, StockCountLine, Attachment, PeriodLock đã bị xóa - các bảng không sử dụng
        // public DbSet<StockCount> StockCounts => Set<StockCount>();
        // public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
        // public DbSet<Attachment> Attachments => Set<Attachment>();
        // public DbSet<PeriodLock> PeriodLocks => Set<PeriodLock>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
        public DbSet<PurchaseRequestDetail> PurchaseRequestDetails => Set<PurchaseRequestDetail>();
        public DbSet<LotHistory> LotHistories => Set<LotHistory>();
        public DbSet<WarehouseDistance> WarehouseDistances => Set<WarehouseDistance>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<DemandForecast> DemandForecasts => Set<DemandForecast>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // ===== Role & UserRole n-n =====
            // modelBuilder.Entity<Role>()
            // .HasIndex(x => x.Code)
            // .IsUnique();
            // modelBuilder.Entity<UserRole>()
            // .HasKey(ur => new { ur.UserId, ur.RoleId });

            // modelBuilder.Entity<UserRole>()
            //     .HasOne(ur => ur.User)
            //     .WithMany(u => u.UserRoles)
            //     .HasForeignKey(ur => ur.UserId);
            // modelBuilder.Entity<UserRole>()
            //     .HasOne(ur => ur.Role)
            //     .WithMany(r => r.UserRoles)
            //     .HasForeignKey(ur => ur.RoleId);
            modelBuilder.Entity<MNBEMART.Models.Role>()
                .HasIndex(x => x.Code).IsUnique();

            // AuditLog quan hệ (tuỳ chọn Warehouse)
            modelBuilder.Entity<AuditLog>(e =>
            {
                e.HasOne(a => a.User)
                 .WithMany(u => u.AuditLogs)
                 .HasForeignKey(a => a.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(a => a.Warehouse)
                 .WithMany()
                 .HasForeignKey(a => a.WarehouseId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<MNBEMART.Models.UserRole>(b =>
            {
                b.HasKey(x => new { x.UserId, x.RoleId });
                b.HasOne(x => x.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(x => x.UserId);
                b.HasOne(x => x.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(x => x.RoleId);
            });
            // ===== RolePermission n-n =====
            modelBuilder.Entity<RolePermission>()
                .HasIndex(x => new { x.RoleId, x.PermissionId })
                .IsUnique();


            // ===== Warehouse quan hệ =====
            modelBuilder.Entity<StockReceipt>()
                .HasOne(sr => sr.Warehouse)
                .WithMany(w => w.StockReceipts)
                .HasForeignKey(sr => sr.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockIssue>()
                .HasOne(si => si.Warehouse)
                .WithMany(w => w.StockIssues)
                .HasForeignKey(si => si.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            // modelBuilder.Entity<Material>()
            //     .HasOne(m => m.Supplier)
            //     .WithMany(s => s.Materials)
            //     .HasForeignKey(m => m.SupplierId)
            //     .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Material>(e =>
            {
                // Quan hệ Supplier như bạn đang dùng
                e.HasOne(m => m.Supplier)
                .WithMany(s => s.Materials)
                .HasForeignKey(m => m.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(m => m.Warehouse)
                 .WithMany(w => w.Materials)
                 .HasForeignKey(m => m.WarehouseId)
                 .OnDelete(DeleteBehavior.SetNull);



                // Thuộc tính & ràng buộc cơ bản
                e.Property(x => x.Code).HasMaxLength(64).IsRequired();   // gợi ý bắt buộc mã
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Unit).HasMaxLength(32).IsRequired();

                e.Property(x => x.Specification).HasMaxLength(256);      // Quy cách
                e.Property(x => x.PurchasePrice).HasPrecision(18, 2);    // Giá nhập
                e.Property(x => x.SellingPrice).HasPrecision(18, 2);     // Giá bán
                e.Property(x => x.StockQuantity).HasDefaultValue(0);     // Tồn kho mặc định 0
                e.Property(x => x.MinimumStock).HasPrecision(18, 2);     // Tồn tối thiểu
                e.Property(x => x.MaximumStock).HasPrecision(18, 2);     // Tồn tối đa
                e.Property(x => x.ReorderQuantity).HasPrecision(18, 2);  // Số lượng đặt lại
                e.Property(x => x.CostingMethod).HasConversion<int>();  // Phương pháp tính giá (mặc định WeightedAverage trong code)



                // (Khuyến nghị) Mã vật tư duy nhất
                e.HasIndex(x => x.Code).IsUnique();
            });

            // ===== MaterialSpecification =====
            modelBuilder.Entity<MaterialSpecification>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
            });
            // ===== StockBalance =====
            modelBuilder.Entity<StockBalance>(e =>
            {
                // Nếu EF7/8 + SQL Server:
                e.Property(x => x.Date).HasColumnType("date");
                // Nếu EF6 hoặc không hỗ trợ DateOnly thì dùng DateTime như nói ở trên và bỏ dòng này.

                e.Property(x => x.InQty   ).HasPrecision(18,3).HasDefaultValue(0);
                e.Property(x => x.OutQty  ).HasPrecision(18,3).HasDefaultValue(0);
                e.Property(x => x.InValue ).HasPrecision(18,2).HasDefaultValue(0);
                e.Property(x => x.OutValue).HasPrecision(18,2).HasDefaultValue(0);

                e.HasIndex(x => new { x.WarehouseId, x.MaterialId, x.Date }).IsUnique();

                e.HasOne(x => x.Warehouse).WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Material).WithMany()
                .HasForeignKey(x => x.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            });



            // ===== UserWarehouse n-n =====
            modelBuilder.Entity<UserWarehouse>()
                .HasOne(uw => uw.User)
                .WithMany(u => u.UserWarehouses)
                .HasForeignKey(uw => uw.UserId);

            modelBuilder.Entity<UserWarehouse>()
                .HasOne(uw => uw.Warehouse)
                .WithMany(w => w.UserWarehouses)
                .HasForeignKey(uw => uw.WarehouseId);

            modelBuilder.Entity<Stock>(e =>
            {
                // Tồn hiện tại cho từng (Kho, Vật tư)
                e.ToTable("Stocks");

                // Dùng unique index như bạn đã có
                e.HasIndex(x => new { x.WarehouseId, x.MaterialId }).IsUnique();

                // Precision & default
                e.Property(x => x.Quantity)
                .HasPrecision(18, 3)
                .HasDefaultValue(0);

                // Liên kết bắt buộc; không cho xoá kho/vật tư kéo theo tồn
                e.HasOne(x => x.Warehouse)
                .WithMany() // không cần .Materials ở đây
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Material)
                .WithMany() // không cần .Stocks ở Material
                .HasForeignKey(x => x.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);

                // (Tuỳ chọn) nếu có RowVersion trong entity để khoá cạnh tranh:
                // e.Property(x => x.RowVersion).IsRowVersion();
            });



            // ===== Người TẠO phiếu (có inverse ở User) =====
            modelBuilder.Entity<StockReceipt>()
                .HasOne(sr => sr.CreatedBy)
                .WithMany(u => u.CreatedReceipts)        // inverse đã có trong User
                .HasForeignKey(sr => sr.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockIssue>()
                .HasOne(si => si.CreatedBy)
                .WithMany(u => u.CreatedIssues)          // inverse đã có trong User
                .HasForeignKey(si => si.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // ===== Người DUYỆT phiếu (không có inverse trong User) =====
            modelBuilder.Entity<StockReceipt>()
                .HasOne(sr => sr.ApprovedBy)
                .WithMany()                               // không inverse, tránh mơ hồ
                .HasForeignKey(sr => sr.ApprovedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockIssue>()
                .HasOne(si => si.ApprovedBy)
                .WithMany()
                .HasForeignKey(si => si.ApprovedById)
                .OnDelete(DeleteBehavior.Restrict);

            // ===== User Approval Relationship =====
            modelBuilder.Entity<User>()
                .HasOne(u => u.ApprovedBy)
                .WithMany(u => u.ApprovedUsers)
                .HasForeignKey(u => u.ApprovedById)
                .OnDelete(DeleteBehavior.Restrict);

            // ===== Precision giá =====
            modelBuilder.Entity<StockReceiptDetail>()
                .Property(d => d.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<StockIssueDetail>()
                .Property(d => d.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<StockIssueDetail>(e =>
            {
                e.Property(p => p.Specification)
                .HasMaxLength(256)
                .IsRequired(false); // <-- cho phép null
            });


            // Decimal precision cho UnitPrice, QuantityDiff, Quantity…
            modelBuilder.Entity<StockReceiptDetail>().Property(p => p.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<StockIssueDetail>().Property(p => p.UnitPrice).HasPrecision(18, 2);
            // StockAdjustment đã bị vô hiệu hóa - chức năng không còn sử dụng
            // modelBuilder.Entity<StockAdjustmentDetail>().Property(p => p.QuantityDiff).HasPrecision(18, 3);
            modelBuilder.Entity<StockTransferDetail>().Property(p => p.Quantity).HasPrecision(18, 3);

            modelBuilder.Entity<StockReceiptDetail>()
                .Property(p => p.Quantity).HasPrecision(18,3);
            modelBuilder.Entity<StockReceiptDetail>()
                .Property(p => p.UnitPrice).HasPrecision(18,2);

            modelBuilder.Entity<StockIssueDetail>()
                .Property(p => p.Quantity).HasPrecision(18,3);

            // ===== LOTS =====
            modelBuilder.Entity<StockLot>(e =>
            {
                e.HasIndex(x => new { x.WarehouseId, x.MaterialId, x.LotNumber, x.ManufactureDate, x.ExpiryDate }).IsUnique();
                e.Property(x => x.Quantity).HasPrecision(18,3);
            });

            modelBuilder.Entity<StockIssueAllocation>(e =>
            {
                e.Property(x => x.Quantity).HasPrecision(18,3);
                e.HasOne(a => a.StockIssueDetail).WithMany().HasForeignKey(a => a.StockIssueDetailId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(a => a.StockLot).WithMany().HasForeignKey(a => a.StockLotId).OnDelete(DeleteBehavior.Restrict);
            });

            // AppDbContext.OnModelCreating(...)
            modelBuilder.Entity<DocumentNumbering>(e =>
            {
                e.HasIndex(x => new { x.DocumentType, x.WarehouseId, x.Year }).IsUnique();
                e.Property(x => x.DocumentType).HasMaxLength(64).IsRequired();
                e.Property(x => x.Prefix).HasMaxLength(16).IsRequired();
                e.Property(x => x.Format).HasMaxLength(128);
                e.Property(x => x.CurrentNo).IsRequired();
            });

            // ===== Document =====
            modelBuilder.Entity<Document>(e =>
            {
                e.HasIndex(x => new { x.DocumentType, x.DocumentId });
                
                e.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
                e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
                e.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
                e.Property(x => x.MimeType).HasMaxLength(100);
                e.Property(x => x.Description).HasMaxLength(500);

                e.HasOne(x => x.UploadedBy)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedById)
                    .OnDelete(DeleteBehavior.Restrict);
            });


            // Ràng buộc chuyển kho
           modelBuilder.Entity<StockTransfer>(e =>
            {
                e.Property(x => x.TransferNumber).HasMaxLength(50).IsRequired();
                e.Property(x => x.Note).HasMaxLength(1000);
                e.HasOne(x => x.FromWarehouse).WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ToWarehouse).WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ApprovedBy).WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<StockTransferDetail>(e =>
            {
                e.Property(x => x.Quantity).HasColumnType("decimal(18,4)");
                e.Property(x => x.UnitPrice).HasPrecision(18, 2);
                e.Property(x => x.Unit).HasMaxLength(50);
                e.Property(x => x.Note).HasMaxLength(500);
                e.HasOne(d => d.StockTransfer).WithMany(h => h.Details).HasForeignKey(d => d.StockTransferId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(d => d.Lot).WithMany().HasForeignKey(d => d.LotId).OnDelete(DeleteBehavior.NoAction);
            });
            // Attachment và PeriodLock đã bị xóa - các bảng không sử dụng
            // modelBuilder.Entity<Attachment>()
            //  .HasOne(a => a.UploadedBy).WithMany()
            //  .HasForeignKey(a => a.UploadedById)
            //  .OnDelete(DeleteBehavior.Restrict);

            // modelBuilder.Entity<PeriodLock>()
            //  .HasOne(p => p.LockedBy).WithMany()
            //  .HasForeignKey(p => p.LockedById)
            //  .OnDelete(DeleteBehavior.Restrict);

            // ===== NotificationSettings =====
            modelBuilder.Entity<NotificationSettings>(e =>
            {
                e.HasKey(x => x.UserId);
                e.HasOne(x => x.User)
                    .WithOne()
                    .HasForeignKey<NotificationSettings>(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.Property(x => x.SoundType).HasMaxLength(50).HasDefaultValue("default");
                e.Property(x => x.UpdateFrequency).HasDefaultValue(30);
            });

            // ===== Notification indexes =====
            modelBuilder.Entity<Notification>(e =>
            {
                e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
                e.HasIndex(x => new { x.UserId, x.IsImportant });
                e.HasIndex(x => new { x.UserId, x.IsArchived });
                e.HasIndex(x => new { x.IsDeleted, x.DeletedAt });
            });

            // Tùy chọn: index hiệu năng
            modelBuilder.Entity<StockReceipt>().HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
            modelBuilder.Entity<StockIssue>().HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
            modelBuilder.Entity<StockReceiptDetail>().HasIndex(x => new { x.StockReceiptId, x.MaterialId });
            modelBuilder.Entity<StockIssueDetail>().HasIndex(x => new { x.StockIssueId, x.MaterialId });

            // ===== DemandForecast =====
            modelBuilder.Entity<DemandForecast>(e =>
            {
                e.HasIndex(x => new { x.MaterialId, x.WarehouseId, x.ForecastDate }).IsUnique();
                e.Property(x => x.ForecastedQuantity).HasPrecision(18, 3);
                e.Property(x => x.ConfidenceLevel).HasPrecision(5, 2);
                e.Property(x => x.HistoricalAverage).HasPrecision(18, 3);
                e.Property(x => x.Trend).HasPrecision(18, 3);
                e.Property(x => x.Method).HasMaxLength(50).IsRequired();
                e.Property(x => x.Notes).HasMaxLength(500);

                e.HasOne(x => x.Material)
                    .WithMany()
                    .HasForeignKey(x => x.MaterialId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Warehouse)
                    .WithMany()
                    .HasForeignKey(x => x.WarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== ChatMessage =====
            modelBuilder.Entity<ChatMessage>(e =>
            {
                e.HasIndex(x => new { x.UserId, x.CreatedAt });
                e.Property(x => x.Role).HasMaxLength(10).IsRequired();
                e.Property(x => x.Message).HasMaxLength(5000).IsRequired();

                e.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

    }

    
}
