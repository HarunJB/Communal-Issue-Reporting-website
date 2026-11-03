using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using product_website.Models;

namespace product_website.Services;

/// <summary>Creates the signed login tokens (JWT) that the mobile app sends with each request.</summary>
public class TokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config) => _config = config;

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var key = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("'Jwt:Key' is missing in appsettings.json.");
        var minutes = _config.GetValue("Jwt:ExpiresMinutes", 10080); // default 7 days
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            Expires = expires,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.DisplayName ?? user.FirstName),
                new Claim("email", user.Email),
                new Claim("role", user.Role)
            }),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
