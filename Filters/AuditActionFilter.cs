using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using System.Text;
using MNBEMART.Services;
using System.Globalization;

namespace MNBEMART.Filters
{
    // Ghi nhận audit cho mọi action thành công
    public class AuditActionFilter : IAsyncActionFilter
    {
        private readonly IAuditService _audit;
        public AuditActionFilter(IAuditService audit) { _audit = audit; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            if (executed.Exception != null) return; // bỏ qua khi lỗi

            var http = context.HttpContext;

            // Bỏ qua audit cho chatbot (api/chat/*)
            var ctrl = (string?)context.RouteData.Values["controller"] ?? "";
            if (string.Equals(ctrl, "Chat", StringComparison.OrdinalIgnoreCase))
                return;

            var user = http.User;
            var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var uid) || uid <= 0) return; // chưa đăng nhập

            var act  = (string?)context.RouteData.Values["action"] ?? "";
            var method = http.Request.Method?.ToUpperInvariant() ?? "GET";

            string action = MapAction(act, method);
            // BỎ QUA các thao tác chỉ đọc (GET/"Read")
            if (string.Equals(action, "Read", StringComparison.OrdinalIgnoreCase)) return;

            string module = ctrl;
            string objectType = ctrl;
            string? objectId = TryFindObjectId(context);
            int? warehouseId = TryFindWarehouseId(http);

