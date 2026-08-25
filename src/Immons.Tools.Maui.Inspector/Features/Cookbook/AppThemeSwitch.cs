namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>The app-wide theme override behind the cookbook's ☀︎ / ☾ toggle (Application.UserAppTheme).</summary>
internal static class AppThemeSwitch
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>What the user forced: "light", "dark" or "system" (following the OS).</summary>
    public static string Current => Application.Current?.UserAppTheme switch
    {
        AppTheme.Light => Light,
        AppTheme.Dark => Dark,
        _ => System,
    };

    /// <summary>The theme actually in effect right now.</summary>
    public static string Effective => Application.Current?.RequestedTheme == AppTheme.Dark ? Dark : Light;

    public static bool Set(string theme)
    {
        if (Application.Current is not { } app)
            return false;
        switch (theme)
        {
            case Light:
                app.UserAppTheme = AppTheme.Light;
                return true;
            case Dark:
                app.UserAppTheme = AppTheme.Dark;
                return true;
            case System:
                app.UserAppTheme = AppTheme.Unspecified;
                return true;
            default:
                return false;
        }
    }
}
