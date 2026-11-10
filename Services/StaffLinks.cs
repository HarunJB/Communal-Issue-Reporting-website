using Microsoft.AspNetCore.WebUtilities;
using product_website.Models;

namespace product_website.Services;

/// <summary>Builds links to the Issues list that keep the current filters (only non-default values are written).</summary>
public static class StaffLinks
{
    public static string Issues(
        string path, ReportFilter f, string? status = null, int? page = null, bool clearPage = false)
    {
        var query = new Dictionary<string, string?>();

        var effectiveStatus = status ?? f.Status;
        if (!string.IsNullOrWhiteSpace(effectiveStatus) && effectiveStatus != "open")
            query["status"] = effectiveStatus;

        if (!string.IsNullOrWhiteSpace(f.Q)) query["q"] = f.Q;
        if (f.CategoryId.HasValue) query["categoryId"] = f.CategoryId.Value.ToString();
        if (f.DepartmentId.HasValue) query["departmentId"] = f.DepartmentId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(f.Priority)) query["priority"] = f.Priority;
        if (!string.IsNullOrWhiteSpace(f.Assigned)) query["assigned"] = f.Assigned;
        if (f.From.HasValue) query["from"] = f.From.Value.ToString("yyyy-MM-dd");
        if (f.To.HasValue) query["to"] = f.To.Value.ToString("yyyy-MM-dd");
        if (f.Archived) query["archived"] = "true";
        if (!string.IsNullOrWhiteSpace(f.Sort) && f.Sort != "oldest") query["sort"] = f.Sort;

        var p = clearPage ? 1 : (page ?? f.Page);
        if (p > 1) query["page"] = p.ToString();

        return QueryHelpers.AddQueryString(path, query);
    }
}
