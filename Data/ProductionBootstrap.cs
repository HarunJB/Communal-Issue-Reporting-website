using Dapper;
using Npgsql;

namespace product_website.Data;

/// <summary>
/// Runs on every start of the LIVE site (never in Development).
///
///  1. Makes sure the departments and report categories exist (the app is useless without them).
///  2. Creates the first accounts from settings, only if that email does not exist yet:
///        Bootstrap:Admin:Email     Bootstrap:Admin:Password     (optional: FirstName, LastName)
///        Bootstrap:Operator:Email  Bootstrap:Operator:Password  Bootstrap:Operator:Department  (English department name)
///        Bootstrap:Citizen:Email   Bootstrap:Citizen:Password
///     On Azure these are App Settings written like  Bootstrap__Admin__Email.
///
/// An existing account is never changed, so changing a password later has to be done in the website.
/// There are no demo accounts and no demo reports on the live site.
/// </summary>
public static class ProductionBootstrap
{
    private const int MinPasswordLength = 12;

    public static async Task RunAsync(NpgsqlDataSource db, IConfiguration config, ILogger logger)
    {
        await using var conn = await db.OpenConnectionAsync();

        var departmentIds = await DbSeeder.EnsureReferenceDataAsync(conn);

        await EnsureAccountAsync(conn, config, logger, "Admin", "admin", null);

        int? operatorDepartment = null;
        var departmentName = config["Bootstrap:Operator:Department"];
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            if (departmentIds.TryGetValue(departmentName.Trim(), out var id))
                operatorDepartment = id;
            else
                logger.LogError("Bootstrap:Operator:Department '{Name}' is not one of: {Names}",
                    departmentName, string.Join(", ", departmentIds.Keys));
        }
        await EnsureAccountAsync(conn, config, logger, "Operator", "operator", operatorDepartment);

        await EnsureAccountAsync(conn, config, logger, "Citizen", "citizen", null);
    }

    private static async Task EnsureAccountAsync(
        NpgsqlConnection conn, IConfiguration config, ILogger logger,
        string section, string role, int? departmentId)
    {
        var email = config[$"Bootstrap:{section}:Email"]?.Trim().ToLowerInvariant();
        var password = config[$"Bootstrap:{section}:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return; // not configured, nothing to do

        if (password.Length < MinPasswordLength)
        {
            logger.LogError("Bootstrap:{Section}:Password must be at least {Min} characters. Account not created.",
                section, MinPasswordLength);
            return;
        }

        if (role == "operator" && departmentId is null)
        {
            logger.LogError("Operator account not created: set Bootstrap:Operator:Department to a valid department name.");
            return;
        }

        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @email)", new { email });
        if (exists)
        {
            logger.LogInformation("Bootstrap {Section} account already exists, left unchanged.", section);
            return;
        }

        var firstName = config[$"Bootstrap:{section}:FirstName"];
        var lastName = config[$"Bootstrap:{section}:LastName"];
        if (string.IsNullOrWhiteSpace(firstName)) firstName = section;
        if (string.IsNullOrWhiteSpace(lastName)) lastName = "Account";

        await conn.ExecuteAsync(@"
            INSERT INTO users (email, password_hash, first_name, last_name, display_name, role, department_id)
            VALUES (@email, @hash, @firstName, @lastName, @firstName, CAST(@role AS user_role), @departmentId)",
            new
            {
                email,
                hash = BCrypt.Net.BCrypt.HashPassword(password),
                firstName = firstName.Trim(),
                lastName = lastName.Trim(),
                role,
                departmentId
            });

        logger.LogInformation("Bootstrap {Section} account created for {Email}.", section, email);
    }
}
