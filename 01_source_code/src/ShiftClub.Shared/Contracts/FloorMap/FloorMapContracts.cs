using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.FloorMap;

public sealed record FloorMapElementDto(
    Guid Id,
    Guid BranchId,
    FloorMapElementKind Kind,
    string? Label,
    int GridCol,
    int GridRow,
    int ColSpan,
    int RowSpan,
    int? EndCol,
    int? EndRow,
    string? ColorHex,
    int SortOrder,
    bool IsVisible,
    string? FillHex = null,
    int? StrokeWidth = null,
    int? FontSize = null,
    bool ShowIcon = true,
    int RotationDeg = 0);

public sealed record UpsertFloorMapElementRequest(
    FloorMapElementKind Kind,
    string? Label,
    int GridCol,
    int GridRow,
    int ColSpan = 1,
    int RowSpan = 1,
    int? EndCol = null,
    int? EndRow = null,
    string? ColorHex = null,
    int SortOrder = 0,
    bool IsVisible = true,
    string? FillHex = null,
    int? StrokeWidth = null,
    int? FontSize = null,
    bool ShowIcon = true,
    int RotationDeg = 0);

public sealed record UpdateFloorMapSettingsRequest(
    int FloorGridCols,
    int FloorGridRows,
    string? FloorBackgroundHex = null);

public sealed record FloorMapDto(
    Guid BranchId,
    int GridCols,
    int GridRows,
    IReadOnlyList<ComputerDto> Computers,
    IReadOnlyList<FloorMapElementDto> Elements,
    string? BackgroundHex = null);
