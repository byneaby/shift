using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Licensing;

public sealed record LicenseRow(
    string Id,
    string Key,
    string ClubName,
    int MaxComputers,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool Revoked,
    string? MachineId,
    string? MachineName,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastSeenAt,
    string? Note,
    string? ClubId);

public sealed partial class LicenseStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public LicenseStore(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        _db = new SqliteConnection(builder.ToString());
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS licenses (
              id TEXT PRIMARY KEY,
              key TEXT NOT NULL UNIQUE,
              club_name TEXT NOT NULL,
              max_computers INTEGER NOT NULL,
              kind TEXT NOT NULL,
              created_at TEXT NOT NULL,
              expires_at TEXT NOT NULL,
              revoked INTEGER NOT NULL DEFAULT 0,
              machine_id TEXT,
              machine_name TEXT,
              activated_at TEXT,
              last_seen_at TEXT,
              note TEXT
            );
            CREATE TABLE IF NOT EXISTS clubs (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              city TEXT,
              contact_name TEXT,
              phone TEXT,
              telegram TEXT,
              note TEXT,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS license_events (
              id TEXT PRIMARY KEY,
              club_id TEXT,
              license_id TEXT,
              action TEXT NOT NULL,
              message TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        MigrateClubs();
    }

    public async Task<LicenseRow> CreateAsync(
        string clubName,
        int maxComputers,
        int days,
        string kind,
        string? note,
        CancellationToken cancellationToken,
        string? clubId = null)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var key = LicenseKeyFormat.Create();
                var boundClub = clubId ?? EnsureClubByName(clubName, null);
                var row = new LicenseRow(
                    Guid.NewGuid().ToString("D"),
                    key,
                    clubName,
                    maxComputers,
                    kind,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(days),
                    false,
                    null,
                    null,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    boundClub);
                try
                {
                    Insert(row);
                    AddEvent(boundClub, row.Id, "issued", $"Выдан ключ {row.Key}, {maxComputers} мест, {days} дн., {(kind == "trial" ? "триал" : "оплата")}.");
                    return row;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && attempt < 4)
                {
                    // редкое столкновение ключа
                }
            }

            throw new InvalidOperationException("Не удалось выдать уникальный ключ.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<LicenseRow>> ListAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM licenses ORDER BY created_at DESC";
            using var reader = cmd.ExecuteReader();
            var list = new List<LicenseRow>();
            while (reader.Read())
                list.Add(Read(reader));
            return list;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> FindByKeyAsync(string key, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return FindByKey(key);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM licenses WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            using var cmd = _db.CreateCommand();
            var current = FindById(id);
            if (current is null)
                return false;
            cmd.CommandText = "UPDATE licenses SET revoked = 1 WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var ok = cmd.ExecuteNonQuery() == 1;
            if (ok)
                AddEvent(current.ClubId, id, "revoked", "Лицензия отозвана. Сервер клуба остановится при следующей проверке.");
            return ok;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> UnbindAsync(string id, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE licenses
                SET machine_id = NULL, machine_name = NULL, activated_at = NULL
                WHERE id = $id
                """;
            var current = FindById(id);
            if (current is null)
                return false;
            cmd.Parameters.AddWithValue("$id", id);
            var ok = cmd.ExecuteNonQuery() == 1;
            if (ok)
                AddEvent(current.ClubId, id, "unbound", "Сервер отвязан. Ключ можно активировать на новом сервере клуба.");
            return ok;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> ExtendAsync(string id, int days, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var current = FindById(id);
            if (current is null)
                return null;

            var from = current.ExpiresAt > DateTimeOffset.UtcNow ? current.ExpiresAt : DateTimeOffset.UtcNow;
            var next = from.AddDays(days);
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE licenses SET expires_at = $exp, revoked = 0 WHERE id = $id";
            cmd.Parameters.AddWithValue("$exp", Stamp(next));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            AddEvent(current.ClubId, id, "extended", $"Срок продлён на {days} дн., до {next:dd.MM.yyyy}.");
            return FindById(id);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LicenseRow?> BindAndTouchAsync(
        string id,
        string machineId,
        string? machineName,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var current = FindById(id);
            if (current is null)
                return null;

            var activated = current.ActivatedAt ?? DateTimeOffset.UtcNow;
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE licenses
                SET machine_id = $mid,
                    machine_name = $name,
                    activated_at = $activated,
                    last_seen_at = $seen
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$mid", machineId);
            cmd.Parameters.AddWithValue("$name", (object?)machineName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$activated", Stamp(activated));
            cmd.Parameters.AddWithValue("$seen", Stamp(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            return FindById(id);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _mutex.Dispose();
    }

    private void Insert(LicenseRow row)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO licenses
              (id, key, club_name, max_computers, kind, created_at, expires_at, revoked, note, club_id)
            VALUES
              ($id, $key, $club, $seats, $kind, $created, $expires, 0, $note, $clubId)
            """;
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.Parameters.AddWithValue("$key", row.Key);
        cmd.Parameters.AddWithValue("$club", row.ClubName);
        cmd.Parameters.AddWithValue("$seats", row.MaxComputers);
        cmd.Parameters.AddWithValue("$kind", row.Kind);
        cmd.Parameters.AddWithValue("$created", Stamp(row.CreatedAt));
        cmd.Parameters.AddWithValue("$expires", Stamp(row.ExpiresAt));
        cmd.Parameters.AddWithValue("$note", (object?)row.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clubId", (object?)row.ClubId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private LicenseRow? FindByKey(string key)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM licenses WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private LicenseRow? FindById(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM licenses WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static LicenseRow Read(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("id")),
        reader.GetString(reader.GetOrdinal("key")),
        reader.GetString(reader.GetOrdinal("club_name")),
        reader.GetInt32(reader.GetOrdinal("max_computers")),
        reader.GetString(reader.GetOrdinal("kind")),
        Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        Parse(reader.GetString(reader.GetOrdinal("expires_at"))),
        reader.GetInt32(reader.GetOrdinal("revoked")) != 0,
        reader.IsDBNull(reader.GetOrdinal("machine_id")) ? null : reader.GetString(reader.GetOrdinal("machine_id")),
        reader.IsDBNull(reader.GetOrdinal("machine_name")) ? null : reader.GetString(reader.GetOrdinal("machine_name")),
        reader.IsDBNull(reader.GetOrdinal("activated_at")) ? null : Parse(reader.GetString(reader.GetOrdinal("activated_at"))),
        reader.IsDBNull(reader.GetOrdinal("last_seen_at")) ? null : Parse(reader.GetString(reader.GetOrdinal("last_seen_at"))),
        reader.IsDBNull(reader.GetOrdinal("note")) ? null : reader.GetString(reader.GetOrdinal("note")),
        reader.IsDBNull(reader.GetOrdinal("club_id")) ? null : reader.GetString(reader.GetOrdinal("club_id")));

    private static string Stamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
