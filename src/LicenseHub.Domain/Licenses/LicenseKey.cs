using System.Security.Cryptography;
using System.Text;

namespace LicenseHub.Domain.Licenses;

/// <summary>
/// A license key in the format <c>LH-XXXX-XXXX-XXXX-XXXX</c>.
/// The 16 characters come from the Crockford Base32 alphabet (no I, L, O or U), which gives 80 bits
/// of entropy from a cryptographically secure generator.
/// Only <see cref="ComputeHash"/> may be persisted; the plain key is shown to the customer once.
/// </summary>
public sealed class LicenseKey : IEquatable<LicenseKey>
{
    public const string Prefix = "LH";
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int GroupCount = 4;
    public const int GroupLength = 4;
    public const int HashLength = 32; // SHA-256

    private const string MaskedValue = "LH-****-****-****-****";
    private const int FormattedLength = 2 + (GroupCount * (GroupLength + 1)); // "LH" + 4 x "-XXXX"

    private LicenseKey(string value) => Value = value;

    /// <summary>The plain key. Never log or persist it.</summary>
    public string Value { get; }

    public static LicenseKey Generate()
    {
        var body = RandomNumberGenerator.GetString(Alphabet, GroupCount * GroupLength);
        var builder = new StringBuilder(FormattedLength).Append(Prefix);
        for (var group = 0; group < GroupCount; group++)
        {
            builder.Append('-').Append(body, group * GroupLength, GroupLength);
        }

        return new LicenseKey(builder.ToString());
    }

    /// <summary>
    /// Parses user input leniently: ignores case and whitespace, and maps the characters that
    /// Crockford Base32 treats as typos (O to 0, I and L to 1).
    /// </summary>
    public static bool TryParse(string? input, out LicenseKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var compact = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!char.IsWhiteSpace(c))
            {
                compact.Append(char.ToUpperInvariant(c));
            }
        }

        if (compact.Length != FormattedLength || !compact.ToString(0, Prefix.Length).Equals(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = new StringBuilder(FormattedLength).Append(Prefix);
        for (var i = Prefix.Length; i < compact.Length; i++)
        {
            var c = compact[i];
            var isSeparator = (i - Prefix.Length) % (GroupLength + 1) == 0;

            if (isSeparator)
            {
                if (c != '-')
                {
                    return false;
                }

                normalized.Append(c);
                continue;
            }

            c = c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };

            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }

            normalized.Append(c);
        }

        key = new LicenseKey(normalized.ToString());
        return true;
    }

    /// <summary>SHA-256 of the normalized key. This is the only form stored in the database.</summary>
    public byte[] ComputeHash() => SHA256.HashData(Encoding.ASCII.GetBytes(Value));

    public bool Equals(LicenseKey? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is LicenseKey other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <summary>Masked on purpose so the key does not leak through logs or string interpolation.</summary>
    public override string ToString() => MaskedValue;
}
