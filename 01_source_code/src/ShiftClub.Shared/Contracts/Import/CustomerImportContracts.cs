namespace ShiftClub.Shared.Contracts.Import;

public static class CustomerImportActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Skip = "skip";
}

public sealed record CustomerImportRowDto(
    int LineNumber,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    decimal Balance,
    decimal BonusBalance,
    string? Notes,
    string Action,
    string? Problem);

public sealed record CustomerImportPreviewDto(
    int TotalRows,
    int WillCreate,
    int WillUpdate,
    int WillSkip,
    decimal BalanceToPost,
    IReadOnlyList<string> RecognizedColumns,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CustomerImportRowDto> Rows,
    string Summary);

public sealed record CustomerImportResultDto(
    int Created,
    int Updated,
    int Skipped,
    decimal BalancePosted,
    string Summary);

public sealed record CustomerImportRequest(
    string Csv,
    bool UpdateExisting,
    bool ImportBalances);
