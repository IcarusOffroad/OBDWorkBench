using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace OBDWorkbench.Services;

public static class ThemeService
{
    public static bool UseDark(AppSettingsData settings) => settings.UseLcarsSkin || settings.UseIcarusTheme || (settings.FollowSystemTheme && IsWindowsDark());

    private static bool _lcars;
    public static bool IsLcars => _lcars;
    public static void Apply(Window window, AppSettingsData settings)
    {
        ApplyApplication(UseDark(settings), settings.UseLcarsSkin);
        Apply(window, UseDark(settings));
    }

    public static bool IsWindowsDark()
    {
        object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return value is int i && i == 0;
    }
    public static void Apply(Window window, bool dark)
    {
        ApplyApplication(dark, _lcars);
        ResourceDictionary r = Application.Current.Resources;
        var background = (Brush)r["AppBackgroundBrush"]; var foreground = (Brush)r["PrimaryTextBrush"]; var panel = (Brush)r["ControlBackgroundBrush"];
        window.Background = background; window.Foreground = foreground; window.Resources[SystemColors.WindowBrushKey] = panel; window.Resources[SystemColors.ControlBrushKey] = panel; window.Resources[SystemColors.ControlTextBrushKey] = foreground; window.Resources[SystemColors.WindowTextBrushKey] = foreground;
        ApplyTitleBar(window, dark);
    }

    public static void ApplyApplication(bool dark, bool lcars = false)
    {
        _lcars = lcars;
        ResourceDictionary r = Application.Current.Resources;
        r["AppBackgroundBrush"] = Brush(dark ? "#111313" : "#FAF7F1"); r["PanelBackgroundBrush"] = Brush(dark ? "#191C1C" : "#F0EBE3"); r["ControlBackgroundBrush"] = Brush(dark ? "#202424" : "#FFFFFF");
        r["PrimaryTextBrush"] = Brush(dark ? "#F3EEE3" : "#282520"); r["SecondaryTextBrush"] = Brush(dark ? "#BDB7AD" : "#6C6258"); r["BorderBrush"] = Brush(dark ? "#414644" : "#9A9A9A"); r["ErrorBrush"] = Brush(dark ? "#FF8A80" : "#B00020");
        r["HoverBackgroundBrush"] = Brush(dark ? "#35302B" : "#EDE0D2"); r["PressedBackgroundBrush"] = Brush(dark ? "#493A30" : "#E2C9B3"); r["SelectionBackgroundBrush"] = Brush(dark ? "#594231" : "#E9C8AB");
        r["AccentBrush"] = Brush(lcars ? "#FFBC65" : "#D99A6C");
        r["LcarsPeachBrush"] = Brush(lcars ? "#FFA889" : (dark ? "#191C1C" : "#F0EBE3"));
        r["LcarsLilacBrush"] = Brush(lcars ? "#B7A5EF" : (dark ? "#191C1C" : "#F0EBE3"));
        r["LcarsBlueBrush"] = Brush(lcars ? "#8FAEFF" : (dark ? "#191C1C" : "#F0EBE3"));
        r["LcarsControlTextBrush"] = Brush(lcars ? "#10111C" : (dark ? "#F3EEE3" : "#282520"));
        r["LcarsActionBrush"] = Brush(lcars ? "#63E4A1" : (dark ? "#202424" : "#FFFFFF"));
        r["LcarsFrameVisibility"] = lcars ? Visibility.Visible : Visibility.Collapsed;
        r["LcarsGaugeTileHeight"] = lcars ? 205.0 : 220.0;
        r["LcarsGaugeFaceHeight"] = lcars ? 102.0 : 150.0;
        r["LcarsCornerRadius"] = lcars ? new CornerRadius(18, 18, 4, 4) : new CornerRadius(0);
        r["LcarsButtonRadius"] = lcars ? new CornerRadius(16) : new CornerRadius(0);
        r["LcarsNavRadius"] = lcars ? new CornerRadius(18, 18, 3, 3) : new CornerRadius(0);
        r["LcarsGridHeaderThickness"] = lcars ? new Thickness(0, 0, 0, 3) : new Thickness(0);
        if (lcars)
        {
            r["AppBackgroundBrush"] = Brush("#050609"); r["PanelBackgroundBrush"] = Brush("#0B0C14");
            r["ControlBackgroundBrush"] = Brush("#06070C"); r["PrimaryTextBrush"] = Brush("#F3F0FF");
            r["SecondaryTextBrush"] = Brush("#B7BADE"); r["BorderBrush"] = Brush("#8E88C4");
            r["HoverBackgroundBrush"] = Brush("#433B68"); r["PressedBackgroundBrush"] = Brush("#654B6B");
            r["SelectionBackgroundBrush"] = Brush("#4A3D66"); r["ErrorBrush"] = Brush("#FF8C80");
        }
        var foreground = (Brush)r["PrimaryTextBrush"]; var panel = (Brush)r["ControlBackgroundBrush"];
        r[SystemColors.WindowBrushKey] = panel; r[SystemColors.ControlBrushKey] = panel; r[SystemColors.ControlTextBrushKey] = foreground; r[SystemColors.WindowTextBrushKey] = foreground; r[SystemColors.HighlightTextBrushKey] = foreground;
    }
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private sealed class CaptionState { public bool Dark; public bool Attached; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, CaptionState> Captions = new();
    private static void ApplyTitleBar(Window window, bool dark)
    {
        var state = Captions.GetValue(window, _ => new CaptionState());
        state.Dark = dark;
        void SetAttribute()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int enabled = state.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            // Windows 11 supports explicit caption/text colors; Windows 10 uses the
            // immersive dark-mode attributes above. Unsupported attributes are harmless.
            int caption = state.Dark ? 0x00131311 : unchecked((int)0xFFFFFFFF);
            int text = state.Dark ? 0x00E3EEF3 : unchecked((int)0xFFFFFFFF);
            _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }
        if (!state.Attached)
        {
            state.Attached = true;
            window.SourceInitialized += (_, _) => SetAttribute();
            window.Loaded += (_, _) => SetAttribute();
            window.Activated += (_, _) => SetAttribute();
        }
        SetAttribute();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
