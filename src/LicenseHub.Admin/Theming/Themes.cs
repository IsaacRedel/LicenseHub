namespace LicenseHub.Admin.Theming;

public static class Themes
{
    public const string CookieName = "lh-theme";
    public const string Arquivo = "arquivo";
    public const string Tatico = "tatico";

    /// <summary>The theme saved in the lh-theme cookie, or null to let the CSS follow the system preference.</summary>
    public static string? FromCookie(string? value) => value is Arquivo or Tatico ? value : null;

    public static string Label(string theme) => theme == Tatico ? "TEMA: TÁTICO" : "TEMA: ARQUIVO";

    public static string SwitchLabel(string theme) =>
        theme == Tatico ? "Trocar para o tema Arquivo" : "Trocar para o tema Tático";
}
