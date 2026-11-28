using System;
using System.Collections.Generic;
using GymApp.Contracts;

namespace GymApp.Services;

public enum CooldownKind { Feedback, Report }

/// <summary>
/// The 10-minute waits after sending feedback and after reporting a user (0.28.1), as far as this phone knows: set
/// after a send, or from the server's 429 (<see cref="ProblemExtensions.RetryAt"/>), which is what really counts, so a
/// reinstall or a second phone still waits. Kept in memory per signed-in user; the screens grey out Send until then.
/// </summary>
public sealed class SendCooldowns(SessionService session, TimeProvider? time = null)
{
    private readonly Dictionary<(int User, CooldownKind Kind), DateTime> _until = new();
    private DateTime UtcNow => (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public static TimeSpan Length(CooldownKind kind) =>
        kind == CooldownKind.Feedback ? FeedbackRules.Cooldown : UserReportRules.Cooldown;

    /// <summary>When the signed-in user can send again, UTC, or null when they can now.</summary>
    public DateTime? Until(CooldownKind kind)
    {
        if (session.CurrentUser?.Id is not { } me) return null;
        lock (_until)
            return _until.TryGetValue((me, kind), out var until) && until > UtcNow ? until : null;
    }

    /// <summary>A send went through: the wait starts now.</summary>
    public void Sent(CooldownKind kind) => Set(kind, UtcNow + Length(kind));

    /// <summary>The server said to wait until <paramref name="retryAtUtc"/>.</summary>
    public void Refused(CooldownKind kind, DateTime retryAtUtc) => Set(kind, retryAtUtc);

    private void Set(CooldownKind kind, DateTime untilUtc)
    {
        if (session.CurrentUser?.Id is not { } me) return;
        lock (_until) _until[(me, kind)] = untilUtc;
    }
}
