using System.Globalization;

namespace product_website.Services;

/// <summary>
/// Saves report photos on disk (folder "uploads" next to the project, or "Uploads:Path" from the settings)
/// and keeps only the web path (/uploads/...) in the database.
/// </summary>
public class PhotoStorage
{
    public const long MaxBytes = 8 * 1024 * 1024; // per photo
    public const int MaxPhotos = 3;

    public string RootPath { get; }

    public PhotoStorage(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["Uploads:Path"];
        RootPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "uploads")
            : configured;

        Directory.CreateDirectory(RootPath);
    }

    /// <summary>
    /// Looks at the first bytes of the file (not its name or declared type) and returns
    /// ".jpg", ".png" or ".webp", or null if it is not one of those images.
    /// </summary>
    public static async Task<string?> DetectExtensionAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken: ct);

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ".jpg";

        if (read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return ".png";

        if (read >= 12
            && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
            return ".webp";

        return null;
    }

    /// <summary>Saves the file under a random name and returns its web path, e.g. /uploads/reports/2026/11/ab12.jpg</summary>
    public async Task<string> SaveAsync(IFormFile file, string extension, CancellationToken ct, string area = "reports")
    {
        var now = DateTime.UtcNow;
        var year = now.ToString("yyyy", CultureInfo.InvariantCulture);
        var month = now.ToString("MM", CultureInfo.InvariantCulture);

        var folder = Path.Combine(RootPath, area, year, month);
        Directory.CreateDirectory(folder);

        var name = $"{Guid.NewGuid():N}{extension}";
        await using (var target = File.Create(Path.Combine(folder, name)))
        {
            await file.CopyToAsync(target, ct);
        }

        return $"/uploads/{area}/{year}/{month}/{name}";
    }

    /// <summary>Removes a file saved by <see cref="SaveAsync"/> (used if the database insert fails).</summary>
    public void Delete(string webPath)
    {
        try
        {
            const string prefix = "/uploads/";
            if (!webPath.StartsWith(prefix, StringComparison.Ordinal)) return;

            var relative = webPath[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(RootPath, relative));

            // Never delete outside the uploads folder.
            if (!full.StartsWith(Path.GetFullPath(RootPath), StringComparison.Ordinal)) return;

            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException)
        {
            // best effort only
        }
    }
}
