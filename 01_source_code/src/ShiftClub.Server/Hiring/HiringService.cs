using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ShiftClub.Server.Hiring;

public sealed class HiringService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HiringOptions _opt;
    private readonly string _dbPath;
    private readonly string _photosDir;
    private readonly string _backupsDir;
    private readonly object _initLock = new();
    private bool _initialized;

    public HiringService(IOptions<HiringOptions> options, IWebHostEnvironment env)
    {
        _opt = options.Value;
        var root = Path.Combine(env.ContentRootPath, _opt.DataFolder);
        Directory.CreateDirectory(root);
        _photosDir = Path.Combine(root, "photos");
        _backupsDir = Path.Combine(root, "backups");
        Directory.CreateDirectory(_photosDir);
        Directory.CreateDirectory(_backupsDir);
        _dbPath = Path.Combine(root, "hiring.db");
    }

    private SqliteConnection Open()
    {
        EnsureDb();
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        return c;
    }

    private void EnsureDb()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            using var c = new SqliteConnection($"Data Source={_dbPath}");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS candidates (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  full_name TEXT NOT NULL,
                  birth_date TEXT,
                  age INTEGER,
                  phone TEXT,
                  messenger TEXT,
                  district TEXT,
                  travel_time TEXT,
                  instagram TEXT,
                  tiktok TEXT,
                  photo_path TEXT,
                  answers_json TEXT NOT NULL,
                  current_status TEXT NOT NULL DEFAULT 'анкета заполнена',
                  consent_given INTEGER NOT NULL DEFAULT 1,
                  interview_json TEXT,
                  scores_json TEXT,
                  flags_json TEXT,
                  trial_json TEXT,
                  decision_json TEXT,
                  manager_notes TEXT,
                  total_score REAL,
                  archived INTEGER NOT NULL DEFAULT 0,
                  created_at TEXT NOT NULL,
                  updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS history (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  candidate_id INTEGER NOT NULL,
                  action_type TEXT NOT NULL,
                  old_value TEXT,
                  new_value TEXT,
                  comment TEXT,
                  created_at TEXT NOT NULL,
                  created_by TEXT
                );
                CREATE TABLE IF NOT EXISTS sessions (
                  token TEXT PRIMARY KEY,
                  created_at TEXT NOT NULL,
                  expires_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_candidates_status ON candidates(current_status);
                CREATE INDEX IF NOT EXISTS idx_candidates_created ON candidates(created_at);
                """;
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    public bool ValidatePin(string? pin) =>
        !string.IsNullOrWhiteSpace(pin)
        && string.Equals(pin.Trim(), _opt.ManagerPin.Trim(), StringComparison.Ordinal);

    public string CreateSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var now = DateTimeOffset.UtcNow;
        var exp = now.AddHours(_opt.SessionHours);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(token, created_at, expires_at) VALUES($t,$c,$e)";
        cmd.Parameters.AddWithValue("$t", token);
        cmd.Parameters.AddWithValue("$c", now.ToString("O"));
        cmd.Parameters.AddWithValue("$e", exp.ToString("O"));
        cmd.ExecuteNonQuery();
        return token;
    }

    public bool ValidateSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT expires_at FROM sessions WHERE token=$t LIMIT 1";
        cmd.Parameters.AddWithValue("$t", token.Trim());
        var exp = cmd.ExecuteScalar() as string;
        if (exp is null) return false;
        if (!DateTimeOffset.TryParse(exp, out var e) || e < DateTimeOffset.UtcNow) return false;
        return true;
    }

    public long CreateCandidate(Dictionary<string, JsonElement> answers, string? photoRelativePath)
    {
        string S(string key)
        {
            if (!answers.TryGetValue(key, out var el)) return "";
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.Number => el.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => el.ToString(),
                _ => el.ToString()
            };
        }

        var fullName = S("fullName").Trim();
        if (fullName.Length < 2) throw new InvalidOperationException("Укажите ФИО.");
        var phone = S("phone").Trim();
        if (phone.Length < 10) throw new InvalidOperationException("Укажите корректный телефон.");
        if (!answers.TryGetValue("consent", out var consent) || consent.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Нужно согласие на обработку данных.");

        DateTime? birth = null;
        int? age = null;
        var birthRaw = S("birthDate");
        if (DateTime.TryParse(birthRaw, out var bd))
        {
            birth = bd.Date;
            age = AgeYears(bd);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var json = JsonSerializer.Serialize(answers, JsonOpts);
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO candidates(
              full_name, birth_date, age, phone, messenger, district, travel_time,
              instagram, tiktok, photo_path, answers_json, current_status, consent_given,
              created_at, updated_at)
            VALUES($n,$b,$a,$p,$m,$d,$tt,$ig,$tk,$ph,$aj,'анкета заполнена',1,$c,$u);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$n", fullName);
        cmd.Parameters.AddWithValue("$b", (object?)birth?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", (object?)age ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", phone);
        cmd.Parameters.AddWithValue("$m", string.IsNullOrWhiteSpace(S("messenger"))
            ? (string.IsNullOrWhiteSpace(S("telegram")) ? S("whatsapp") : S("telegram"))
            : S("messenger"));
        cmd.Parameters.AddWithValue("$d", S("district"));
        cmd.Parameters.AddWithValue("$tt", S("travelTime"));
        cmd.Parameters.AddWithValue("$ig", S("instagram"));
        cmd.Parameters.AddWithValue("$tk", S("tiktok"));
        cmd.Parameters.AddWithValue("$ph", (object?)photoRelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aj", json);
        cmd.Parameters.AddWithValue("$c", now);
        cmd.Parameters.AddWithValue("$u", now);
        var id = (long)(cmd.ExecuteScalar() ?? 0L);
        AddHistory(c, tx, id, "status", null, "анкета заполнена", "Анкета отправлена", "candidate");
        tx.Commit();
        BackupQuiet();
        return id;
    }

    public string SavePhoto(long tempId, Stream stream, string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "")?.ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            ext = ".jpg";
        var name = $"c_{DateTime.UtcNow:yyyyMMddHHmmss}_{RandomNumberGenerator.GetInt32(1000, 9999)}{ext}";
        var path = Path.Combine(_photosDir, name);
        using (var fs = File.Create(path))
            stream.CopyTo(fs);
        return $"photos/{name}";
    }

    public IReadOnlyList<object> ListCandidates(string? status, string? q, bool archived)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var sql = new StringBuilder("""
            SELECT id, full_name, age, phone, district, current_status, total_score,
                   created_at, answers_json, manager_notes, archived
            FROM candidates WHERE archived=$arch
            """);
        cmd.Parameters.AddWithValue("$arch", archived ? 1 : 0);
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql.Append(" AND current_status=$st");
            cmd.Parameters.AddWithValue("$st", status.Trim());
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.Append(" AND (full_name LIKE $q OR phone LIKE $q OR instagram LIKE $q OR manager_notes LIKE $q)");
            cmd.Parameters.AddWithValue("$q", "%" + q.Trim() + "%");
        }
        sql.Append(" ORDER BY datetime(created_at) DESC LIMIT 500");
        cmd.CommandText = sql.ToString();
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var answers = ParseObj(r["answers_json"]?.ToString());
            list.Add(new
            {
                id = r.GetInt64(0),
                fullName = r.GetString(1),
                age = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                phone = r.IsDBNull(3) ? null : r.GetString(3),
                district = r.IsDBNull(4) ? null : r.GetString(4),
                status = r.GetString(5),
                totalScore = r.IsDBNull(6) ? (double?)null : r.GetDouble(6),
                createdAt = r.GetString(7),
                schedule = GetStr(answers, "schedule"),
                expectedSalary = GetStr(answers, "expectedSalary"),
                trialReady = GetStr(answers, "trialReady"),
                notes = r.IsDBNull(9) ? null : r.GetString(9)
            });
        }
        return list;
    }

    public object? GetCandidate(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM candidates WHERE id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var answers = ParseObj(r["answers_json"]?.ToString());
        var hints = HiringHints.Build(answers);
        var autoProfile = HiringAutoProfile.Build(answers);
        return new
        {
            id = r.GetInt64(r.GetOrdinal("id")),
            fullName = r["full_name"]?.ToString(),
            birthDate = r["birth_date"]?.ToString(),
            age = r["age"] is long or int ? Convert.ToInt32(r["age"]) : (int?)null,
            phone = r["phone"]?.ToString(),
            messenger = r["messenger"]?.ToString(),
            district = r["district"]?.ToString(),
            travelTime = r["travel_time"]?.ToString(),
            instagram = r["instagram"]?.ToString(),
            tiktok = r["tiktok"]?.ToString(),
            photoPath = r["photo_path"]?.ToString(),
            status = r["current_status"]?.ToString(),
            answers,
            interview = ParseObj(r["interview_json"]?.ToString()),
            scores = ParseObj(r["scores_json"]?.ToString()),
            flags = ParseObj(r["flags_json"]?.ToString()),
            trial = ParseObj(r["trial_json"]?.ToString()),
            decision = ParseObj(r["decision_json"]?.ToString()),
            managerNotes = r["manager_notes"]?.ToString(),
            totalScore = r["total_score"] is DBNull or null ? (double?)null : Convert.ToDouble(r["total_score"]),
            createdAt = r["created_at"]?.ToString(),
            updatedAt = r["updated_at"]?.ToString(),
            hints,
            autoProfile,
            scoreBreakdown = HiringScores.Classify(
                r["total_score"] is DBNull or null ? null : Convert.ToDouble(r["total_score"]))
        };
    }

    public void UpdateCandidate(long id, JsonElement patch, string by)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var get = c.CreateCommand();
        get.Transaction = tx;
        get.CommandText = "SELECT current_status, total_score FROM candidates WHERE id=$id";
        get.Parameters.AddWithValue("$id", id);
        using var r = get.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Кандидат не найден.");
        var oldStatus = r.GetString(0);
        r.Close();

        string? newStatus = null;
        string? interview = null;
        string? scores = null;
        string? flags = null;
        string? trial = null;
        string? decision = null;
        string? notes = null;
        double? total = null;

        if (patch.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
            newStatus = st.GetString();
        if (patch.TryGetProperty("interview", out var iv))
            interview = iv.GetRawText();
        if (patch.TryGetProperty("scores", out var sc))
        {
            scores = sc.GetRawText();
            total = HiringScores.ComputePercent(sc);
        }
        if (patch.TryGetProperty("flags", out var fl))
            flags = fl.GetRawText();
        if (patch.TryGetProperty("trial", out var tr))
            trial = tr.GetRawText();
        if (patch.TryGetProperty("decision", out var dec))
            decision = dec.GetRawText();
        if (patch.TryGetProperty("managerNotes", out var mn) && mn.ValueKind == JsonValueKind.String)
            notes = mn.GetString();

        if (newStatus is null && interview is not null && oldStatus == "анкета заполнена")
            newStatus = "собеседование проводится";
        if (newStatus is null && trial is not null)
            newStatus = "приглашен на пробную смену";

        using var upd = c.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE candidates SET
              current_status=COALESCE($st, current_status),
              interview_json=COALESCE($iv, interview_json),
              scores_json=COALESCE($sc, scores_json),
              flags_json=COALESCE($fl, flags_json),
              trial_json=COALESCE($tr, trial_json),
              decision_json=COALESCE($dec, decision_json),
              manager_notes=COALESCE($notes, manager_notes),
              total_score=COALESCE($tot, total_score),
              updated_at=$u
            WHERE id=$id
            """;
        upd.Parameters.AddWithValue("$st", (object?)newStatus ?? DBNull.Value);
        upd.Parameters.AddWithValue("$iv", (object?)interview ?? DBNull.Value);
        upd.Parameters.AddWithValue("$sc", (object?)scores ?? DBNull.Value);
        upd.Parameters.AddWithValue("$fl", (object?)flags ?? DBNull.Value);
        upd.Parameters.AddWithValue("$tr", (object?)trial ?? DBNull.Value);
        upd.Parameters.AddWithValue("$dec", (object?)decision ?? DBNull.Value);
        upd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        upd.Parameters.AddWithValue("$tot", (object?)total ?? DBNull.Value);
        upd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        upd.Parameters.AddWithValue("$id", id);
        upd.ExecuteNonQuery();

        if (newStatus is not null && newStatus != oldStatus)
            AddHistory(c, tx, id, "status", oldStatus, newStatus, null, by);
        tx.Commit();
        BackupQuiet();
    }

    public void SetArchived(long id, bool archived, string by)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE candidates SET archived=$a, updated_at=$u WHERE id=$id";
        cmd.Parameters.AddWithValue("$a", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        AddHistory(c, tx, id, archived ? "archive" : "unarchive", null, archived.ToString(), null, by);
        tx.Commit();
    }

    public object Dashboard()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT current_status, COUNT(*) FROM candidates WHERE archived=0 GROUP BY current_status;
            """;
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                byStatus[r.GetString(0)] = r.GetInt32(1);
        }
        return new
        {
            byStatus,
            newForms = byStatus.GetValueOrDefault("анкета заполнена"),
            interview = byStatus.GetValueOrDefault("собеседование проводится")
                + byStatus.GetValueOrDefault("приглашен на собеседование"),
            trial = byStatus.GetValueOrDefault("приглашен на пробную смену"),
            hired = byStatus.GetValueOrDefault("принят"),
            reserve = byStatus.GetValueOrDefault("резерв")
        };
    }

    public string PhotosAbsolutePath => _photosDir;

    private static void AddHistory(SqliteConnection c, SqliteTransaction tx, long id, string type, string? oldV, string? newV, string? comment, string by)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO history(candidate_id, action_type, old_value, new_value, comment, created_at, created_by)
            VALUES($id,$t,$o,$n,$c,$at,$by)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$o", (object?)oldV ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)newV ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$by", by);
        cmd.ExecuteNonQuery();
    }

    private void BackupQuiet()
    {
        try
        {
            var name = $"shift_candidates_backup_{DateTime.Now:yyyy-MM-dd_HH-mm}.db";
            File.Copy(_dbPath, Path.Combine(_backupsDir, name), overwrite: true);
            var files = new DirectoryInfo(_backupsDir).GetFiles("shift_candidates_backup_*.db")
                .OrderByDescending(f => f.LastWriteTime).Skip(20).ToList();
            foreach (var f in files) f.Delete();
        }
        catch { /* ignore backup errors */ }
    }

    private static int AgeYears(DateTime birth)
    {
        var today = DateTime.Today;
        var age = today.Year - birth.Year;
        if (birth.Date > today.AddYears(-age)) age--;
        return age;
    }

    private static Dictionary<string, JsonElement> ParseObj(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new();
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(); }
    }

    private static string? GetStr(Dictionary<string, JsonElement> d, string key)
    {
        if (!d.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }
}
