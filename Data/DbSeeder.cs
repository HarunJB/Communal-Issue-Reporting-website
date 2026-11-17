using Dapper;
using Npgsql;

namespace product_website.Data;

/// <summary>
/// Development only. Creates the departments and report categories (if there are none) and one demo
/// account per role so you can test each role. Change the demo password here, and never keep demo
/// accounts in a deployed database.
/// </summary>
public static class DbSeeder
{
    private const string DemoPassword = "Demo12345!";

    private sealed record DepartmentSeed(string En, string Bs);

    private sealed record CategorySeed(string En, string Bs, string Icon, string DepartmentEn, string Priority);

    // Invented, realistic departments. Rename or edit them freely.
    private static readonly DepartmentSeed[] Departments =
    {
        new("Roads and Traffic", "Putevi i saobraćaj"),
        new("Public Lighting", "Javna rasvjeta"),
        new("Water and Sewage Company", "Vodovod i Kanalizacija"),
        new("Sanitation and Waste", "Čistoća i otpad"),
        new("Parks and Greenery", "Parkovi i zelenilo"),
        new("General Municipal Services", "Opće komunalne službe"),
    };

    // "Icon" is a key the Flutter app maps to an icon (see widgets/category_icon.dart).
    private static readonly CategorySeed[] Categories =
    {
        new("Pothole", "Rupa na cesti", "pothole", "Roads and Traffic", "normal"),
        new("Damaged sidewalk", "Oštećen trotoar", "sidewalk", "Roads and Traffic", "normal"),
        new("Broken traffic light", "Neispravan semafor", "traffic_light", "Roads and Traffic", "high"),
        new("Damaged road sign", "Oštećen saobraćajni znak", "road_sign", "Roads and Traffic", "normal"),
        new("Broken street light", "Neispravna ulična rasvjeta", "street_light", "Public Lighting", "normal"),
        new("Water leak", "Curenje vode", "water_leak", "Water and Sewage Company", "high"),
        new("Open or damaged manhole", "Otvoren ili oštećen šaht", "manhole", "Water and Sewage Company", "high"),
        new("Clogged drain", "Začepljen odvod", "drain", "Water and Sewage Company", "normal"),
        new("Illegal dumping", "Ilegalno odlaganje otpada", "dumping", "Sanitation and Waste", "normal"),
        new("Overflowing container", "Prepun kontejner", "container", "Sanitation and Waste", "normal"),
        new("Spilled trash", "Rasuti otpad", "trash", "Sanitation and Waste", "low"),
        new("Fallen tree or branch", "Srušeno drvo ili grana", "tree", "Parks and Greenery", "high"),
        new("Damaged bench or playground", "Oštećena klupa ili igralište", "bench", "Parks and Greenery", "low"),
        new("Other issue", "Ostalo", "other", "General Municipal Services", "low"),
    };

