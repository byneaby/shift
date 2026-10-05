using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

/// <summary>Декор / ориентир на единой карте зала (касса, вход, стены и т.п.).</summary>
public class FloorMapElement : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public FloorMapElementKind Kind { get; set; }
    public string? Label { get; set; }

    /// <summary>Ячейка начала (иконка / rect / label) или первый узел стены.</summary>
    public int GridCol { get; set; }
    public int GridRow { get; set; }

    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;

    /// <summary>Второй конец стены (узлы сетки).</summary>
    public int? EndCol { get; set; }
    public int? EndRow { get; set; }

    /// <summary>Основной цвет (обводка стены / акцент иконки / текст).</summary>
    public string? ColorHex { get; set; }

    /// <summary>Заливка зоны / фона подписи (если null — ColorHex).</summary>
    public string? FillHex { get; set; }

    /// <summary>Толщина линии стены (логические единицы SVG ~0.05–0.5). null = default.</summary>
    public int? StrokeWidth { get; set; }

    /// <summary>Размер шрифта подписи (px). null = default CSS.</summary>
    public int? FontSize { get; set; }

    /// <summary>Показывать глиф вида (Касса/Вход/…). false = только текст.</summary>
    public bool ShowIcon { get; set; } = true;

    /// <summary>Поворот элемента в градусах (0–359).</summary>
    public int RotationDeg { get; set; }

    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}
