using Microsoft.Data.Sqlite;

namespace ShiftClub.Licensing;

public sealed record ClubRow(
    string Id,
    string Name,
    string? City,
    string? ContactName,
    string? Phone,
    string? Telegram,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record LicenseEventRow(
    string Id,
    string? ClubId,
    string? LicenseId,
    string Action,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record ClubOverview(
    ClubRow Club,
    LicenseRow? License,
    string Status,
    int DaysRemaining);

public sealed partial class LicenseStore
{
    public static string StatusOf(LicenseRow? row)
    {
        if (row is null)
            return "none";
        if (row.Revoked)
            return "revoked";
        if (LicenseTokenExpired(row))
            return "expired";
        if (string.IsNullOrEmpty(row.MachineId))
            return "waiting";
        if (ShiftClub.Shared.Licensing.LicenseToken.IsWarning(row.ExpiresAt))
            return "expiring";
        if (row.Kind == "trial")
            return "trial";
        return "active";
    }

    public static int DaysRemaining(DateTimeOffset expiresAt)
    {
        var left = expiresAt - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
            return 0;
        return (int)Math.Ceiling(left.TotalDays);
    }

    public async Task<IReadOnlyList<ClubOverview>> ListClubsAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var clubs = ReadClubs();
            var licenses = ReadLicenses();
            var list = clubs.Select(club =>
            {
                var current = CurrentLicense(licenses.Where(l => l.ClubId == club.Id));
                return new ClubOverview(
                    club,
                    current,
                    StatusOf(current),
                    current is null ? 0 : DaysRemaining(current.ExpiresAt));
            }).OrderBy(Rank).ThenBy(item => item.License?.ExpiresAt ?? DateTimeOffset.MaxValue).ToList();
            return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<(ClubRow? Club, LicenseRow? Current, IReadOnlyList<LicenseRow> Licenses, IReadOnlyList<LicenseEventRow> Events)> GetClubAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var club = FindClub(id);
            if (club is null)
                return (null, null, Array.Empty<LicenseRow>(), Array.Empty<LicenseEventRow>());
            var licenses = ReadLicenses().Where(l => l.ClubId == id).OrderByDescending(l => l.CreatedAt).ToList();
            return (club, CurrentLicense(licenses), licenses, ReadEvents(id));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<(ClubRow Club, LicenseRow License)> CreateClubAsync(
        string name,
        string? city,
        string? contactName,
        string? phone,
        string? telegram,
        string? note,
        int maxComputers,
        int days,
        string kind,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var club = InsertClub(name, city, contactName, phone, telegram, note, DateTimeOffset.UtcNow);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var key = ShiftClub.Shared.Licensing.LicenseKeyFormat.Create();
                var row = new LicenseRow(
                    Guid.NewGuid().ToString("D"),
                    key,
                    name,
                    maxComputers,
                    kind,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(days),
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    club.Id);
                try
                {
                    Insert(row);
                    AddEvent(club.Id, row.Id, "issued", $"Клуб создан. Ключ {row.Key}, {maxComputers} мест, {days} дн., {(kind == "trial" ? "триал" : "оплата")}.");
                    return (club, row);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && attempt < 4)
                {
                }
            }

            throw new InvalidOperationException("Не удалось выдать уникальный ключ.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ClubRow?> UpdateClubAsync(
        string id,
        string name,
        string? city,
        string? contactName,
        string? phone,
        string? telegram,
        string? note,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (FindClub(id) is null)
                return null;

            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE clubs
                SET name = $name, city = $city, contact_name = $contact, phone = $phone, telegram = $telegram, note = $note
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$city", (object?)city ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$contact", (object?)contactName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$phone", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$telegram", (object?)telegram ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();

            using var rename = _db.CreateCommand();
            rename.CommandText = "UPDATE licenses SET club_name = $name WHERE club_id = $id AND revoked = 0";
            rename.Parameters.AddWithValue("$name", name);
            rename.Parameters.AddWithValue("$id", id);
            rename.ExecuteNonQuery();
            AddEvent(id, null, "edited", "Карточка клуба обновлена.");
            return FindClub(id);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> SetSeatsAsync(string licenseId, int seats, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var current = FindById(licenseId);
            if (current is null || current.Revoked)
                return null;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE licenses SET max_computers = $seats WHERE id = $id";
            cmd.Parameters.AddWithValue("$seats", seats);
            cmd.Parameters.AddWithValue("$id", licenseId);
            cmd.ExecuteNonQuery();
            AddEvent(current.ClubId, licenseId, "seats", $"Мест теперь {seats}. Сервер клуба подхватит лимит в течение 5 минут.");
            return FindById(licenseId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> ReissueAsync(string licenseId, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var current = FindById(licenseId);
            if (current is null || current.Revoked || LicenseTokenExpired(current) || string.IsNullOrEmpty(current.ClubId))
                return null;

            using (var revoke = _db.CreateCommand())
            {
                revoke.CommandText = "UPDATE licenses SET revoked = 1 WHERE id = $id";
                revoke.Parameters.AddWithValue("$id", licenseId);
                revoke.ExecuteNonQuery();
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var key = ShiftClub.Shared.Licensing.LicenseKeyFormat.Create();
                var row = new LicenseRow(
                    Guid.NewGuid().ToString("D"),
                    key,
                    current.ClubName,
                    current.MaxComputers,
                    current.Kind,
                    DateTimeOffset.UtcNow,
                    current.ExpiresAt,
                    false,
                    null,
                    null,
                    null,
                    null,
                    current.Note,
                    current.ClubId);
                try
                {
                    Insert(row);
                    AddEvent(current.ClubId, row.Id, "reissued", $"Старый ключ отозван. Новый ключ {row.Key}. Его нужно вставить на сервере клуба.");
                    return row;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && attempt < 4)
                {
                }
            }

            throw new InvalidOperationException("Не удалось выдать уникальный ключ.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void MigrateClubs()
    {
        if (!ColumnExists("licenses", "club_id"))
        {
            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE licenses ADD COLUMN club_id TEXT";
            alter.ExecuteNonQuery();
        }

        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, club_name, created_at, note FROM licenses WHERE club_id IS NULL OR club_id = ''";
        using var reader = cmd.ExecuteReader();
        var pending = new List<(string Id, string Name, DateTimeOffset Created, string? Note)>();
        while (reader.Read())
        {
            pending.Add((
                reader.GetString(0),
                reader.GetString(1),
                Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        reader.Close();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pending)
        {
            if (!byName.TryGetValue(item.Name, out var clubId))
            {
                var club = InsertClub(item.Name, null, null, null, null, item.Note, item.Created);
                clubId = club.Id;
                byName[item.Name] = clubId;
            }

            using var link = _db.CreateCommand();
            link.CommandText = "UPDATE licenses SET club_id = $club WHERE id = $id";
            link.Parameters.AddWithValue("$club", clubId);
            link.Parameters.AddWithValue("$id", item.Id);
            link.ExecuteNonQuery();
            AddEvent(clubId, item.Id, "imported", $"Ключ перенесён в карточку клуба {item.Name}.");
        }
    }

    private string EnsureClubByName(string name, string? note)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id FROM clubs WHERE name = $name COLLATE NOCASE ORDER BY created_at LIMIT 1";
        cmd.Parameters.AddWithValue("$name", name);
        var existing = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrEmpty(existing))
            return existing;
        return InsertClub(name, null, null, null, null, note, DateTimeOffset.UtcNow).Id;
    }

    private ClubRow InsertClub(
        string name,
        string? city,
        string? contactName,
        string? phone,
        string? telegram,
        string? note,
        DateTimeOffset createdAt)
    {
        var club = new ClubRow(Guid.NewGuid().ToString("D"), name, city, contactName, phone, telegram, note, createdAt);
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO clubs (id, name, city, contact_name, phone, telegram, note, created_at)
            VALUES ($id, $name, $city, $contact, $phone, $telegram, $note, $created)
            """;
        cmd.Parameters.AddWithValue("$id", club.Id);
        cmd.Parameters.AddWithValue("$name", club.Name);
        cmd.Parameters.AddWithValue("$city", (object?)club.City ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$contact", (object?)club.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$phone", (object?)club.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$telegram", (object?)club.Telegram ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)club.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", Stamp(club.CreatedAt));
        cmd.ExecuteNonQuery();
        return club;
    }

    private void AddEvent(string? clubId, string? licenseId, string action, string message)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO license_events (id, club_id, license_id, action, message, created_at)
            VALUES ($id, $club, $license, $action, $message, $created)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$club", (object?)clubId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$license", (object?)licenseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$message", message);
        cmd.Parameters.AddWithValue("$created", Stamp(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    private List<ClubRow> ReadClubs()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM clubs ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var list = new List<ClubRow>();
        while (reader.Read())
            list.Add(ReadClub(reader));
        return list;
    }

    private List<LicenseRow> ReadLicenses()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM licenses";
        using var reader = cmd.ExecuteReader();
        var list = new List<LicenseRow>();
        while (reader.Read())
            list.Add(Read(reader));
        return list;
    }

    private List<LicenseEventRow> ReadEvents(string clubId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM license_events WHERE club_id = $id ORDER BY created_at DESC LIMIT 40";
        cmd.Parameters.AddWithValue("$id", clubId);
        using var reader = cmd.ExecuteReader();
        var list = new List<LicenseEventRow>();
        while (reader.Read())
        {
            list.Add(new LicenseEventRow(
                reader.GetString(reader.GetOrdinal("id")),
                reader.IsDBNull(reader.GetOrdinal("club_id")) ? null : reader.GetString(reader.GetOrdinal("club_id")),
                reader.IsDBNull(reader.GetOrdinal("license_id")) ? null : reader.GetString(reader.GetOrdinal("license_id")),
                reader.GetString(reader.GetOrdinal("action")),
                reader.GetString(reader.GetOrdinal("message")),
                Parse(reader.GetString(reader.GetOrdinal("created_at")))));
        }

        return list;
    }

    private ClubRow? FindClub(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM clubs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadClub(reader) : null;
    }

    private static ClubRow ReadClub(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("id")),
        reader.GetString(reader.GetOrdinal("name")),
        Optional(reader, "city"),
        Optional(reader, "contact_name"),
        Optional(reader, "phone"),
        Optional(reader, "telegram"),
        Optional(reader, "note"),
        Parse(reader.GetString(reader.GetOrdinal("created_at"))));

    private static string? Optional(SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

    private static LicenseRow? CurrentLicense(IEnumerable<LicenseRow> licenses)
    {
        var list = licenses.ToList();
        var live = list.Where(l => !l.Revoked).OrderByDescending(l => l.ExpiresAt).FirstOrDefault();
        return live ?? list.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
    }

    private static int Rank(ClubOverview item) => item.Status switch
    {
        "expired" => 0,
        "expiring" => 1,
        "waiting" => 2,
        "trial" => 3,
        "active" => 4,
        "revoked" => 6,
        _ => 5,
    };

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool LicenseTokenExpired(LicenseRow row) =>
        ShiftClub.Shared.Licensing.LicenseToken.IsExpired(row.ExpiresAt);
}
