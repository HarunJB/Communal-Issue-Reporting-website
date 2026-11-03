using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using product_website.Data;
using product_website.Services;

var builder = WebApplication.CreateBuilder(args);

// Map snake_case columns (password_hash) to PascalCase properties (PasswordHash)
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddControllersWithViews();

// ---------- Database ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing in appsettings.json.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddSingleton<TokenService>();

// ---------- Authentication ----------
// Website: cookie. Mobile app (/api/...): JWT bearer token.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("'Jwt:Key' is missing in appsettings.json (see appsettings-jwt-snippet.json).");

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.MapInboundClaims = false; // keep claim names as written: sub, name, email, role
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

// Development only: lets the Flutter app running in Chrome call the API from another origin.
builder.Services.AddCors(options =>
    options.AddPolicy("DevApi", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    // Not used in Development so the emulator can reach the plain http:// address.
    app.UseHttpsRedirection();
}

// ---------- Demo accounts (development only) ----------
if (app.Environment.IsDevelopment())
{
    try
    {
        await DbSeeder.SeedAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Could not seed demo accounts. Check the connection string in appsettings.json " +
            "and make sure schema.sql has been run on the database.");
    }
}

app.UseStaticFiles();

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevApi");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

