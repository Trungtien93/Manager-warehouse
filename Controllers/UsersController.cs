using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;

namespace MNBEMART.Controllers
{
    [Authorize(Roles = "admin")]
    public class UsersController : Controller
    {
        private readonly AppDbContext _db;
        public UsersController(AppDbContext db) => _db = db;

        // LIST
        public async Task<IActionResult> Index()
        {
            var users = await _db.Users
                .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                .OrderBy(u => u.Username)
                .ToListAsync();
            return View(users);
        }

        // CREATE (GET)
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await BindWarehousesAsync();
            ViewBag.Roles = RoleSelectList();
            return View(new UserFormVM());
        }

        // CREATE (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserFormVM vm)
        {
            await BindWarehousesAsync(vm.SelectedWarehouseIds);
            ViewBag.Roles = RoleSelectList(vm.Role);

            if (!ModelState.IsValid)
                return View(vm);

            if (string.IsNullOrWhiteSpace(vm.Username) || string.IsNullOrWhiteSpace(vm.Password))
            {
                ModelState.AddModelError("", "Vui lòng nhập Username và Password.");
                return View(vm);
            }

            if (await _db.Users.AnyAsync(x => x.Username == vm.Username))
            {
                ModelState.AddModelError(nameof(vm.Username), "Username đã tồn tại.");
                return View(vm);
            }

            var user = new User
            {
                FullName = vm.FullName?.Trim(),
                Username = vm.Username.Trim(),
                PasswordHash = vm.Password, // TODO: hash bằng BCrypt
                Role = (vm.Role?.Trim()?.ToLower() == "admin") ? "admin" : "user",
            };

            user.UserWarehouses = vm.SelectedWarehouseIds
                .Distinct()
                .Select(wid => new UserWarehouse { WarehouseId = wid })
                .ToList();

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // EDIT (GET)
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _db.Users
                .Include(u => u.UserWarehouses)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var vm = new UserFormVM
            {
                Id = user.Id,
                FullName = user.FullName ?? "",
                Username = user.Username,
                Role = user.Role ?? "User",
                IsActive = true, // nếu bạn có cột IsActive thì map vào
                SelectedWarehouseIds = user.UserWarehouses.Select(uw => uw.WarehouseId).ToList()
            };

            await BindWarehousesAsync(vm.SelectedWarehouseIds);
            ViewBag.Roles = RoleSelectList(vm.Role);

            return View(vm);
        }

        // EDIT (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserFormVM vm)
        {
            await BindWarehousesAsync(vm.SelectedWarehouseIds);
            ViewBag.Roles = RoleSelectList(vm.Role);

            if (!ModelState.IsValid) return View(vm);

            var user = await _db.Users
                .Include(u => u.UserWarehouses)
                .FirstOrDefaultAsync(u => u.Id == vm.Id);
            if (user == null) return NotFound();

            // cập nhật thông tin
            user.FullName = vm.FullName?.Trim();
            user.Username = vm.Username.Trim();

            // đổi role
            user.Role = (vm.Role?.Trim()?.ToLower() == "admin") ? "Admin" : "User";

            // nếu có nhập Password mới thì đổi
            if (!string.IsNullOrWhiteSpace(vm.Password))
                user.PasswordHash = vm.Password; // TODO: hash

            // cập nhật mapping kho
            var incoming = vm.SelectedWarehouseIds.Distinct().ToHashSet();
            var current  = user.UserWarehouses.Select(x => x.WarehouseId).ToHashSet();

            // remove những kho không còn chọn
           // user.UserWarehouses.RemoveAll(x => !incoming.Contains(x.WarehouseId));

            // add kho mới
            foreach (var wid in incoming.Where(w => !current.Contains(w)))
                user.UserWarehouses.Add(new UserWarehouse { WarehouseId = wid, UserId = user.Id });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // RESET PASSWORD (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(int id, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["Msg"] = "Mật khẩu mới không được trống.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.PasswordHash = newPassword; // TODO: hash
            await _db.SaveChangesAsync();

            TempData["Msg"] = $"Đã đặt lại mật khẩu cho {user.Username}.";
            return RedirectToAction(nameof(Index));
        }

        // DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "Đã xoá người dùng.";
            return RedirectToAction(nameof(Index));
        }

        // helper: bind warehouses
        private async Task BindWarehousesAsync(IEnumerable<int>? selected = null)
        {
            var items = await _db.Warehouses.AsNoTracking()
                .Select(w => new { w.Id, Name = w.Name + " - " + w.Address })
                .ToListAsync();

            ViewBag.WarehouseList = new MultiSelectList(items, "Id", "Name", selected);
        }

        // helper: role select list
        private SelectList RoleSelectList(string? selected = null)
        {
            var roles = new[]
            {
                new { Value = "User",  Text = "User"  },
                new { Value = "Admin", Text = "Admin" }
            };
            return new SelectList(roles, "Value", "Text", selected ?? "User");
        }
    }
}
