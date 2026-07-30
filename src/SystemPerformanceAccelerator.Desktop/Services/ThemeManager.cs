using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Models;
using System.IO;

namespace SystemPerformanceAccelerator.Desktop.Services;

public static class ThemeManager
{
    private sealed record ThemePalette(
        Color AppBackground,
        Color Surface,
        Color SurfaceMuted,
        Color InputBackground,
        Color InputBorder,
        Color TextPrimary,
        Color TextSecondary,
        Color Border,
        Color DataGridHeader,
        Color DataGridRowHover,
        Color DataGridRowSelected,
        Color SecondaryButtonForeground,
        Color SecondaryButtonBorder,
        Color SecondaryButtonHover,
        Color ProgressTrack,
        Color MetaText);

    private static readonly ThemePalette LightPalette = new(
        Color.FromRgb(0xEE, 0xF2, 0xF6),
        Colors.White,
        Color.FromRgb(0xF7, 0xF9, 0xFB),
        Colors.White,
        Color.FromRgb(0xC8, 0xD1, 0xDC),
        Color.FromRgb(0x17, 0x20, 0x33),
        Color.FromRgb(0x68, 0x73, 0x86),
        Color.FromRgb(0xDD, 0xE3, 0xEA),
        Color.FromRgb(0xF4, 0xF6, 0xF8),
        Color.FromRgb(0xF5, 0xF9, 0xFD),
        Color.FromRgb(0xE8, 0xF2, 0xFC),
        Color.FromRgb(0x48, 0x55, 0x68),
        Color.FromRgb(0xCB, 0xD3, 0xDD),
        Color.FromRgb(0xF1, 0xF4, 0xF7),
        Color.FromRgb(0xE7, 0xEC, 0xF2),
        Color.FromRgb(0x4D, 0x5B, 0x6E));

    private static readonly ThemePalette DarkPalette = new(
        Color.FromRgb(0x11, 0x18, 0x27),
        Color.FromRgb(0x1F, 0x29, 0x37),
        Color.FromRgb(0x27, 0x32, 0x44),
        Color.FromRgb(0x18, 0x22, 0x31),
        Color.FromRgb(0x44, 0x50, 0x64),
        Color.FromRgb(0xF3, 0xF6, 0xFA),
        Color.FromRgb(0xAA, 0xB6, 0xC5),
        Color.FromRgb(0x3A, 0x46, 0x58),
        Color.FromRgb(0x27, 0x32, 0x44),
        Color.FromRgb(0x25, 0x36, 0x4A),
        Color.FromRgb(0x24, 0x47, 0x65),
        Color.FromRgb(0xD9, 0xE2, 0xEC),
        Color.FromRgb(0x4A, 0x57, 0x69),
        Color.FromRgb(0x2C, 0x38, 0x49),
        Color.FromRgb(0x35, 0x41, 0x52),
        Color.FromRgb(0xC4, 0xCE, 0xDB));

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
        SetBrush("InputBackgroundBrush", palette.InputBackground);
        SetBrush("InputBorderBrush", palette.InputBorder);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("DataGridHeaderBrush", palette.DataGridHeader);
        SetBrush("DataGridRowHoverBrush", palette.DataGridRowHover);
        SetBrush("DataGridRowSelectedBrush", palette.DataGridRowSelected);
        SetBrush("SecondaryButtonForegroundBrush", palette.SecondaryButtonForeground);
        SetBrush("SecondaryButtonBorderBrush", palette.SecondaryButtonBorder);
        SetBrush("SecondaryButtonHoverBrush", palette.SecondaryButtonHover);
        SetBrush("ProgressTrackBrush", palette.ProgressTrack);
        SetBrush("MetaTextBrush", palette.MetaText);
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