            var content = BuildContent(http, act);
            try
            {
                await _audit.LogAsync(uid, action, objectType, objectId, module: module, warehouseId: warehouseId, content: content);
            }
            catch { /* không làm ảnh hưởng luồng chính */ }
        }

        private static string MapAction(string actionName, string method)
        {
            var n = (actionName ?? "").ToLowerInvariant();
            if (n.Contains("login")) return "Login";
            if (n.Contains("logout")) return "Logout";
            if (n.Contains("create") || n.Contains("add")) return "Create";
            if (n.Contains("edit") || n.Contains("update")) return "Update";
            if (n.Contains("delete") || n.Contains("remove") || n.Contains("bulkdelete") || n.Contains("rejectpost") || n == "reject") return "Delete";
            if (n.Contains("approve")) return "Approve";
            if (n.Contains("complete")) return "Complete";
            if (n.Contains("cancel")) return "Cancel";
            if (n.Contains("receive")) return "Receive";
            if (n.Contains("issue")) return "Issue";
            if (n.Contains("reject")) return "Reject";
            if (n.Contains("print")) return "Print";
            if (n.Contains("export")) return "Export";
            if (n.Contains("downloadtemplate")) return "DownloadTemplate";
            if (n.Contains("importcsv") || n.Contains("import")) return "ImportCsv";
            if (method == "POST") return "Post";
            return "Read";
        }

        private static string? TryFindObjectId(ActionExecutingContext ctx)
        {
            // Lấy tham số id nếu có
            if (ctx.ActionArguments.TryGetValue("id", out var v) && v != null)
                return v.ToString();
            return null;
        }

        private static int? TryFindWarehouseId(HttpContext http)
        {
            var req = http.Request;

            // Ưu tiên query string
            string? s = req.Query["warehouseId"].FirstOrDefault()
                      ?? req.Query["fromWarehouseId"].FirstOrDefault()
                      ?? req.Query["WarehouseId"].FirstOrDefault();

            // Nếu không có trong query thì thử route values
            if (string.IsNullOrEmpty(s) && req.RouteValues.TryGetValue("warehouseId", out var rv))
                s = rv?.ToString();

            // Chỉ đọc Form khi Content-Type là form để tránh InvalidOperationException
            if (string.IsNullOrEmpty(s) && req.HasFormContentType)
            {
                s = req.Form["WarehouseId"].FirstOrDefault()
                  ?? req.Form["FromWarehouseId"].FirstOrDefault()
                  ?? req.Form["warehouseId"].FirstOrDefault();
            }

            if (int.TryParse(s, out var wid)) return wid;
            return null;
        }

        private static string BuildContent(HttpContext http, string actionName)
        {
            var ctrl = (string?)http.Request.RouteValues["controller"] ?? "";
            var act = actionName ?? "";
            
            // Lấy mô tả tiếng Việt cho action
            var description = GetActionDescription(ctrl, act);
            
            // Parse query string để lấy thông tin có ý nghĩa
            var queryInfo = ParseQueryString(http.Request.Query);
            
            // Tạo nội dung dễ hiểu
            var sb = new StringBuilder();
            sb.Append(description);
            
            if (!string.IsNullOrEmpty(queryInfo))
            {
                sb.Append(" (");
                sb.Append(queryInfo);
                sb.Append(")");
            }
            
            // Thêm thông tin import nếu có
            if (act?.ToLowerInvariant().Contains("import") == true)
            {
                if (!string.IsNullOrEmpty(queryInfo))
                    sb.Append(" - ");
                else
                    sb.Append(" (");
                sb.Append("Nhập file");
                if (string.IsNullOrEmpty(queryInfo))
                    sb.Append(")");
            }
            
            return sb.ToString();
        }

        private static string GetActionDescription(string controller, string action)
        {
            var ctrlLower = controller.ToLowerInvariant();
            var actLower = (action ?? "").ToLowerInvariant();
            
            // Reports Controller - Export PDF
            if (ctrlLower == "reports")
            {
                if (actLower.Contains("exportpdfrevenue"))
                    return "Xuất PDF báo cáo doanh thu";
                if (actLower.Contains("exportpdfprofitloss") || actLower.Contains("exportpdfp&l"))
                    return "Xuất PDF báo cáo lợi nhuận";
                if (actLower.Contains("exportpdfturnoverrate"))
                    return "Xuất PDF báo cáo tỷ lệ quay vòng";
                if (actLower.Contains("exportpdfmovements"))
                    return "Xuất PDF báo cáo nhập xuất";
                if (actLower.Contains("exportpdfcogs"))
                    return "Xuất PDF báo cáo giá vốn hàng bán";
                if (actLower.Contains("exportpdfinventoryvalue"))
                    return "Xuất PDF báo cáo NXT theo giá trị";
                if (actLower.Contains("exportpdfinventoryvaluation"))
                    return "Xuất PDF báo cáo định giá tồn kho";
                if (actLower.Contains("exportpdf"))
                    return "Xuất PDF báo cáo";
                if (actLower.Contains("export") || actLower.Contains("excel"))
                    return "Xuất file Excel báo cáo";
            }
            
            // Notifications Controller
            if (ctrlLower == "notifications")
            {
                if (actLower.Contains("getimportant"))
                    return "Nhập file CSV thông báo";
                if (actLower.Contains("import"))
                    return "Nhập file CSV thông báo";
            }
            
            // Stock Receipts Controller
            if (ctrlLower == "stockreceipts")
            {
                if (actLower.Contains("exportpdf"))
                    return "Xuất PDF phiếu nhập kho";
                if (actLower.Contains("exportcsv") || actLower.Contains("export"))
                    return "Xuất file Excel phiếu nhập kho";
                if (actLower.Contains("importcsv") || actLower.Contains("import"))
                    return "Nhập file Excel phiếu nhập kho";
            }
            
            // Stock Issues Controller
            if (ctrlLower == "stockissues")
            {
                if (actLower.Contains("exportpdf"))
                    return "Xuất PDF phiếu xuất kho";
                if (actLower.Contains("exportcsv") || actLower.Contains("export"))
                    return "Xuất file Excel phiếu xuất kho";
                if (actLower.Contains("importcsv") || actLower.Contains("import"))
                    return "Nhập file Excel phiếu xuất kho";
            }
            
            // Stocks Controller
            if (ctrlLower == "stocks")
            {
                if (actLower.Contains("exportpdf"))
                    return "Xuất PDF báo cáo tồn kho";
                if (actLower.Contains("export"))
                    return "Xuất file Excel báo cáo tồn kho";
            }
            
            // Materials Controller
            if (ctrlLower == "materials")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Thêm nguyên liệu";
                if (actLower.Contains("edit") || actLower.Contains("update"))
                    return "Cập nhật nguyên liệu";
                if (actLower.Contains("delete"))
                    return "Xóa nguyên liệu";
            }
            
            // Warehouses Controller
            if (ctrlLower == "warehouses")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Thêm kho";
                if (actLower.Contains("edit") || actLower.Contains("update"))
                    return "Cập nhật kho";
                if (actLower.Contains("delete"))
                    return "Xóa kho";
            }
            
            // Suppliers Controller
            if (ctrlLower == "suppliers")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Thêm nhà cung cấp";
                if (actLower.Contains("edit") || actLower.Contains("update"))
                    return "Cập nhật nhà cung cấp";
                if (actLower.Contains("delete"))
                    return "Xóa nhà cung cấp";
            }
            
            // Users Controller
            if (ctrlLower == "users")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Thêm người dùng";
                if (actLower.Contains("edit") || actLower.Contains("update"))
                    return "Cập nhật người dùng";
                if (actLower.Contains("delete"))
                    return "Xóa người dùng";
            }
            
            // Purchase Requests Controller
            if (ctrlLower == "purchaserequests")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Tạo đề xuất đặt hàng";
                if (actLower.Contains("approve"))
                    return "Duyệt đề xuất đặt hàng";
                if (actLower.Contains("reject"))
                    return "Từ chối đề xuất đặt hàng";
            }
            
            // Stock Transfers Controller
            if (ctrlLower == "stocktransfers")
            {
                if (actLower.Contains("create") || actLower.Contains("add"))
                    return "Tạo phiếu chuyển kho";
                if (actLower.Contains("complete"))
                    return "Hoàn thành chuyển kho";
                if (actLower.Contains("cancel"))
                    return "Hủy chuyển kho";
            }
            
            // Mặc định: dùng tên action
            return $"{controller}/{action}";
        }

        private static string ParseQueryString(Microsoft.AspNetCore.Http.IQueryCollection query)
        {
            var parts = new List<string>();
            
            // Bỏ qua From và To vì thời gian đã có cột riêng hiển thị
            
            // Parse WarehouseId (nếu có thể lấy tên kho thì tốt, nhưng ở đây chỉ hiển thị ID)
            // Note: Để lấy tên kho cần truy cập database, nhưng filter không nên phụ thuộc vào DB
            // Nên chỉ hiển thị ID hoặc bỏ qua
            
            // Parse các tham số khác có ý nghĩa
            var meaningfulParams = new[] { "GroupBy", "Period", "Status" };
            foreach (var param in meaningfulParams)
            {
                if (query.ContainsKey(param))
                {
                    var value = query[param].FirstOrDefault();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var displayName = param switch
                        {
                            "GroupBy" => "Nhóm theo",
                            "Period" => "Kỳ",
                            "Status" => "Trạng thái",
                            _ => param
                        };
                        parts.Add($"{displayName}: {value}");
                    }
                }
            }
            
            return string.Join(", ", parts);
        }
    }
}
