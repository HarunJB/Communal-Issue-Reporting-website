using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace product_website.Controllers;

/// <summary>
/// Base class for every /api endpoint that needs a logged-in user. The app sends its token in the
/// "Authorization: Bearer ..." header, so these controllers use the JWT scheme (the website uses cookies).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class AuthedApiController : ControllerBase
{
    protected int UserId => int.TryParse(User.FindFirstValue("sub"), out var id) ? id : 0;

    protected string UserRole => User.FindFirstValue("role") ?? "citizen";

    protected bool IsStaff => UserRole is "operator" or "admin";
}
