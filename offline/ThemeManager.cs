using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace SchoolPiBoard.Services;

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

    /// <summary>Фирменный шрифт заголовков и кнопок (Manrope, встроен в программу).</summary>
    public static FontFamily HeadingFont =>
        Application.Current?.Resources["HeadingFont"] as FontFamily ?? new FontFamily("Segoe UI");

    /// <summary>Тема сменилась: окна, собранные кодом, могут перестроиться.</summary>
    public static event Action? ThemeChanged;

    /// <summary>Текущий выбор пользователя: "System", "Light" или "Dark".</summary>
    public static string Preference { get; private set; } = "System";

    /// <summary>
    /// preference — сохранённое значение из настроек ("System" по умолчанию,
    /// если ничего не сохранено или значение не распознано).
    /// </summary>
    public static void Initialize(string preference)
    {
        Preference = IsKnownPreference(preference) ? preference : "System";
        IsDark = ResolveIsDark();
        ApplyPalette();

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General)
                return;

            // При явном выборе «Светлая»/«Тёмная» системную тему больше не
            // слушаем — пользователь попросил конкретный вид, а не «как в Windows».
            if (Preference != "System")
                return;

            var dark = ReadSystemIsDark();
            if (dark == IsDark)
                return;

            IsDark = dark;
            ApplyPalette();

            foreach (var window in Tracked.ToList())
                ApplyTitleBar(window);

            ThemeChanged?.Invoke();
        };
    }

    /// <summary>Меняет тему по явному выбору пользователя в настройках.</summary>
    public static void SetPreference(string preference)
    {
        if (!IsKnownPreference(preference))
            return;

        Preference = preference;
        var dark = ResolveIsDark();
        if (dark == IsDark)
            return;

        IsDark = dark;
        ApplyPalette();

        foreach (var window in Tracked.ToList())
            ApplyTitleBar(window);

        ThemeChanged?.Invoke();
    }

    private static bool IsKnownPreference(string? preference) =>
        preference is "System" or "Light" or "Dark";

    private static bool ResolveIsDark() => Preference switch
    {
        "Light" => false,
        "Dark" => true,
        _ => ReadSystemIsDark()
    };

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
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        var color = (Color)ColorConverter.ConvertFromString(IsDark ? dark : light)!;

        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = color;
            return;
        }

        // Кисть заморожена (или её нет) — менять Color нельзя, и раньше тема
        // молча не применялась. Подменяем ресурс новой кистью: все элементы
        // с DynamicResource подхватят её сами.
        resources[key] = new SolidColorBrush(color);
    }

    /// <summary>Меняет цвета кистей приложения; все элементы обновятся автоматически.</summary>
    public static void ApplyPalette()
    {
        if (Application.Current is null)
            return;

        // Фирменный стиль «Круг и число» — те же токены, что у school-pi.online.
        //        ключ               тёмная       светлая         (токен сайта)
        SetBrush("AppBg", "#FF0B1622", "#FFF3F7F9");             // --background
        SetBrush("AppBg2", "#FF132233", "#FFFFFFFF");            // --surface (поля, списки)
        SetBrush("AppBg3", "#FF1B2E43", "#FFE8EFF4");            // --surface-2
        SetBrush("Surface", "#FF132233", "#FFFFFFFF");           // --surface (панели)
        SetBrush("SurfaceHover", "#FF1B2E43", "#FFE8EFF4");      // --surface-2
        SetBrush("Surface3", "#FF243A52", "#FFDAE5EC");          // --surface-3
        SetBrush("SurfaceActive", "#FF12303D", "#FFE1F2F7");     // --accent-soft
        SetBrush("TextPrimary", "#FFEAF1F7", "#FF0C1E33");       // --text
        SetBrush("TextSecondary", "#FFA3B5C6", "#FF4F6278");     // --text-muted
        SetBrush("BorderBrushColor", "#FF24384D", "#FFDDE5EB");  // --line
        SetBrush("LineStrong", "#FF7088A0", "#FF6F8498");        // --line-strong
        SetBrush("Accent", "#FF5CC3DC", "#FF0A6F8A");            // --accent
        SetBrush("AccentHover", "#FF7FD3E7", "#FF075268");       // --accent-hover
        SetBrush("AccentText", "#FF06202A", "#FFFFFFFF");        // --text-on-accent
        SetBrush("HeaderBg", "#FF132233", "#FF0C1E33");          // --header-bg
        SetBrush("TextOnDark", "#FFF1F6FA", "#FFF1F6FA");        // --text-on-dark
        SetBrush("Amber", "#FFF5B93F", "#FFF5B93F");             // --amber
        SetBrush("AmberStrong", "#FFC98F1E", "#FFC98F1E");       // --amber-strong
        SetBrush("AmberInk", "#FF3B2A00", "#FF3B2A00");          // --amber-ink
        SetBrush("Pi", "#FF1295B3", "#FF1295B3");                // --pi
        SetBrush("PiDeep", "#FF0A6680", "#FF0A6680");            // --pi-deep
        SetBrush("DangerBg", "#FF3B1F1D", "#FFFBE7E5");          // --danger-soft
        SetBrush("DangerText", "#FFF08A7C", "#FFB3261E");        // --danger
        SetBrush("DangerButtonBg", "#FFB3261E", "#FFB3261E");
        SetBrush("DangerButtonHover", "#FF8F1E18", "#FF8F1E18");
        SetBrush("Success", "#FF7FCB9B", "#FF1E7A4C");           // --success
        SetBrush("SuccessSoft", "#FF16352A", "#FFE2F4EA");       // --success-soft
        SetBrush("Warning", "#FFF0BD5A", "#FF8A5A00");           // --warning
        SetBrush("WarningSoft", "#FF3A2E12", "#FFFFF4D9");       // --warning-soft
    }
}
