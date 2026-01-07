using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MNBEMART.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _fromName;
        private readonly string _baseUrl;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _smtpServer = _configuration["Email:SmtpServer"] ?? "smtp.gmail.com";
            _smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            _smtpUsername = _configuration["Email:Username"] ?? "";
            _smtpPassword = _configuration["Email:Password"] ?? "";
            _fromEmail = _configuration["Email:FromEmail"] ?? _smtpUsername;
            _fromName = _configuration["Email:FromName"] ?? "BEMART System";
            _baseUrl = _configuration["Email:BaseUrl"] ?? "http://localhost:5073";
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            try
            {
                // Nếu không có cấu hình email, log và return false (không throw error)
                if (string.IsNullOrEmpty(_smtpUsername) || string.IsNullOrEmpty(_smtpPassword))
                {
                    _logger.LogWarning("Email service not configured. Skipping email send to {Email}", to);
                    return false;
                }

                using var client = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword)
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    To = { new MailAddress(to) },
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };

                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent successfully to {Email}", to);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {Email}", to);
                return false;
            }
        }

        public async Task SendRegistrationNotificationToAdminAsync(string userFullName, string userEmail, string username, int userId)
        {
            var subject = "Có tài khoản mới cần duyệt - BEMART";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #2563eb;'>Thông báo: Tài khoản mới cần duyệt</h2>
                        <p>Xin chào Admin,</p>
                        <p>Có một tài khoản mới đã đăng ký và cần được duyệt:</p>
                        <div style='background-color: #f8fafc; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <p><strong>Họ tên:</strong> {userFullName}</p>
                            <p><strong>Email:</strong> {userEmail}</p>
                            <p><strong>Username:</strong> {username}</p>
                            <p><strong>Ngày đăng ký:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}</p>
                        </div>
                        <p>
                            <a href='{_baseUrl}/Users/Details/{userId}' 
                               style='background-color: #2563eb; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem và duyệt tài khoản
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            // Lấy danh sách admin emails (có thể lấy từ config hoặc database)
            var adminEmails = _configuration["Email:AdminEmails"]?.Split(',') ?? new[] { _fromEmail };
            
            foreach (var adminEmail in adminEmails)
            {
                await SendEmailAsync(adminEmail.Trim(), subject, body);
            }
        }

        public async Task SendAccountActivatedEmailAsync(string userEmail, string userFullName, string username, string loginUrl)
        {
            var subject = "Tài khoản BEMART của bạn đã được kích hoạt";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #10b981;'>Tài khoản đã được kích hoạt</h2>
                        <p>Xin chào <strong>{userFullName}</strong>,</p>
                        <p>Tài khoản <strong>{username}</strong> của bạn đã được admin duyệt và kích hoạt thành công.</p>
                        <p>Bạn có thể đăng nhập vào hệ thống ngay bây giờ:</p>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{loginUrl}' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Đăng nhập ngay
                            </a>
                        </p>
                        <div style='background-color: #f0fdf4; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #10b981;'>
                            <p style='margin: 0;'><strong>Thông tin đăng nhập:</strong></p>
                            <p style='margin: 5px 0;'><strong>Username:</strong> {username}</p>
                            <p style='margin: 5px 0;'><strong>Email:</strong> {userEmail}</p>
                        </div>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Nếu bạn không yêu cầu tài khoản này, vui lòng liên hệ admin ngay lập tức.
                        </p>
                    </div>
                </body>
                </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        public async Task SendAccountRejectedEmailAsync(string userEmail, string userFullName, string reason)
        {
            var subject = "Đơn đăng ký tài khoản BEMART";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #ef4444;'>Đơn đăng ký không được duyệt</h2>
                        <p>Xin chào <strong>{userFullName}</strong>,</p>
                        <p>Rất tiếc, đơn đăng ký tài khoản của bạn không được duyệt.</p>
                        {(string.IsNullOrEmpty(reason) ? "" : $"<p><strong>Lý do:</strong> {reason}</p>")}
                        <p>Nếu bạn có thắc mắc, vui lòng liên hệ với admin để được hỗ trợ.</p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        public async Task SendPasswordResetEmailAsync(string userEmail, string userFullName, string resetToken, string resetUrl)
        {
            var subject = "Đặt lại mật khẩu BEMART";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #2563eb;'>Yêu cầu đặt lại mật khẩu</h2>
                        <p>Xin chào <strong>{userFullName}</strong>,</p>
                        <p>Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản BEMART.</p>
                        <p>Vui lòng click vào link bên dưới để đặt lại mật khẩu:</p>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{resetUrl}' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Đặt lại mật khẩu
                            </a>
                        </p>
                        <p style='font-size: 12px; color: #666;'>
                            Hoặc copy link sau vào trình duyệt:<br/>
                            <span style='word-break: break-all;'>{resetUrl}</span>
                        </p>
                        <div style='background-color: #fef3c7; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #f59e0b;'>
                            <p style='margin: 0;'><strong>⚠️ Lưu ý:</strong></p>
                            <ul style='margin: 10px 0; padding-left: 20px;'>
                                <li>Link này có hiệu lực trong <strong>24 giờ</strong></li>
                                <li>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này</li>
                                <li>Mật khẩu của bạn sẽ không thay đổi nếu bạn không click vào link trên</li>
                            </ul>
                        </div>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng liên hệ admin ngay lập tức.
                        </p>
                    </div>
                </body>
                </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        public async Task SendPasswordChangedConfirmationEmailAsync(string userEmail, string userFullName)
        {
            var subject = "Mật khẩu đã được thay đổi - BEMART";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #10b981;'>Mật khẩu đã được thay đổi</h2>
                        <p>Xin chào <strong>{userFullName}</strong>,</p>
                        <p>Mật khẩu tài khoản BEMART của bạn đã được thay đổi thành công.</p>
                        <p><strong>Thời gian:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}</p>
                        <div style='background-color: #fef2f2; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #ef4444;'>
                            <p style='margin: 0;'><strong>⚠️ Cảnh báo bảo mật:</strong></p>
                            <p style='margin: 10px 0;'>Nếu bạn không thực hiện thay đổi này, vui lòng:</p>
                            <ul style='margin: 10px 0; padding-left: 20px;'>
                                <li>Liên hệ admin ngay lập tức</li>
                                <li>Đổi mật khẩu ngay nếu bạn vẫn có thể đăng nhập</li>
                                <li>Kiểm tra hoạt động đăng nhập gần đây</li>
                            </ul>
                        </div>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        public async Task SendPurchaseRequestNotificationAsync(int purchaseRequestId, string requestNumber, string requestedByName, List<string> materialNames, decimal totalQuantity)
        {
            var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                            ?? new List<string>();

            if (adminEmails.Count == 0)
                return; // Không có admin email để gửi

            var subject = $"Đề xuất đặt hàng mới: {requestNumber} - BEMART";
            var materialList = string.Join("", materialNames.Take(10).Select((name, idx) => $"<li>{idx + 1}. {name}</li>"));
            if (materialNames.Count > 10)
                materialList += $"<li>... và {materialNames.Count - 10} vật tư khác</li>";

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #2563eb;'>Đề xuất đặt hàng mới</h2>
                        <p>Xin chào Admin,</p>
                        <p>Có một đề xuất đặt hàng mới cần được duyệt:</p>
                        <div style='background-color: #f8fafc; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <p><strong>Số đề xuất:</strong> {requestNumber}</p>
                            <p><strong>Người đề xuất:</strong> {requestedByName}</p>
                            <p><strong>Tổng số lượng:</strong> {totalQuantity:N0}</p>
                            <p><strong>Số vật tư:</strong> {materialNames.Count}</p>
                            <p><strong>Danh sách vật tư:</strong></p>
                            <ul style='margin: 10px 0; padding-left: 20px;'>
                                {materialList}
                            </ul>
                            <p><strong>Thời gian:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}</p>
                        </div>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_baseUrl}/PurchaseRequests/Details/{purchaseRequestId}' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem và duyệt đề xuất
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            foreach (var email in adminEmails)
            {
                await SendEmailAsync(email, subject, body);
            }
        }

        public async Task SendPurchaseRequestStatusChangeAsync(string userEmail, string userFullName, string requestNumber, string status, string? reason = null)
        {
            var statusText = status switch
            {
                "Approved" => "đã được duyệt",
                "Rejected" => "đã bị từ chối",
                "Ordered" => "đã được đặt hàng",
                "Cancelled" => "đã bị hủy",
                _ => status
            };

            var statusColor = status switch
            {
                "Approved" => "#10b981",
                "Rejected" => "#ef4444",
                "Ordered" => "#2563eb",
                "Cancelled" => "#6b7280",
                _ => "#6b7280"
            };

            var subject = $"Đề xuất đặt hàng {requestNumber} {statusText.ToLower()} - BEMART";
            
            var reasonSection = !string.IsNullOrEmpty(reason) 
                ? $@"
                    <div style='background-color: #fef3c7; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #f59e0b;'>
                        <p style='margin: 0;'><strong>Lý do:</strong> {reason}</p>
                    </div>"
                : "";

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: {statusColor};'>Đề xuất đặt hàng {statusText}</h2>
                        <p>Xin chào <strong>{userFullName}</strong>,</p>
                        <p>Đề xuất đặt hàng <strong>{requestNumber}</strong> của bạn {statusText}.</p>
                        {reasonSection}
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_baseUrl}/PurchaseRequests/Details' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem chi tiết
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        public async Task SendExpiryAlertEmailAsync(List<ExpiryAlertItem> expiredItems, List<ExpiryAlertItem> expiringSoonItems)
        {
            var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                            ?? new List<string>();

            if (adminEmails.Count == 0 || (expiredItems.Count == 0 && expiringSoonItems.Count == 0))
                return;

            var subject = $"Cảnh báo hết hạn hàng hóa - BEMART ({expiredItems.Count} đã hết hạn, {expiringSoonItems.Count} sắp hết hạn)";
            
            var expiredSection = expiredItems.Count > 0
                ? $@"
                    <div style='background-color: #fef2f2; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #ef4444;'>
                        <h3 style='color: #ef4444; margin-top: 0;'>Đã hết hạn ({expiredItems.Count} lô)</h3>
                        <table style='width: 100%; border-collapse: collapse;'>
                            <thead>
                                <tr style='background-color: #fee2e2;'>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Mã vật tư</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Tên vật tư</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Số lô</th>
                                    <th style='padding: 8px; text-align: right; border-bottom: 1px solid #fecaca;'>SL</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Hết hạn</th>
                                </tr>
                            </thead>
                            <tbody>
                                {string.Join("", expiredItems.Take(20).Select(item => $@"
                                    <tr>
                                        <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.MaterialCode}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.MaterialName}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.LotNumber}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fee2e2; text-align: right;'>{item.Quantity:N0} {item.Unit}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fee2e2; color: #ef4444;'>{item.ExpiryDate:dd/MM/yyyy}</td>
                                    </tr>"))}
                            </tbody>
                        </table>
                        {(expiredItems.Count > 20 ? $"<p style='font-size: 12px; color: #666;'>... và {expiredItems.Count - 20} lô khác</p>" : "")}
                    </div>"
                : "";

            var expiringSoonSection = expiringSoonItems.Count > 0
                ? $@"
                    <div style='background-color: #fef3c7; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #f59e0b;'>
                        <h3 style='color: #f59e0b; margin-top: 0;'>Sắp hết hạn ({expiringSoonItems.Count} lô)</h3>
                        <table style='width: 100%; border-collapse: collapse;'>
                            <thead>
                                <tr style='background-color: #fef3c7;'>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fde68a;'>Mã vật tư</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fde68a;'>Tên vật tư</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fde68a;'>Số lô</th>
                                    <th style='padding: 8px; text-align: right; border-bottom: 1px solid #fde68a;'>SL</th>
                                    <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fde68a;'>Còn lại</th>
                                </tr>
                            </thead>
                            <tbody>
                                {string.Join("", expiringSoonItems.Take(20).Select(item => $@"
                                    <tr>
                                        <td style='padding: 8px; border-bottom: 1px solid #fef3c7;'>{item.MaterialCode}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fef3c7;'>{item.MaterialName}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fef3c7;'>{item.LotNumber}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fef3c7; text-align: right;'>{item.Quantity:N0} {item.Unit}</td>
                                        <td style='padding: 8px; border-bottom: 1px solid #fef3c7; color: #f59e0b;'>{item.DaysRemaining} ngày</td>
                                    </tr>"))}
                            </tbody>
                        </table>
                        {(expiringSoonItems.Count > 20 ? $"<p style='font-size: 12px; color: #666;'>... và {expiringSoonItems.Count - 20} lô khác</p>" : "")}
                    </div>"
                : "";

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 800px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #f59e0b;'>Cảnh báo hết hạn hàng hóa</h2>
                        <p>Xin chào Admin,</p>
                        <p>Hệ thống phát hiện các lô hàng hóa đã hết hạn hoặc sắp hết hạn trong vòng 30 ngày tới.</p>
                        {expiredSection}
                        {expiringSoonSection}
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_baseUrl}/Materials/Expiring' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem chi tiết
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART. Email được gửi hàng ngày lúc 8:00 sáng.
                        </p>
                    </div>
                </body>
                </html>";

            foreach (var email in adminEmails)
            {
                await SendEmailAsync(email, subject, body);
            }
        }

        public async Task SendLowStockAlertEmailAsync(List<LowStockItem> lowStockItems)
        {
            var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                            ?? new List<string>();

            if (adminEmails.Count == 0 || lowStockItems.Count == 0)
                return;

            var subject = $"Cảnh báo tồn kho thấp - BEMART ({lowStockItems.Count} vật tư)";
            
            var itemsTable = $@"
                <table style='width: 100%; border-collapse: collapse;'>
                    <thead>
                        <tr style='background-color: #fee2e2;'>
                            <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Mã vật tư</th>
                            <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Tên vật tư</th>
                            <th style='padding: 8px; text-align: right; border-bottom: 1px solid #fecaca;'>Tồn hiện tại</th>
                            <th style='padding: 8px; text-align: right; border-bottom: 1px solid #fecaca;'>Tồn tối thiểu</th>
                            <th style='padding: 8px; text-align: left; border-bottom: 1px solid #fecaca;'>Kho</th>
                        </tr>
                    </thead>
                    <tbody>
                        {string.Join("", lowStockItems.Take(30).Select(item => $@"
                            <tr>
                                <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.MaterialCode}</td>
                                <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.MaterialName}</td>
                                <td style='padding: 8px; border-bottom: 1px solid #fee2e2; text-align: right; color: #ef4444;'>{item.CurrentStock:N0} {item.Unit}</td>
                                <td style='padding: 8px; border-bottom: 1px solid #fee2e2; text-align: right;'>{item.MinimumStock:N0} {item.Unit}</td>
                                <td style='padding: 8px; border-bottom: 1px solid #fee2e2;'>{item.WarehouseName}</td>
                            </tr>"))}
                    </tbody>
                </table>
                {(lowStockItems.Count > 30 ? $"<p style='font-size: 12px; color: #666;'>... và {lowStockItems.Count - 30} vật tư khác</p>" : "")}";

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 800px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #ef4444;'>Cảnh báo tồn kho thấp</h2>
                        <p>Xin chào Admin,</p>
                        <p>Hệ thống phát hiện <strong>{lowStockItems.Count} vật tư</strong> có tồn kho dưới mức tối thiểu:</p>
                        <div style='background-color: #fef2f2; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #ef4444;'>
                            {itemsTable}
                        </div>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_baseUrl}/PurchaseRequests/Create' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Tạo đề xuất đặt hàng
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            foreach (var email in adminEmails)
            {
                await SendEmailAsync(email, subject, body);
            }
        }

        public async Task<bool> SendNotificationEmailAsync(string userEmail, Models.Notification notification)
        {
            if (string.IsNullOrEmpty(userEmail) || notification == null)
                return false;

            var typeText = notification.Type switch
            {
                Models.NotificationType.Receipt => "Phiếu nhập",
                Models.NotificationType.Issue => "Phiếu xuất",
                Models.NotificationType.Transfer => "Phiếu chuyển kho",
                Models.NotificationType.PurchaseRequest => "Đề xuất mua hàng",
                Models.NotificationType.ExpiryAlert => "Cảnh báo hết hạn",
                Models.NotificationType.LowStockAlert => "Cảnh báo tồn kho thấp",
                Models.NotificationType.UserRegistration => "Đăng ký tài khoản",
                Models.NotificationType.RoleCreated => "Vai trò mới",
                Models.NotificationType.WarehouseCreated => "Kho mới",
                Models.NotificationType.UnconfirmedDocument => "Phiếu chưa xác nhận",
                _ => "Thông báo hệ thống"
            };

            var priorityColor = notification.Priority switch
            {
                Models.NotificationPriority.Urgent => "#ef4444",
                Models.NotificationPriority.High => "#f59e0b",
                Models.NotificationPriority.Normal => "#2563eb",
                _ => "#6b7280"
            };

            var detailUrl = notification.Type switch
            {
                Models.NotificationType.Receipt => $"{_baseUrl}/StockReceipts/Details/{notification.DocumentId}",
                Models.NotificationType.Issue => $"{_baseUrl}/StockIssues/Details/{notification.DocumentId}",
                Models.NotificationType.Transfer => $"{_baseUrl}/StockTransfers/Details/{notification.DocumentId}",
                Models.NotificationType.PurchaseRequest => $"{_baseUrl}/PurchaseRequests/Details/{notification.DocumentId}",
                Models.NotificationType.ExpiryAlert => $"{_baseUrl}/Materials/Expiring",
                Models.NotificationType.LowStockAlert => $"{_baseUrl}/Stocks/Index",
                Models.NotificationType.UserRegistration => $"{_baseUrl}/Users/Details/{notification.DocumentId}",
                _ => $"{_baseUrl}/Notifications/Index"
            };

            var subject = notification.IsImportant 
                ? $"⭐ {notification.Title} - BEMART"
                : $"{notification.Title} - BEMART";

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: {priorityColor};'>{(notification.IsImportant ? "⭐ " : "")}{notification.Title}</h2>
                        {(string.IsNullOrEmpty(notification.Message) ? "" : $"<p>{notification.Message}</p>")}
                        <div style='background-color: #f8fafc; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <p><strong>Loại:</strong> {typeText}</p>
                            <p><strong>Mức độ:</strong> {notification.Priority}</p>
                            <p><strong>Thời gian:</strong> {notification.CreatedAt:dd/MM/yyyy HH:mm}</p>
                        </div>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{detailUrl}' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem chi tiết
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART.
                        </p>
                    </div>
                </body>
                </html>";

            return await SendEmailAsync(userEmail, subject, body);
        }

        public async Task<bool> SendNotificationDigestAsync(string userEmail, List<Models.Notification> notifications, string frequency)
        {
            if (string.IsNullOrEmpty(userEmail) || notifications == null || !notifications.Any())
                return false;
            
            var frequencyText = frequency switch
            {
                "Daily" => "hàng ngày",
                "Weekly" => "hàng tuần",
                _ => frequency
            };

            var subject = $"Tóm tắt thông báo {frequencyText} - BEMART ({notifications.Count} thông báo)";
            
            var notificationList = string.Join("", notifications.Take(20).Select((n, idx) => {
                var typeText = n.Type switch
                {
                    Models.NotificationType.Receipt => "Phiếu nhập",
                    Models.NotificationType.Issue => "Phiếu xuất",
                    Models.NotificationType.Transfer => "Phiếu chuyển kho",
                    Models.NotificationType.PurchaseRequest => "Đề xuất mua hàng",
                    Models.NotificationType.ExpiryAlert => "Cảnh báo hết hạn",
                    Models.NotificationType.LowStockAlert => "Cảnh báo tồn kho",
                    _ => n.Type.ToString()
                };
                return $@"
                <tr>
                    <td style='padding: 8px; border-bottom: 1px solid #e5e7eb;'>{idx + 1}</td>
                    <td style='padding: 8px; border-bottom: 1px solid #e5e7eb;'>{(n.IsImportant ? "⭐ " : "")}{n.Title}</td>
                    <td style='padding: 8px; border-bottom: 1px solid #e5e7eb;'>{typeText}</td>
                    <td style='padding: 8px; border-bottom: 1px solid #e5e7eb;'>{n.CreatedAt:dd/MM/yyyy HH:mm}</td>
                </tr>";
            }));

            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 800px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #2563eb;'>Tóm tắt thông báo {frequencyText}</h2>
                        <p>Xin chào,</p>
                        <p>Bạn có <strong>{notifications.Count} thông báo</strong> chưa đọc trong hệ thống:</p>
                        <div style='background-color: #f8fafc; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <table style='width: 100%; border-collapse: collapse;'>
                                <thead>
                                    <tr style='background-color: #e5e7eb;'>
                                        <th style='padding: 8px; text-align: left; border-bottom: 1px solid #d1d5db;'>#</th>
                                        <th style='padding: 8px; text-align: left; border-bottom: 1px solid #d1d5db;'>Tiêu đề</th>
                                        <th style='padding: 8px; text-align: left; border-bottom: 1px solid #d1d5db;'>Loại</th>
                                        <th style='padding: 8px; text-align: left; border-bottom: 1px solid #d1d5db;'>Thời gian</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {notificationList}
                                </tbody>
                            </table>
                            {(notifications.Count > 20 ? $"<p style='font-size: 12px; color: #666; margin-top: 10px;'>... và {notifications.Count - 20} thông báo khác</p>" : "")}
                        </div>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_baseUrl}/Notifications/Index' 
                               style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                Xem tất cả thông báo
                            </a>
                        </p>
                        <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                            Đây là email tự động từ hệ thống BEMART. Bạn có thể tắt email digest trong cài đặt thông báo.
                        </p>
                    </div>
                </body>
                </html>";

            return await SendEmailAsync(userEmail, subject, body);
        }
    }
}



