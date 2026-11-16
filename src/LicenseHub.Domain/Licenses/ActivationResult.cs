using LicenseHub.Domain.Validation;

namespace LicenseHub.Domain.Licenses;

/// <summary>Outcome of <see cref="License.Activate"/>.</summary>
public sealed class ActivationResult
{
    private ActivationResult(ValidationOutcome outcome, Activation? activation, bool isNewActivation)
    {
        Outcome = outcome;
        Activation = activation;
        IsNewActivation = isNewActivation;
    }

    public ValidationOutcome Outcome { get; }

    /// <summary>The activation used by the instance; null when the validation was rejected.</summary>
    public Activation? Activation { get; }

    /// <summary>True when <see cref="Activation"/> was just created and still has to be persisted.</summary>
    public bool IsNewActivation { get; }

    public bool IsValid => Outcome == ValidationOutcome.Valid;

    internal static ActivationResult Accepted(Activation activation, bool isNew) =>
        new(ValidationOutcome.Valid, activation, isNew);

    internal static ActivationResult Rejected(ValidationOutcome outcome) => new(outcome, null, false);
}
