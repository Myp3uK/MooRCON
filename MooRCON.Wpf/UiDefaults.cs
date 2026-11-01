using System.Windows;
using System.Windows.Controls;

namespace MooRCON.Wpf;

/// <summary>Умолчания WPF, которые нельзя задать стилем — только через метаданные свойств.</summary>
public static class UiDefaults
{
    private static bool _applied;

    /// <summary>
    /// Подсказки: появляются заметно быстрее стандартного, держатся дольше (в заготовках
    /// команд они длинные) и показываются на выключенных кнопках — там подсказка нужнее
    /// всего, ведь по самой кнопке непонятно, чего ей не хватает.
    /// </summary>
    public static void Apply()
    {
        if (_applied) return; // OverrideMetadata во второй раз бросает исключение
        _applied = true;

        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(250));
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(20000));
        ToolTipService.ShowOnDisabledProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
    }
}
