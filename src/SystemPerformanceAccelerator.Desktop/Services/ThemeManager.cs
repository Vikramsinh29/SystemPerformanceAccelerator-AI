using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public static class ThemeManager
{
    private sealed record ThemePalette(
        Color AppBackground,
        Color Surface,
        Color SurfaceMuted,
        Color SurfaceRaised,
        Color InputBackground,
        Color InputBorder,
        Color TextPrimary,
        Color TextSecondary,
        Color TextTertiary,
        Color Border,
        Color BorderStrong,
        Color DataGridHeader,
        Color DataGridRowHover,
        Color DataGridRowSelected,
        Color SecondaryButtonForeground,
        Color SecondaryButtonBorder,
        Color SecondaryButtonHover,
        Color ProgressTrack,
        Color MetaText,
        Color Accent,
        Color AccentHover,
        Color AccentPressed,
        Color AccentSoft,
        Color Focus,
        Color Success,
        Color SuccessSoft,
        Color Warning,
        Color WarningSoft,
        Color Danger,
        Color DangerSoft,
        Color Sidebar,
        Color SidebarHover,
        Color SidebarSelected,
        Color SidebarText,
        Color SidebarMutedText,
        Color SidebarDivider,
        Color SidebarPanel);

    private static readonly ThemePalette LightPalette = new(
        Color.FromRgb(0xF3, 0xF5, 0xF8),
        Colors.White,
        Color.FromRgb(0xF7, 0xF8, 0xFA),
        Colors.White,
        Colors.White,
        Color.FromRgb(0xD5, 0xDA, 0xE2),
        Color.FromRgb(0x1B, 0x1F, 0x2A),
        Color.FromRgb(0x66, 0x70, 0x85),
        Color.FromRgb(0x98, 0xA2, 0xB3),
        Color.FromRgb(0xE3, 0xE7, 0xED),
        Color.FromRgb(0xCD, 0xD3, 0xDC),
        Color.FromRgb(0xF7, 0xF8, 0xFA),
        Color.FromRgb(0xF7, 0xFA, 0xFC),
        Color.FromRgb(0xEA, 0xF3, 0xFF),
        Color.FromRgb(0x34, 0x40, 0x54),
        Color.FromRgb(0xD0, 0xD5, 0xDD),
        Color.FromRgb(0xF9, 0xFA, 0xFB),
        Color.FromRgb(0xEA, 0xEC, 0xF0),
        Color.FromRgb(0x47, 0x54, 0x67),
        Color.FromRgb(0x0F, 0x6C, 0xBD),
        Color.FromRgb(0x11, 0x5E, 0xA3),
        Color.FromRgb(0x0C, 0x3B, 0x5E),
        Color.FromRgb(0xE7, 0xF3, 0xFF),
        Color.FromRgb(0x4F, 0x9E, 0xEA),
        Color.FromRgb(0x10, 0x7C, 0x41),
        Color.FromRgb(0xE8, 0xF5, 0xEE),
        Color.FromRgb(0xCA, 0x50, 0x10),
        Color.FromRgb(0xFF, 0xF4, 0xE5),
        Color.FromRgb(0xC4, 0x2B, 0x1C),
        Color.FromRgb(0xFD, 0xEC, 0xEC),
        Color.FromRgb(0x11, 0x18, 0x27),
        Color.FromRgb(0x1F, 0x29, 0x37),
        Color.FromRgb(0x0F, 0x6C, 0xBD),
        Color.FromRgb(0xE5, 0xE7, 0xEB),
        Color.FromRgb(0x9C, 0xA3, 0xAF),
        Color.FromRgb(0x2A, 0x34, 0x43),
        Color.FromRgb(0x0C, 0x14, 0x20));

    private static readonly ThemePalette DarkPalette = new(
        Color.FromRgb(0x0F, 0x14, 0x1A),
        Color.FromRgb(0x17, 0x1C, 0x22),
        Color.FromRgb(0x1D, 0x23, 0x2B),
        Color.FromRgb(0x1C, 0x22, 0x2A),
        Color.FromRgb(0x13, 0x18, 0x1E),
        Color.FromRgb(0x43, 0x4C, 0x59),
        Color.FromRgb(0xF2, 0xF4, 0xF7),
        Color.FromRgb(0xB3, 0xBC, 0xC8),
        Color.FromRgb(0x7E, 0x89, 0x98),
        Color.FromRgb(0x32, 0x3B, 0x47),
        Color.FromRgb(0x4A, 0x55, 0x63),
        Color.FromRgb(0x1D, 0x23, 0x2B),
        Color.FromRgb(0x21, 0x2B, 0x37),
        Color.FromRgb(0x19, 0x3A, 0x58),
        Color.FromRgb(0xE1, 0xE6, 0xEC),
        Color.FromRgb(0x4A, 0x55, 0x63),
        Color.FromRgb(0x23, 0x2B, 0x35),
        Color.FromRgb(0x31, 0x3A, 0x45),
        Color.FromRgb(0xC8, 0xD0, 0xDB),
        Color.FromRgb(0x28, 0x8E, 0xD8),
        Color.FromRgb(0x36, 0x9E, 0xE8),
        Color.FromRgb(0x17, 0x70, 0xAF),
        Color.FromRgb(0x18, 0x35, 0x4C),
        Color.FromRgb(0x60, 0xB2, 0xF4),
        Color.FromRgb(0x31, 0xA3, 0x69),
        Color.FromRgb(0x19, 0x36, 0x2A),
        Color.FromRgb(0xFF, 0x9F, 0x43),
        Color.FromRgb(0x3B, 0x2C, 0x1A),
        Color.FromRgb(0xF1, 0x70, 0x67),
        Color.FromRgb(0x3A, 0x20, 0x20),
        Color.FromRgb(0x0A, 0x0F, 0x15),
        Color.FromRgb(0x17, 0x20, 0x2A),
        Color.FromRgb(0x28, 0x8E, 0xD8),
        Color.FromRgb(0xE5, 0xE7, 0xEB),
        Color.FromRgb(0x9C, 0xA3, 0xAF),
        Color.FromRgb(0x2A, 0x34, 0x43),
        Color.FromRgb(0x08, 0x0D, 0x13));

    public static void Apply(ApplicationTheme theme)
    {
        var useDarkPalette = theme switch
        {
            ApplicationTheme.Dark => true,
            ApplicationTheme.Light => false,
            _ => IsSystemDarkTheme()
        };

        var palette = useDarkPalette ? DarkPalette : LightPalette;
        SetBrush("AppBackgroundBrush", palette.AppBackground);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceMutedBrush", palette.SurfaceMuted);
        SetBrush("SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush("InputBackgroundBrush", palette.InputBackground);
        SetBrush("InputBorderBrush", palette.InputBorder);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("TextTertiaryBrush", palette.TextTertiary);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("BorderStrongBrush", palette.BorderStrong);
        SetBrush("DataGridHeaderBrush", palette.DataGridHeader);
        SetBrush("DataGridRowHoverBrush", palette.DataGridRowHover);
        SetBrush("DataGridRowSelectedBrush", palette.DataGridRowSelected);
        SetBrush("SecondaryButtonForegroundBrush", palette.SecondaryButtonForeground);
        SetBrush("SecondaryButtonBorderBrush", palette.SecondaryButtonBorder);
        SetBrush("SecondaryButtonHoverBrush", palette.SecondaryButtonHover);
        SetBrush("ProgressTrackBrush", palette.ProgressTrack);
        SetBrush("MetaTextBrush", palette.MetaText);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentHoverBrush", palette.AccentHover);
        SetBrush("AccentPressedBrush", palette.AccentPressed);
        SetBrush("AccentSoftBrush", palette.AccentSoft);
        SetBrush("FocusBrush", palette.Focus);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("SuccessSoftBrush", palette.SuccessSoft);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("WarningSoftBrush", palette.WarningSoft);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("DangerSoftBrush", palette.DangerSoft);
        SetBrush("SidebarBrush", palette.Sidebar);
        SetBrush("SidebarHoverBrush", palette.SidebarHover);
        SetBrush("SidebarSelectedBrush", palette.SidebarSelected);
        SetBrush("SidebarTextBrush", palette.SidebarText);
        SetBrush("SidebarMutedTextBrush", palette.SidebarMutedText);
        SetBrush("SidebarDividerBrush", palette.SidebarDivider);
        SetBrush("SidebarPanelBrush", palette.SidebarPanel);
    }

    private static bool IsSystemDarkTheme()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void SetBrush(string resourceKey, Color color)
    {
        var resources = Application.Current.Resources;
        if (resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[resourceKey] = new SolidColorBrush(color);
    }
}
