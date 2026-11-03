namespace product_website.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }

    public string Role { get; set; } = "citizen";

    public string Status { get; set; } = "active";

    public int? DepartmentId { get; set; }
    public string Language { get; set; } = "bs";
}
