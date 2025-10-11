using System.ComponentModel.DataAnnotations;

namespace MNBEMART.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Tài khoản không được để trống")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
