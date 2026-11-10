using System.Globalization;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using product_website.Data;
using product_website.Services;

// Numbers must always use "." (the app sends 43.8563), whatever language the PC is set to.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Map snake_case columns (password_hash) to PascalCase properties (PasswordHash)
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

// Used by GeoController to turn map coordinates into a street address (OpenStreetMap Nominatim).
builder.Services.AddHttpClient("nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SarajevoCivicReports/1.0 (senior design project)");
    client.Timeout = TimeSpan.FromSeconds(8);
});

// ---------- Database ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing (set it in user secrets).");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<CategoryRepository>();
builder.Services.AddScoped<ReportRepository>();
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<StaffReportRepository>();
builder.Services.AddScoped<DashboardRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PhotoStorage>();

// ---------- Authentication ----------
// Website: cookie. Mobile app (/api/...): JWT bearer token.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("'Jwt:Key' is missing (set it in user secrets).");

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

        // On every website request: the account must still exist, be active and keep the same role.
        // Suspending a user or changing a role signs them out of the dashboard right away.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var cookieRole = context.Principal?.FindFirstValue(ClaimTypes.Role);

                var valid = false;
                if (int.TryParse(idText, out var id))
                {
                    var users = context.HttpContext.RequestServices.GetRequiredService<UserRepository>();
                    var user = await users.GetByIdAsync(id);
                    valid = user is not null && user.Status == "active" && user.Role == cookieRole;
                }

                if (!valid)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
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

        // On every request: the account must still exist, be active, and still have the same role.
        // This makes suspending a user, deleting an account or changing a role take effect immediately.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var idText = context.Principal?.FindFirstValue("sub");
                var tokenRole = context.Principal?.FindFirstValue("role");

                if (!int.TryParse(idText, out var id))
                {
                    context.Fail("Invalid token.");
                    return;
                }

                var users = context.HttpContext.RequestServices.GetRequiredService<UserRepository>();
                var user = await users.GetByIdAsync(id);

                if (user is null || user.Status != "active" || user.Role != tokenRole)
                    context.Fail("The session is no longer valid.");
            }
        };
    });

builder.Services.AddAuthorization();

// Development only: lets the Flutter app running in Chrome call the API (and load photos) from another origin.
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

// ---------- Demo data (development only) ----------
if (app.Environment.IsDevelopment())
{
    try
    {
        await DbSeeder.SeedAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Could not seed demo data. Check the connection string " +
            "and make sure schema.sql has been run on the database.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevApi"); // before static files so uploaded photos also get the CORS headers
}

// Uploaded report photos are served from /uploads/...
var photoStorage = app.Services.GetRequiredService<PhotoStorage>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(photoStorage.RootPath),
    RequestPath = "/uploads"
});
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
