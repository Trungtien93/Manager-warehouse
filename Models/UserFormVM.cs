namespace MNBEMART.ViewModels;

public class UserFormVM
{
    public int? Id { get; set; }                // null = create
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }       // Create/Reset mới dùng
    public string Role { get; set; } = "User";  // "Admin" | "User"
    public bool IsActive { get; set; } = true;

    public List<int> SelectedWarehouseIds { get; set; } = new();
}