    /// <summary>
    /// Creates the departments and report categories if they are missing. Safe to run on every start.
    /// Used both by the development seeder and by the production start-up (no demo accounts, no demo reports).
    /// </summary>
    public static async Task<Dictionary<string, int>> EnsureReferenceDataAsync(NpgsqlConnection conn)
    {
        var departmentIds = new Dictionary<string, int>();
        foreach (var department in Departments)
            departmentIds[department.En] = await EnsureDepartmentAsync(conn, department);

        var hasCategories = await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM categories)");
        if (!hasCategories)
        {
            foreach (var category in Categories)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO categories (name_bs, name_en, icon, department_id, default_priority)
                    VALUES (@Bs, @En, @Icon, @departmentId, CAST(@Priority AS priority_level))",
                    new
                    {
                        category.Bs,
                        category.En,
                        category.Icon,
                        departmentId = departmentIds[category.DepartmentEn],
                        category.Priority
                    });
            }
        }

        return departmentIds;
    }

    public static async Task SeedAsync(NpgsqlDataSource db)
    {
        await using var conn = await db.OpenConnectionAsync();

        var departmentIds = await EnsureReferenceDataAsync(conn);

        // An operator must belong to a department (see CHECK constraint in schema.sql).
        var operatorDepartmentId = departmentIds["Water and Sewage Company"];

        await EnsureUserAsync(conn, "citizen@demo.ba", "Demo", "Citizen", "citizen", null);
        await EnsureUserAsync(conn, "operator@demo.ba", "Demo", "Operator", "operator", operatorDepartmentId);
        await EnsureUserAsync(conn, "admin@demo.ba", "Demo", "Admin", "admin", null);

        if (SeedDemoReports)
            await SeedDemoReportsAsync(conn, operatorDepartmentId);
    }

    // =====================================================================
    // Demo reports, so the staff dashboard has something to show.
    // They are marked with source = 'web' (real reports from the app are 'mobile').
    // To remove them later:  DELETE FROM reports WHERE source = 'web';
    // To stop creating them: set SeedDemoReports to false.
    // =====================================================================
    private const bool SeedDemoReports = true;

    private sealed class CategoryInfo
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string Priority { get; set; } = "normal";
    }

    private static readonly (string Area, double Lat, double Lng, double Spread, string[] Streets)[] Areas =
    {
        ("Centar", 43.8563, 18.4131, 0.006, new[] { "Ferhadija", "Zmaja od Bosne", "Maršala Tita", "Mula Mustafe Bašeskije", "Branilaca Sarajeva" }),
        ("Stari Grad", 43.8590, 18.4310, 0.006, new[] { "Bistrik", "Kovači", "Vratnik", "Mejtaš", "Alifakovac" }),
        ("Novo Sarajevo", 43.8505, 18.3850, 0.006, new[] { "Zmaja od Bosne bb", "Hamdije Čemerlića", "Džemala Bijedića", "Vilsonovo šetalište", "Fra Anđela Zvizdovića" }),
        ("Novi Grad", 43.8570, 18.3600, 0.008, new[] { "Safeta Zajke", "Aleja Bosne Srebrene", "Dobrinja", "Alipašina", "Hrasnička cesta" }),
        ("Ilidža", 43.8280, 18.3100, 0.008, new[] { "Butmirska cesta", "Put života", "Stup", "Hrasnica", "Ilidža centar" }),
        ("Vogošća", 43.9010, 18.3800, 0.008, new[] { "Semizovac", "Svrake", "Vogošća centar", "Kobilja Glava", "Bačići" }),
        ("Hadžići", 43.8200, 18.2000, 0.010, new[] { "Hadžići centar", "Tarčin", "Pazarić", "Binježevo", "Lukavica" }),
    };

    private static readonly Dictionary<string, string[]> Descriptions = new()
    {
        ["pothole"] = new[] { "Deep pothole in the right lane, cars swerve around it.", "Large hole after the winter, dangerous for bikes.", "Asphalt has collapsed near the crossing." },
        ["sidewalk"] = new[] { "Broken paving stones, hard for strollers and wheelchairs.", "Sidewalk is sunken and fills with water when it rains." },
        ["traffic_light"] = new[] { "Traffic light stays red in one direction.", "Pedestrian signal not working at the crossing." },
        ["road_sign"] = new[] { "Stop sign is bent and facing the wrong way.", "Road sign fell down and lies on the pavement." },
        ["street_light"] = new[] { "Street light has been out for over a week.", "Whole street is dark in the evening, three lamps are out." },
        ["water_leak"] = new[] { "Water is running out of the road all day.", "Leaking pipe near the building, the street is flooded." },
        ["manhole"] = new[] { "Manhole cover is missing, very dangerous.", "Cover is loose and rattles when cars pass." },
        ["drain"] = new[] { "Drain is clogged and the street floods when it rains.", "Storm drain full of leaves and rubbish." },
        ["dumping"] = new[] { "Someone dumped furniture and building waste.", "Pile of rubbish bags next to the river." },
        ["container"] = new[] { "Containers are overflowing, trash all over the pavement.", "Bins not emptied for several days." },
        ["trash"] = new[] { "Rubbish scattered around the playground.", "Spilled waste near the bus stop." },
        ["tree"] = new[] { "Large branch fell on the footpath.", "Tree is leaning over the road after the storm." },
        ["bench"] = new[] { "Broken bench in the park, sharp edges.", "Swing in the playground is damaged." },
        ["other"] = new[] { "Problem that does not fit the other categories.", "Please have a look at this spot." },
    };

    private static readonly string[] RejectionReasons =
    {
        "Duplicate of an existing report.",
        "Not a municipal responsibility.",
        "Could not find the problem at this location."
    };

    private static readonly string[] PublicReplies =
    {
        "Thank you for the report. A crew has been informed.",
        "We have scheduled this for the coming days.",
        "The work is finished. Thank you for helping us."
    };

    private static async Task SeedDemoReportsAsync(NpgsqlConnection conn, int operatorDepartmentId)
    {
        if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM reports WHERE source = 'web')"))
            return;

        var citizenId = await conn.ExecuteScalarAsync<int>("SELECT id FROM users WHERE email = 'citizen@demo.ba'");
        var operatorId = await conn.ExecuteScalarAsync<int>("SELECT id FROM users WHERE email = 'operator@demo.ba'");
        var adminId = await conn.ExecuteScalarAsync<int>("SELECT id FROM users WHERE email = 'admin@demo.ba'");

        var categories = (await conn.QueryAsync<CategoryInfo>(@"
            SELECT id, department_id, COALESCE(icon, 'other') AS icon, default_priority::text AS priority
            FROM categories WHERE is_active ORDER BY id")).ToList();
        if (categories.Count == 0) return;

        var rnd = new Random(2026);
        const int count = 48;

        for (var i = 0; i < count; i++)
        {
            var category = categories[rnd.Next(categories.Count)];
            var area = Areas[rnd.Next(Areas.Length)];
            var lat = area.Lat + (rnd.NextDouble() * 2 - 1) * area.Spread;
            var lng = area.Lng + (rnd.NextDouble() * 2 - 1) * area.Spread * 1.4;
            var address = $"{area.Streets[rnd.Next(area.Streets.Length)]} {rnd.Next(1, 90)}, {area.Area}";

            var ageDays = rnd.NextDouble() * 27 + 0.1;
            var created = DateTime.UtcNow.AddDays(-ageDays);

            // Older reports are more likely to be finished.
            var roll = rnd.NextDouble() + (ageDays / 27.0) * 0.45;
            string status = roll switch
            {
                < 0.30 => "submitted",
                < 0.50 => "in_review",
                < 0.68 => "assigned",
                < 0.90 => "in_progress",
                < 1.25 => "resolved",
                _ => "rejected"
            };
            if (rnd.NextDouble() < 0.07) status = "rejected";

            var priority = category.Priority;
            if (rnd.NextDouble() < 0.12) priority = "high";

            var descriptions = Descriptions.GetValueOrDefault(category.Icon, Descriptions["other"]);
            var description = rnd.NextDouble() < 0.85 ? descriptions[rnd.Next(descriptions.Length)] : null;

            var assignee = category.DepartmentId == operatorDepartmentId ? operatorId : adminId;
            var chain = new List<string> { "submitted" };
            if (status != "submitted") chain.Add("in_review");
            if (status is "assigned" or "in_progress" or "resolved") chain.Add("assigned");
            if (status is "in_progress" or "resolved") chain.Add("in_progress");
            if (status is "resolved" or "rejected") chain.Add(status);
            chain = chain.Distinct().ToList();

            var times = new List<DateTime> { created };
            var remaining = Math.Max(0.1, ageDays - 0.02);
            for (var s = 1; s < chain.Count; s++)
                times.Add(times[s - 1].AddDays(Math.Min(remaining / chain.Count, rnd.NextDouble() * 3 + 0.05)));

            var last = times[^1];
            var resolvedAt = status == "resolved" ? (DateTime?)last : null;
            var rejection = status == "rejected" ? RejectionReasons[rnd.Next(RejectionReasons.Length)] : null;
            int? assigned = chain.Contains("assigned") ? assignee : null;

            var reportId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO reports (user_id, category_id, department_id, assigned_to, description,
                                     latitude, longitude, address, status, priority, is_anonymous, source,
                                     rejection_reason, created_at, updated_at, resolved_at)
                VALUES (@citizenId, @categoryId, @departmentId, @assigned, @description,
                        @lat, @lng, @address, CAST(@status AS report_status), CAST(@priority AS priority_level),
                        @anonymous, 'web', @rejection, @created, @last, @resolvedAt)
                RETURNING id",
                new
                {
                    citizenId,
                    categoryId = category.Id,
                    departmentId = category.DepartmentId,
                    assigned,
                    description,
                    lat = Math.Round(lat, 6),
                    lng = Math.Round(lng, 6),
                    address,
                    status,
                    priority,
                    anonymous = rnd.NextDouble() < 0.15,
                    rejection,
                    created,
                    last,
                    resolvedAt
                });

            for (var s = 0; s < chain.Count; s++)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO status_history (report_id, old_status, new_status, changed_by, note, created_at)
                    VALUES (@reportId, CAST(@prev AS report_status), CAST(@next AS report_status), @by, @note, @at)",
                    new
                    {
                        reportId,
                        prev = s == 0 ? null : chain[s - 1],
                        next = chain[s],
                        by = s == 0 ? citizenId : assignee,
                        note = chain[s] == "rejected" ? rejection : null,
                        at = times[s]
                    });
            }

            if ((status is "in_progress" or "resolved") && rnd.NextDouble() < 0.6)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO comments (report_id, user_id, body, is_internal, created_at)
                    VALUES (@reportId, @assignee, @body, FALSE, @at)",
                    new { reportId, assignee, body = PublicReplies[status == "resolved" ? 2 : rnd.Next(2)], at = last });
            }

            // A few reports are confirmed by other people ("I see this problem too").
            if (rnd.NextDouble() < 0.5)
            {
                foreach (var supporter in new[] { operatorId, adminId }.Take(rnd.Next(1, 3)))
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO report_supports (report_id, user_id, created_at)
                        VALUES (@reportId, @supporter, @at) ON CONFLICT DO NOTHING",
                        new { reportId, supporter, at = created.AddHours(rnd.Next(1, 48)) });
                }
            }
        }
    }

    private static async Task<int> EnsureDepartmentAsync(NpgsqlConnection conn, DepartmentSeed department)
    {
        var id = await conn.ExecuteScalarAsync<int?>(
            "SELECT id FROM departments WHERE name_en = @En", new { department.En });
        if (id is not null) return id.Value;

        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO departments (name_bs, name_en, contact_email)
            VALUES (@Bs, @En, 'info@example.ba') RETURNING id",
            new { department.Bs, department.En });
    }

    private static async Task EnsureUserAsync(
        NpgsqlConnection conn, string email, string firstName, string lastName, string role, int? departmentId)
    {
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @email)", new { email });
        if (exists) return;

        await conn.ExecuteAsync(@"
            INSERT INTO users (email, password_hash, first_name, last_name, display_name, role, department_id)
            VALUES (@email, @hash, @firstName, @lastName, @firstName, CAST(@role AS user_role), @departmentId)",
            new
            {
                email,
                hash = BCrypt.Net.BCrypt.HashPassword(DemoPassword),
                firstName,
                lastName,
                role,
                departmentId
            });
    }
}
