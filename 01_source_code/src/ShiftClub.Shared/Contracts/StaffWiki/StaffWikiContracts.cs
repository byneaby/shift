namespace ShiftClub.Shared.Contracts.StaffWiki;

public sealed record StaffWikiPageListItemDto(
    Guid Id,
    string Slug,
    string Title,
    int SortOrder,
    DateTimeOffset? UpdatedAt);

public sealed record StaffWikiPageDto(
    Guid Id,
    string Slug,
    string Title,
    string BodyMarkdown,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedByEmployeeId);

public sealed record UpsertStaffWikiPageRequest(
    string Slug,
    string Title,
    string BodyMarkdown,
    int SortOrder);

public sealed record StaffWikiUploadDto(string Url);
