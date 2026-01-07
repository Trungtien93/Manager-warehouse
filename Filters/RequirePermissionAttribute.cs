using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using MNBEMART.Services;

namespace MNBEMART.Filters
{
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string module, string actionKey)
            : base(typeof(RequirePermissionFilter))
        {
            Arguments = new object[] { module, actionKey };
        }

        private class RequirePermissionFilter : IAsyncActionFilter
        {
            private readonly string _module;
            private readonly string _actionKey;
            private readonly IPermissionService _perm;

            public RequirePermissionFilter(string module, string actionKey, IPermissionService perm)
            {
                _module = module; _actionKey = actionKey; _perm = perm;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var httpContext = context.HttpContext;
                var request = httpContext.Request;
                var isAjaxRequest = request.Headers["X-Requested-With"] == "XMLHttpRequest";
                
                var user = httpContext.User;
                var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(idStr, out var uid))
                {
                    if (isAjaxRequest)
                    {
                        context.Result = new JsonResult(new { success = false, message = "Vui lòng đăng nhập" })
                        {
                            StatusCode = 401
                        };
                    }
                    else
                    {
                        context.Result = new RedirectToActionResult("Login", "Account", null);
                    }
                    return;
                }

                // Nếu chưa cấu hình permission thì cho phép (tránh khoá ngoài ý muốn)
                var ok = await _perm.HasAsync(uid, _module, _actionKey);
                if (!ok)
                {
                    if (isAjaxRequest)
                    {
                        context.Result = new JsonResult(new { success = false, message = "Không có quyền thực hiện thao tác này" })
                        {
                            StatusCode = 403
                        };
                    }
                    else
                    {
                        context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
                    }
                    return;
                }

                await next();
            }
        }
    }
}