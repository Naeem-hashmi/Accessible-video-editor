using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Light, dark or follow-Windows theme. Uses the Windows 11 (Fluent) look that comes with .NET, except in
/// High Contrast, where the classic system colours are kept because that is what High Contrast users rely on.
/// The program's own colours ("Ave." keys) point at the theme's colours, so panels, headers, borders and the
/// drawn timeline change with it.
/// </summary>
public static class ThemeService
{
    public const string SystemChoice = "System";
    public const string LightChoice = "Light";
    public const string DarkChoice = "Dark";

    public static IReadOnlyList<string> Choices { get; } = [SystemChoice, LightChoice, DarkChoice];

    /// <summary>True when the dark colours are showing.</summary>
    public static bool IsDark { get; private set; }

    public static bool IsHighContrast => SystemParameters.HighContrast;

    public static event Action? Changed;

    private static string _choice = SystemChoice;
    private static bool _listening;

    public static string Describe(string choice) => choice switch
    {
        LightChoice => "Light",
        DarkChoice => "Dark",
        _ => "Same as Windows",
    };

    public static void Apply(Application app, string? choice)
    {
        _choice = Choices.Contains(choice) ? choice! : SystemChoice;
#pragma warning disable WPF0001 // ThemeMode is marked experimental in .NET 10; it is the supported way to get the Fluent theme.
        if (IsHighContrast)
        {
            app.ThemeMode = ThemeMode.None;
            IsDark = false;
        }
        else
        {
            app.ThemeMode = _choice switch
            {
                LightChoice => ThemeMode.Light,
                DarkChoice => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
            IsDark = _choice == DarkChoice || (_choice == SystemChoice && WindowsAppsAreDark());
        }
#pragma warning restore WPF0001

        SetAliases(app);
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
                {
                    app.Dispatcher.BeginInvoke(() => Apply(app, _choice));
                }
            };
        }

        Changed?.Invoke();
    }

    /// <summary>Windows setting: Personalisation, Colours, "Choose your app mode".</summary>
    public static bool WindowsAppsAreDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SetAliases(Application app)
    {
        Brush Pick(string fluent, Brush classic) =>
            !IsHighContrast && app.TryFindResource(fluent) is Brush brush ? brush : classic;

        var resources = app.Resources;
        resources["Ave.Window"] = Pick("ApplicationBackgroundBrush", SystemColors.WindowBrush);
        resources["Ave.Panel"] = Pick("LayerFillColorDefaultBrush", SystemColors.WindowBrush);
        resources["Ave.Header"] = Pick("SubtleFillColorSecondaryBrush", SystemColors.ControlBrush);
        resources["Ave.Border"] = Pick("CardStrokeColorDefaultBrush", SystemColors.ActiveBorderBrush);
        resources["Ave.Text"] = Pick("TextFillColorPrimaryBrush", SystemColors.WindowTextBrush);
        resources["Ave.TextSecondary"] = Pick("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);
        resources["Ave.Accent"] = Pick("AccentFillColorDefaultBrush", SystemColors.HighlightBrush);
        resources["Ave.Focus"] = IsHighContrast ? SystemColors.HighlightBrush : Pick("AccentFillColorDefaultBrush", SystemColors.HighlightBrush);
    }

    /// <summary>A program colour for drawing code, with a safe fallback.</summary>
    public static Brush Brush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
