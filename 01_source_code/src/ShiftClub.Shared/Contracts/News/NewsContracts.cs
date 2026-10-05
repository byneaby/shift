namespace ShiftClub.Shared.Contracts.News;

public sealed record ClubNewsDto(
    Guid Id,
    Guid BranchId,
    string Title,
    string Body,
    string? ImageUrl,
    string Category,
    bool IsPublished,
    bool IsPinned,
    int SortOrder,
    DateTimeOffset? PublishAt,
    DateTimeOffset? ExpireAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertClubNewsRequest(
    string Title,
    string Body,
    string? ImageUrl,
    string Category,
    bool IsPublished,
    bool IsPinned,
    int SortOrder,
    DateTimeOffset? PublishAt,
    DateTimeOffset? ExpireAt);

/// <summary>Краткая карточка для Shell / публичной ленты.</summary>
public sealed record ClientNewsDto(
    Guid Id,
    string Title,
    string Body,
    string? ImageUrl,
    string Category,
    bool IsPinned,
    DateTimeOffset? PublishAt);
