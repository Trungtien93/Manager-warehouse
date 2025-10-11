
using System.ComponentModel.DataAnnotations;

namespace MNBEMART.Models.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Tài khoản không được để trống")]
        public string FullName { get; set; }
        
        [Required(ErrorMessage = "Tài khoản không được để trống")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Tài khoản không được để trống")]
        [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Tài khoản chỉ được chứa chữ cái và số")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống")]
        [Compare("Password")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; }
    }
}
