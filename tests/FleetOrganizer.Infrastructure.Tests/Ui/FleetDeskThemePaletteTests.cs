using System.Windows;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Infrastructure.Tests.Ui;

public sealed class FleetDeskThemePaletteTests
{
    [Fact]
    public void HighContrastAlwaysUsesWindowsSystemColors()
    {
        var palette = FleetDeskThemePalette.Resolve(
            FleetDeskTheme.Dark,
            highContrast: true,
            windowsUsesDarkTheme: true);

        Assert.Equal(SystemColors.WindowColor, palette.AppBackground);
        Assert.Equal(SystemColors.WindowTextColor, palette.TextPrimary);
        Assert.Equal(SystemColors.HighlightColor, palette.Accent);
        Assert.Equal(SystemColors.WindowTextColor, palette.Border);
    }

    [Fact]
    public void SystemThemeFollowsWindowsDarkPreference()
    {
        var light = FleetDeskThemePalette.Resolve(
            FleetDeskTheme.System,
            highContrast: false,
            windowsUsesDarkTheme: false);
        var dark = FleetDeskThemePalette.Resolve(
            FleetDeskTheme.System,
            highContrast: false,
            windowsUsesDarkTheme: true);

        Assert.NotEqual(light.AppBackground, dark.AppBackground);
        Assert.NotEqual(light.TextPrimary, dark.TextPrimary);
    }
}
