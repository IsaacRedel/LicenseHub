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

    /// <summary>A clear Portuguese message for an API error code; the API's own details are in English.</summary>
    public static string Problem(Api.ApiProblem problem, string fallback) => problem.Code switch
    {
        "product.invalid_name" => "Informe um nome de até 100 caracteres.",
        "product.invalid_slug" => "Use só letras minúsculas, números e hífens no slug (ex.: meu-produto).",
        "product.invalid_max_activations" or "license.invalid_max_activations" =>
            "O limite de ativações deve ficar entre 1 e 10.000.",
        "product.slug_taken" => "Já existe um produto com este slug.",
        "license.product_not_found" or "request.invalid_product_id" => "Escolha um produto.",
        "license.invalid_customer_email" => "Informe um e-mail de cliente válido.",
        "license.invalid_expiration" => "A validade precisa ser uma data futura.",
        "license.invalid_revocation_reason" => "Informe o motivo da revogação (até 500 caracteres).",
        "license.not_found" => "A licença não existe mais.",
        _ => fallback,
    };

    /// <summary>
    /// Short id for tables: the last 8 hex characters of a GUID. Ids are version 7, whose first characters are a
    /// timestamp shared by everything created in the same minute; the end is random.
    /// </summary>
    public static string ShortId(Guid? id) => id is { } value ? value.ToString("N")[^8..].ToUpperInvariant() : "—";
}
