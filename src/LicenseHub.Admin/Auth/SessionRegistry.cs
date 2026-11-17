namespace LicenseHub.Admin.Auth;

/// <summary>
/// In-memory record of the panel's sessions, so logging out ends a session everywhere: the circuits of every tab
/// lose the token at once, and the logged-out cookie is refused even if it is presented again.
/// Process-local by design (a single panel instance): after a restart the ended sessions are forgotten, and a copied
/// cookie of one of them would work again until the token expires (at most 30 minutes).
/// </summary>
public sealed class SessionRegistry(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<TokenProvider>> _circuits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _ended = new(StringComparer.Ordinal);

    /// <summary>Tracks an open circuit. False when the session was already logged out.</summary>
    public bool Register(string session, TokenProvider tokens)
    {
        lock (_lock)
        {
            if (IsEndedLocked(session))
            {
                return false;
            }

            if (!_circuits.TryGetValue(session, out var circuits))
            {
                _circuits[session] = circuits = [];
            }

            circuits.Add(tokens);
            return true;
        }
    }

    public void Unregister(string session, TokenProvider tokens)
    {
        lock (_lock)
        {
            if (_circuits.TryGetValue(session, out var circuits) && circuits.Remove(tokens) && circuits.Count == 0)
            {
                _circuits.Remove(session);
            }
        }
    }

    public bool IsEnded(string session)
    {
        lock (_lock)
        {
            return IsEndedLocked(session);
        }
    }

    /// <summary>Logs a session out: its open circuits drop the token, and it stays refused until it would expire.</summary>
    public void End(string session, DateTimeOffset expiresAt)
    {
        TokenProvider[] circuits;
        lock (_lock)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _ended.Where(e => e.Value <= now).Select(e => e.Key).ToList())
            {
                _ended.Remove(expired);
            }

            _ended[session] = expiresAt;
            circuits = _circuits.Remove(session, out var open) ? [.. open] : [];
        }

        foreach (var tokens in circuits)
        {
            tokens.MarkExpired();
        }
    }

    private bool IsEndedLocked(string session) =>
        _ended.TryGetValue(session, out var expiresAt) && expiresAt > time.GetUtcNow();
}
