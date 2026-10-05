using System.Globalization;
using ShiftClub.Application.Licensing;
using ShiftClub.Shared.Contracts.Licensing;
using ShiftClub.Shared.Licensing;

// Утилита вендора: создать пару ключей и выдавать лицензии клубам.
// Приватный ключ НИКОГДА не уезжает в клуб и не коммитится в git.

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "keygen":
        return KeyGen();
    case "issue":
        return Issue(args.Skip(1).ToArray());
    case "inspect":
        return Inspect(args.Skip(1).ToArray());
    default:
        PrintUsage();
        return 1;
}

static int KeyGen()
{
    var (priv, pub) = LicenseKeyCodec.GenerateKeyPair();

    Console.WriteLine("Пара ключей создана. Выполните один раз и храните приватный ключ вне git.");
    Console.WriteLine();
    Console.WriteLine("ПРИВАТНЫЙ (только у вас, им подписываются лицензии):");
    Console.WriteLine(priv);
    Console.WriteLine();
    Console.WriteLine("ПУБЛИЧНЫЙ (кладётся в secrets.env каждого клуба как License__PublicKey):");
    Console.WriteLine(pub);
    Console.WriteLine();
    Console.WriteLine("Дальше: shift-license issue --private <приватный> --club \"Название\" --pc 40 --days 30");
    return 0;
}

static int Issue(string[] args)
{
    var opts = ParseArgs(args);

    if (!opts.TryGetValue("private", out var privateKey) || string.IsNullOrWhiteSpace(privateKey))
    {
        privateKey = Environment.GetEnvironmentVariable("SHIFT_LICENSE_PRIVATE_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            Console.Error.WriteLine("Нужен --private <ключ> или переменная SHIFT_LICENSE_PRIVATE_KEY.");
            return 1;
        }
    }

    if (!opts.TryGetValue("club", out var clubName) || string.IsNullOrWhiteSpace(clubName))
    {
        Console.Error.WriteLine("Нужен --club \"Название клуба\".");
        return 1;
    }

    var maxPc = GetInt(opts, "pc", 0);
    if (maxPc <= 0)
    {
        Console.Error.WriteLine("Нужен --pc <лимит ПК> больше нуля.");
        return 1;
    }

    var days = GetInt(opts, "days", 30);
    var grace = GetInt(opts, "grace", 14);
    var plan = opts.GetValueOrDefault("plan") ?? "care";

    var clubId = opts.TryGetValue("club-id", out var rawId) && Guid.TryParse(rawId, out var parsedId)
        ? parsedId
        : Guid.NewGuid();

    var features = opts.TryGetValue("features", out var rawFeatures) && !string.IsNullOrWhiteSpace(rawFeatures)
        ? rawFeatures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : LicenseFeatures.All;

    var unknown = features.Where(f => !LicenseFeatures.All.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
    if (unknown.Count > 0)
    {
        Console.Error.WriteLine($"Неизвестные фичи: {string.Join(", ", unknown)}");
        Console.Error.WriteLine($"Доступны: {string.Join(", ", LicenseFeatures.All)}");
        return 1;
    }

    var now = DateTimeOffset.UtcNow;
    var payload = new LicensePayload(
        V: 1,
        ClubId: clubId,
        ClubName: clubName.Trim(),
        Plan: plan,
        MaxComputers: maxPc,
        IssuedAt: now,
        ExpiresAt: now.AddDays(days),
        GraceDays: grace,
        Features: features);

    var key = LicenseKeyCodec.Issue(payload, privateKey);

    Console.WriteLine($"Клуб:       {payload.ClubName}");
    Console.WriteLine($"ID клуба:   {payload.ClubId}");
    Console.WriteLine($"Тариф:      {payload.Plan}");
    Console.WriteLine($"Лимит ПК:   {payload.MaxComputers}");
    Console.WriteLine($"Действует:  до {payload.ExpiresAt:dd.MM.yyyy HH:mm} UTC (+{grace} дн. льготных)");
    Console.WriteLine($"Фичи:       {string.Join(", ", payload.Features)}");
    Console.WriteLine();
    Console.WriteLine("Ключ для клуба (вставить в панели: Лицензия → вставить ключ):");
    Console.WriteLine(key);
    return 0;
}

static int Inspect(string[] args)
{
    var opts = ParseArgs(args);
    var key = opts.GetValueOrDefault("key");
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("Нужен --key <ключ> и --public <публичный ключ>.");
        return 1;
    }

    var pub = opts.GetValueOrDefault("public")
              ?? Environment.GetEnvironmentVariable("SHIFT_LICENSE_PUBLIC_KEY")
              ?? "";

    var parsed = LicenseKeyCodec.Parse(key, pub);
    if (!parsed.Ok || parsed.Payload is null)
    {
        Console.Error.WriteLine($"Ключ не прошёл проверку: {parsed.Error}");
        return 1;
    }

    var p = parsed.Payload;
    var state = LicenseKeyCodec.ResolveState(p, DateTimeOffset.UtcNow);
    Console.WriteLine($"Клуб:      {p.ClubName} ({p.ClubId})");
    Console.WriteLine($"Тариф:     {p.Plan}");
    Console.WriteLine($"Лимит ПК:  {p.MaxComputers}");
    Console.WriteLine($"Выдан:     {p.IssuedAt:dd.MM.yyyy}");
    Console.WriteLine($"До:        {p.ExpiresAt:dd.MM.yyyy} (+{p.GraceDays} дн. льготных)");
    Console.WriteLine($"Состояние: {state}");
    Console.WriteLine($"Фичи:      {string.Join(", ", p.Features)}");
    return 0;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;
        var name = args[i][2..];
        var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i]
            : "true";
        result[name] = value;
    }

    return result;
}

static int GetInt(Dictionary<string, string> opts, string name, int fallback) =>
    opts.TryGetValue(name, out var raw) && int.TryParse(raw, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;

static void PrintUsage()
{
    Console.WriteLine("shift-license — выдача лицензий SHIFT клубам.");
    Console.WriteLine();
    Console.WriteLine("  keygen");
    Console.WriteLine("      Создать пару ключей (один раз). Приватный храните вне git.");
    Console.WriteLine();
    Console.WriteLine("  issue --club \"Nexus Arena\" --pc 40 --days 30 [--grace 14]");
    Console.WriteLine("        [--plan care] [--features shift_case,multi_branch] [--club-id <guid>]");
    Console.WriteLine("        [--private <ключ> | env SHIFT_LICENSE_PRIVATE_KEY]");
    Console.WriteLine("      Выдать ключ клубу. Для продления — тот же --club-id с новым --days.");
    Console.WriteLine();
    Console.WriteLine("  inspect --key <ключ> [--public <ключ> | env SHIFT_LICENSE_PUBLIC_KEY]");
    Console.WriteLine("      Посмотреть, что внутри ключа.");
    Console.WriteLine();
    Console.WriteLine($"Фичи: {string.Join(", ", LicenseFeatures.All)}");
}
