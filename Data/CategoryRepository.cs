using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

public class CategoryRepository
{
    private readonly NpgsqlDataSource _db;

    public CategoryRepository(NpgsqlDataSource db) => _db = db;

    private const string Select = @"
        SELECT c.id, c.name_en, c.name_bs, c.icon, c.department_id,
               d.name_en AS department_name_en, d.name_bs AS department_name_bs,
               c.default_priority::text AS default_priority
        FROM categories c
        JOIN departments d ON d.id = c.department_id
        WHERE c.is_active AND d.is_active";

    public async Task<List<CategoryDto>> GetActiveAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<CategoryDto>($"{Select} ORDER BY c.id");
        return rows.ToList();
    }

    public async Task<CategoryDto?> GetActiveByIdAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<CategoryDto>($"{Select} AND c.id = @id", new { id });
    }
}
