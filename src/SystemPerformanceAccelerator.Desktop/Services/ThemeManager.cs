using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public static class ThemeManager
{
    private sealed record ThemePalette(
        IReadOnlyDictionary<string, Color> Brushes);

    private static readonly ThemePalette LightPalette = new(
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = Color.FromRgb(0xF4, 0xF6, 0xF8),
            ["SurfaceBrush"] = Colors.White,
            ["SurfaceMutedBrush"] = Color.FromRgb(0xF3, 0xF5, 0xF7),
            ["SurfaceRaisedBrush"] = Colors.White,
            ["InputBackgroundBrush"] = Colors.White,
            ["InputBorderBrush"] = Color.FromRgb(0xC9, 0xCF, 0xD7),
            ["TextPrimaryBrush"] = Color.FromRgb(0x1C, 0x21, 0x28),
            ["TextSecondaryBrush"] = Color.FromRgb(0x5B, 0x63, 0x6F),
            ["TextTertiaryBrush"] = Color.FromRgb(0x7A, 0x83, 0x8F),
            ["BorderBrush"] = Color.FromRgb(0xDC, 0xE1, 0xE7),
            ["BorderStrongBrush"] = Color.FromRgb(0xB7, 0x95, 0x46),
            ["PremiumDividerBrush"] = Color.FromRgb(0xE6, 0xE9, 0xED),
            ["OverlayBrush"] = Color.FromArgb(0x80, 0x14, 0x18, 0x1E),

            ["DataGridHeaderBrush"] = Color.FromRgb(0xF2, 0xF4, 0xF6),
            ["DataGridRowHoverBrush"] = Color.FromRgb(0xF8, 0xF5, 0xEC),
            ["DataGridRowSelectedBrush"] = Color.FromRgb(0xF3, 0xE7, 0xC4),
            ["SecondaryButtonForegroundBrush"] = Color.FromRgb(0x2B, 0x31, 0x39),
            ["SecondaryButtonBorderBrush"] = Color.FromRgb(0xC8, 0xCE, 0xD6),
            ["SecondaryButtonHoverBrush"] = Color.FromRgb(0xF7, 0xF4, 0xEC),
            ["ProgressTrackBrush"] = Color.FromRgb(0xE5, 0xE8, 0xEC),
            ["MetaTextBrush"] = Color.FromRgb(0x58, 0x60, 0x6B),

            ["TitleBarBrush"] = Color.FromRgb(0xFB, 0xFC, 0xFD),
            ["TitleBarForegroundBrush"] = Color.FromRgb(0x1C, 0x21, 0x28),
            ["TitleBarMutedTextBrush"] = Color.FromRgb(0x64, 0x6C, 0x77),
            ["TitleBarBorderBrush"] = Color.FromRgb(0xE2, 0xE6, 0xEB),
            ["CaptionButtonHoverBrush"] = Color.FromRgb(0xF1, 0xF3, 0xF5),
            ["CaptionButtonPressedBrush"] = Color.FromRgb(0xE5, 0xE8, 0xEC),
            ["CaptionCloseHoverBrush"] = Color.FromRgb(0xC4, 0x2B, 0x1C),
            ["CaptionClosePressedBrush"] = Color.FromRgb(0xA4, 0x26, 0x2C),

            ["AccentBrush"] = Color.FromRgb(0xB7, 0x87, 0x19),
            ["AccentHoverBrush"] = Color.FromRgb(0xCE, 0x9D, 0x2D),
            ["AccentPressedBrush"] = Color.FromRgb(0x8C, 0x67, 0x12),
            ["AccentSoftBrush"] = Color.FromRgb(0xFA, 0xF0, 0xD6),
            ["AccentForegroundBrush"] = Color.FromRgb(0x17, 0x13, 0x0B),
            ["GoldHighlightBrush"] = Color.FromRgb(0xE2, 0xC2, 0x69),
            ["GoldMutedBrush"] = Color.FromRgb(0x9A, 0x78, 0x32),
            ["FocusBrush"] = Color.FromRgb(0xB9, 0x84, 0x16),

            ["SuccessBrush"] = Color.FromRgb(0x10, 0x7C, 0x41),
            ["SuccessSoftBrush"] = Color.FromRgb(0xE8, 0xF5, 0xEE),
            ["WarningBrush"] = Color.FromRgb(0xB8, 0x60, 0x08),
            ["WarningSoftBrush"] = Color.FromRgb(0xFF, 0xF1, 0xD6),
            ["DangerBrush"] = Color.FromRgb(0xC4, 0x2B, 0x1C),
            ["DangerSoftBrush"] = Color.FromRgb(0xFD, 0xEC, 0xEC),
            ["DisabledBrush"] = Color.FromRgb(0xEB, 0xEE, 0xF1),
            ["DisabledForegroundBrush"] = Color.FromRgb(0x72, 0x7A, 0x85),

            ["SidebarBrush"] = Color.FromRgb(0xF8, 0xF9, 0xFB),
            ["SidebarHoverBrush"] = Color.FromRgb(0xF1, 0xF3, 0xF6),
            ["SidebarSelectedBrush"] = Color.FromRgb(0xEE, 0xDD, 0xAE),
            ["SidebarSelectedForegroundBrush"] = Color.FromRgb(0x5B, 0x42, 0x0C),
            ["SidebarTextBrush"] = Color.FromRgb(0x24, 0x2A, 0x32),
            ["SidebarMutedTextBrush"] = Color.FromRgb(0x67, 0x70, 0x7C),
            ["SidebarDividerBrush"] = Color.FromRgb(0xE2, 0xE6, 0xEB),
            ["SidebarPanelBrush"] = Color.FromRgb(0xF2, 0xF4, 0xF7)
        });

    private static readonly ThemePalette DarkPalette = new(
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = Color.FromRgb(0x0A, 0x0B, 0x0D),
            ["SurfaceBrush"] = Color.FromRgb(0x12, 0x14, 0x17),
            ["SurfaceMutedBrush"] = Color.FromRgb(0x18, 0x1B, 0x1F),
            ["SurfaceRaisedBrush"] = Color.FromRgb(0x1B, 0x1F, 0x24),
            ["InputBackgroundBrush"] = Color.FromRgb(0x0E, 0x10, 0x13),
            ["InputBorderBrush"] = Color.FromRgb(0x52, 0x47, 0x2A),
            ["TextPrimaryBrush"] = Color.FromRgb(0xF6, 0xF2, 0xE8),
            ["TextSecondaryBrush"] = Color.FromRgb(0xC4, 0xBD, 0xAF),
            ["TextTertiaryBrush"] = Color.FromRgb(0x93, 0x8D, 0x83),
            ["BorderBrush"] = Color.FromRgb(0x36, 0x32, 0x29),
            ["BorderStrongBrush"] = Color.FromRgb(0x70, 0x5D, 0x31),
            ["PremiumDividerBrush"] = Color.FromRgb(0x40, 0x38, 0x25),
            ["OverlayBrush"] = Color.FromArgb(0xAA, 0x00, 0x00, 0x00),

            ["DataGridHeaderBrush"] = Color.FromRgb(0x18, 0x1B, 0x1F),
            ["DataGridRowHoverBrush"] = Color.FromRgb(0x20, 0x1E, 0x19),
            ["DataGridRowSelectedBrush"] = Color.FromRgb(0x30, 0x29, 0x1A),
            ["SecondaryButtonForegroundBrush"] = Color.FromRgb(0xEE, 0xE8, 0xD9),
            ["SecondaryButtonBorderBrush"] = Color.FromRgb(0x62, 0x53, 0x30),
            ["SecondaryButtonHoverBrush"] = Color.FromRgb(0x20, 0x1E, 0x19),
            ["ProgressTrackBrush"] = Color.FromRgb(0x29, 0x2C, 0x31),
            ["MetaTextBrush"] = Color.FromRgb(0xC1, 0xBA, 0xAD),

            ["TitleBarBrush"] = Color.FromRgb(0x08, 0x09, 0x0B),
            ["TitleBarForegroundBrush"] = Color.FromRgb(0xF6, 0xF2, 0xE8),
            ["TitleBarMutedTextBrush"] = Color.FromRgb(0xB8, 0xB1, 0xA4),
            ["TitleBarBorderBrush"] = Color.FromRgb(0x3D, 0x35, 0x24),
            ["CaptionButtonHoverBrush"] = Color.FromRgb(0x20, 0x1E, 0x19),
            ["CaptionButtonPressedBrush"] = Color.FromRgb(0x31, 0x2A, 0x1B),
            ["CaptionCloseHoverBrush"] = Color.FromRgb(0xC4, 0x2B, 0x1C),
            ["CaptionClosePressedBrush"] = Color.FromRgb(0xA4, 0x26, 0x2C),

            ["AccentBrush"] = Color.FromRgb(0xD3, 0xAE, 0x47),
            ["AccentHoverBrush"] = Color.FromRgb(0xE2, 0xBF, 0x5A),
            ["AccentPressedBrush"] = Color.FromRgb(0xAA, 0x84, 0x27),
            ["AccentSoftBrush"] = Color.FromRgb(0x2C, 0x25, 0x18),
            ["AccentForegroundBrush"] = Color.FromRgb(0x17, 0x13, 0x0B),
            ["GoldHighlightBrush"] = Color.FromRgb(0xE8, 0xCB, 0x74),
            ["GoldMutedBrush"] = Color.FromRgb(0x83, 0x6B, 0x34),
            ["FocusBrush"] = Color.FromRgb(0xE2, 0xC0, 0x68),

            ["SuccessBrush"] = Color.FromRgb(0x42, 0xB8, 0x83),
            ["SuccessSoftBrush"] = Color.FromRgb(0x15, 0x35, 0x2A),
            ["WarningBrush"] = Color.FromRgb(0xF0, 0xA5, 0x43),
            ["WarningSoftBrush"] = Color.FromRgb(0x39, 0x2B, 0x19),
            ["DangerBrush"] = Color.FromRgb(0xED, 0x6A, 0x63),
            ["DangerSoftBrush"] = Color.FromRgb(0x39, 0x20, 0x20),
            ["DisabledBrush"] = Color.FromRgb(0x2B, 0x2E, 0x33),
            ["DisabledForegroundBrush"] = Color.FromRgb(0x8B, 0x87, 0x80),

            ["SidebarBrush"] = Color.FromRgb(0x08, 0x0A, 0x0C),
            ["SidebarHoverBrush"] = Color.FromRgb(0x1C, 0x1B, 0x17),
            ["SidebarSelectedBrush"] = Color.FromRgb(0x37, 0x2E, 0x1B),
            ["SidebarSelectedForegroundBrush"] = Color.FromRgb(0xF2, 0xD5, 0x80),
            ["SidebarTextBrush"] = Color.FromRgb(0xF2, 0xEE, 0xE5),
            ["SidebarMutedTextBrush"] = Color.FromRgb(0xB0, 0xA9, 0x9C),
            ["SidebarDividerBrush"] = Color.FromRgb(0x39, 0x32, 0x22),
            ["SidebarPanelBrush"] = Color.FromRgb(0x10, 0x13, 0x16)
        });

    public static void Apply(ApplicationTheme theme)
    {
        var useDarkPalette = theme switch
        {
            ApplicationTheme.Dark => true,
            ApplicationTheme.Light => false,
            _ => IsSystemDarkTheme()
        };

        var palette = useDarkPalette ? DarkPalette : LightPalette;
        foreach (var entry in palette.Brushes)
        {
            SetBrush(entry.Key, entry.Value);
        }

        SetGradientBrush(
            "AccentGradientBrush",
            palette.Brushes["GoldHighlightBrush"],
            palette.Brushes["AccentBrush"],
            palette.Brushes["AccentPressedBrush"],
            new Point(0, 0),
            new Point(1, 1));
        SetGradientBrush(
            "AccentHoverGradientBrush",
            palette.Brushes["GoldHighlightBrush"],
            palette.Brushes["AccentHoverBrush"],
            palette.Brushes["AccentBrush"],
            new Point(0, 0),
            new Point(1, 1));
        SetGradientBrush(
            "ProgressGradientBrush",
            palette.Brushes["AccentPressedBrush"],
            palette.Brushes["AccentHoverBrush"],
            palette.Brushes["GoldHighlightBrush"],
            new Point(0, 0),
            new Point(1, 0));

        SetBrandImageResources(useDarkPalette);
    }

    private static void SetBrandImageResources(bool useDarkPalette)
    {
        var resources = Application.Current.Resources;
        resources["BrandPhoenixSource"] = resources[
            useDarkPalette ? "BrandPhoenixDarkSource" : "BrandPhoenixLightSource"];
        resources["BrandWordmarkSource"] = resources[
            useDarkPalette ? "BrandWordmarkDarkSource" : "BrandWordmarkLightSource"];
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

    private static void SetGradientBrush(
        string resourceKey,
        Color start,
        Color middle,
        Color end,
        Point startPoint,
        Point endPoint)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = startPoint,
            EndPoint = endPoint
        };
        gradient.GradientStops.Add(new GradientStop(start, 0));
        gradient.GradientStops.Add(new GradientStop(middle, 0.5));
        gradient.GradientStops.Add(new GradientStop(end, 1));
        Application.Current.Resources[resourceKey] = gradient;
    }
}
