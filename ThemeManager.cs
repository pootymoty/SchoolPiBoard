using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Whiteboard.Services;

/// <summary>
/// Подстраивает палитру приложения и заголовок окна под текущую тему Windows
/// и следит за её переключением на лету.
/// </summary>
public static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static readonly List<Window> Tracked = new();

    public static bool IsDark { get; private set; } = true;

    public static void Initialize()
    {
        IsDark = ReadSystemIsDark();
        ApplyPalette();

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General)
                return;

            var dark = ReadSystemIsDark();
            if (dark == IsDark)
                return;

            IsDark = dark;
            ApplyPalette();

            foreach (var window in Tracked.ToList())
                ApplyTitleBar(window);
        };
    }

    /// <summary>Читает системную настройку «тёмный режим для приложений».</summary>
    private static bool ReadSystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
                return intValue == 0;
        }
        catch
        {
            // Ключа может не быть на старых системах — считаем тему тёмной.
        }
        return true;
    }

    /// <summary>Окно будет перекрашиваться вместе с системной темой.</summary>
    public static void Track(Window window)
    {
        if (!Tracked.Contains(window))
            Tracked.Add(window);

        window.Closed += (_, _) => Tracked.Remove(window);
        ApplyTitleBar(window);
    }

    /// <summary>Перекрашивает стандартный заголовок окна в тёмный или светлый.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var useDark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch
        {
            // На старых сборках Windows атрибут недоступен — не критично.
        }
    }

    private static void SetBrush(string key, string dark, string light)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = (Color)ColorConverter.ConvertFromString(IsDark ? dark : light)!;
    }

    /// <summary>Меняет цвета кистей приложения; все элементы обновятся автоматически.</summary>
    public static void ApplyPalette()
    {
        if (Application.Current is null)
            return;

        //        ключ              тёмная       светлая
        SetBrush("AppBg", "#FF1B1B1F", "#FFF3F3F6");
        SetBrush("AppBg2", "#FF26262C", "#FFFFFFFF");
        SetBrush("AppBg3", "#FF32323A", "#FFE8E8EE");
        SetBrush("Surface", "#FF26262C", "#FFFFFFFF");
        SetBrush("SurfaceHover", "#FF34343D", "#FFEDEDF2");
        SetBrush("SurfaceActive", "#FF3D3F5C", "#FFE0E2FF");
        SetBrush("TextPrimary", "#FFECECF1", "#FF1F1F26");
        SetBrush("TextSecondary", "#FF9B9BAA", "#FF6A6A78");
        SetBrush("BorderBrushColor", "#FF3A3A44", "#FFDDDDE5");
        SetBrush("DangerBg", "#FF5A2B2B", "#FFFFE0E0");
        SetBrush("DangerText", "#FFFF8A8A", "#FFC62828");
        SetBrush("DangerButtonBg", "#FFC62828", "#FFC62828");
        SetBrush("DangerButtonHover", "#FFA91B1B", "#FFA91B1B");
    }
}
