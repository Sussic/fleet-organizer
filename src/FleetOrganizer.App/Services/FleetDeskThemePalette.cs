using System.Windows;
using System.Windows.Media;
using FleetOrganizer.App.ViewModels;

namespace FleetOrganizer.App.Services;

public sealed record FleetDeskThemePalette(
    Color AppBackground,
    Color Sidebar,
    Color SidebarText,
    Color Surface,
    Color SurfaceAlt,
    Color Border,
    Color TextPrimary,
    Color TextMuted,
    Color Accent,
    Color AccentText,
    Color AccentHover,
    Color AccentSoft,
    Color Success,
    Color Warning,
    Color Danger)
{
    public static FleetDeskThemePalette Resolve(
        FleetDeskTheme theme,
        bool highContrast,
        bool windowsUsesDarkTheme)
    {
        if (highContrast)
        {
            return new FleetDeskThemePalette(
                SystemColors.WindowColor,
                SystemColors.WindowColor,
                SystemColors.WindowTextColor,
                SystemColors.WindowColor,
                SystemColors.ControlColor,
                SystemColors.WindowTextColor,
                SystemColors.WindowTextColor,
                SystemColors.GrayTextColor,
                SystemColors.HighlightColor,
                SystemColors.HighlightTextColor,
                SystemColors.HighlightColor,
                SystemColors.HighlightColor,
                SystemColors.WindowTextColor,
                SystemColors.WindowTextColor,
                SystemColors.WindowTextColor);
        }

        var useDark = theme == FleetDeskTheme.Dark ||
            (theme == FleetDeskTheme.System && windowsUsesDarkTheme);
        return useDark
            ? new FleetDeskThemePalette(
                Rgb(0x0B, 0x12, 0x20),
                Rgb(0x10, 0x18, 0x27),
                Rgb(0xE7, 0xEC, 0xF4),
                Rgb(0x15, 0x1E, 0x2E),
                Rgb(0x1B, 0x26, 0x38),
                Rgb(0x33, 0x41, 0x55),
                Rgb(0xF1, 0xF5, 0xF9),
                Rgb(0xA9, 0xB5, 0xC7),
                Rgb(0x2F, 0x7C, 0xF6),
                Colors.White,
                Rgb(0x1F, 0x68, 0xD8),
                Rgb(0x17, 0x2D, 0x50),
                Rgb(0x49, 0xC6, 0x91),
                Rgb(0xF0, 0xA4, 0x47),
                Rgb(0xF0, 0x6B, 0x78))
            : new FleetDeskThemePalette(
                Rgb(0xF4, 0xF7, 0xFB),
                Rgb(0x10, 0x18, 0x27),
                Rgb(0xE7, 0xEC, 0xF4),
                Colors.White,
                Rgb(0xF7, 0xF9, 0xFC),
                Rgb(0xDC, 0xE3, 0xEC),
                Rgb(0x17, 0x20, 0x33),
                Rgb(0x66, 0x70, 0x85),
                Rgb(0x2F, 0x7C, 0xF6),
                Colors.White,
                Rgb(0x1F, 0x68, 0xD8),
                Rgb(0xEA, 0xF2, 0xFF),
                Rgb(0x16, 0x86, 0x5C),
                Rgb(0xC7, 0x6A, 0x13),
                Rgb(0xC8, 0x3E, 0x4D));
    }

    private static Color Rgb(byte red, byte green, byte blue) =>
        Color.FromRgb(red, green, blue);
}
