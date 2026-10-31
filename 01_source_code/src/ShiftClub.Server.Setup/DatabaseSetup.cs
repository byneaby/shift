using Npgsql;

namespace ShiftClub.Server.Setup;

public static class DatabaseSetup
{
    public static string QuoteIdent(string name)
    {
        if (name.Length == 0 || name.Length > 63 || !IsSqlName(name))
            throw new ArgumentException("Недопустимое имя базы или пользователя.", nameof(name));

        return "\"" + name + "\"";
    }

    private static bool IsSqlName(string name)
    {
        if (name.Length == 0 || name.Length > 63)
            return false;
        var first = name[0];
        if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
            return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                return false;
        }
        return true;
    }

    public static string QuoteLiteral(string value)
    {
        if (value.Any(c => c is '\0' or '\r' or '\n'))
            throw new ArgumentException("Пароль содержит недопустимый символ.", nameof(value));

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string CreateRoleSql(string user, string password, bool exists)
    {
        var ident = QuoteIdent(user);
        var literal = QuoteLiteral(password);
        return exists
            ? $"ALTER ROLE {ident} WITH LOGIN PASSWORD {literal}"
            : $"CREATE ROLE {ident} WITH LOGIN PASSWORD {literal}";
    }

    public static string CreateDatabaseSql(string database, string owner) =>
        $"CREATE DATABASE {QuoteIdent(database)} OWNER {QuoteIdent(owner)}";

    public static async Task<string?> TryConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 8,
                CommandTimeout = 8
            };
            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return null;
        }
        catch (Exception)
        {
            return "Не удалось подключиться к PostgreSQL. Проверьте, что служба запущена, и что адрес, порт и пароль верные.";
        }
    }

    public static async Task<string?> EnsureDatabaseAsync(ServerSetupAnswers answers, CancellationToken cancellationToken)
    {
        var adminError = await TryConnectAsync(ServerSecrets.AdminConnectionString(answers), cancellationToken);
        if (adminError is not null)
            return adminError;

        try
        {
            var admin = new NpgsqlConnectionStringBuilder(ServerSecrets.AdminConnectionString(answers));
            await using var conn = new NpgsqlConnection(admin.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            var user = answers.DatabaseUser.Trim();
            var database = answers.DatabaseName.Trim();
            var roleExists = await ExistsAsync(conn, "SELECT 1 FROM pg_roles WHERE rolname = @name", user, cancellationToken);
            await using (var role = new NpgsqlCommand(CreateRoleSql(user, answers.DatabasePassword, roleExists), conn))
                await role.ExecuteNonQueryAsync(cancellationToken);

            var dbExists = await ExistsAsync(conn, "SELECT 1 FROM pg_database WHERE datname = @name", database, cancellationToken);
            if (!dbExists)
            {
                await using var create = new NpgsqlCommand(CreateDatabaseSql(database, user), conn);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            return await TryConnectAsync(ServerSecrets.AppConnectionString(answers), cancellationToken);
        }
        catch (Exception)
        {
            return "Подключились к PostgreSQL, но не смогли создать базу. Проверьте, что это пароль пользователя postgres, а не обычного пользователя.";
        }
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection conn, string sql, string name, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is not null && value is not DBNull;
    }
}
