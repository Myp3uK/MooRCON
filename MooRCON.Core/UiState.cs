namespace MooRCON.Core;

/// <summary>
/// Состояние интерфейса между запусками: геометрия окна и открыта ли боковая панель.
/// Координаты хранятся как есть; проверку, что окно попадает на существующий экран,
/// делает сама оболочка — монитор между запусками могли отключить.
/// </summary>
public sealed class UiState
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Панель с игроками и заготовками команд.</summary>
    public bool PanelOpen { get; set; } = true;

    /// <summary>Есть ли сохранённая геометрия (первый запуск — нет).</summary>
    public bool HasGeometry =>
        WindowLeft is not null && WindowTop is not null &&
        WindowWidth is > 0 && WindowHeight is > 0;
}
