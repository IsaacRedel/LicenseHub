namespace LicenseHub.Admin.Dashboard;

/// <summary>Texts shown for API values. Statuses always pair a symbol with text; color never stands alone.</summary>
public static class Labels
{
    public const string ValidOutcome = "valid";

    /// <summary>Validation outcome in caps, e.g. "VÁLIDA" or "CHAVE INEXISTENTE".</summary>
    public static string Outcome(string outcome) => outcome switch
    {
        ValidOutcome => "VÁLIDA",
        "unknownKey" => "CHAVE INEXISTENTE",
        "productMismatch" => "PRODUTO ERRADO",
        "revoked" => "REVOGADA",
        "expired" => "EXPIRADA",
        "activationLimitReached" => "LIMITE DE ATIVAÇÕES",
        _ => outcome.ToUpperInvariant(),
    };

    /// <summary>License status in caps.</summary>
    public static string LicenseStatus(string status) => status switch
    {
        "active" => "ATIVA",
        "revoked" => "REVOGADA",
        "expired" => "EXPIRADA",
        _ => status.ToUpperInvariant(),
    };

    /// <summary>Short id for tables: the first 8 characters of a GUID.</summary>
    public static string ShortId(Guid? id) => id is { } value ? value.ToString()[..8].ToUpperInvariant() : "—";
}
