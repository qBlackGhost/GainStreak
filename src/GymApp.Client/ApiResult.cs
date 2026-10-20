namespace GymApp.Client;

public enum ApiOutcome
{
    /// <summary>The server did it.</summary>
    Ok,
    /// <summary>The server said no: a 4xx, with its message.</summary>
    Refused,
    /// <summary>The server couldn't be reached: no connection, DNS failure, timeout, or a 5xx.</summary>
    Offline
}

/// <summary>The result of a call with no value. See <see cref="ApiResult{T}"/> for calls that return one.</summary>
public class ApiResult
{
    public const string OfflineMessage = "You're offline. Try again when you're connected.";
    public const string TooManyMessage = "Too many tries. Wait a minute and try again.";
    public const string FallbackMessage = "Something went wrong. Try again.";

    protected ApiResult(ApiOutcome outcome, int? status, string? detail, string? field)
    {
        Outcome = outcome;
        Status = status;
        Detail = detail;
        Field = field;
    }

    public ApiOutcome Outcome { get; }
    public bool IsOk => Outcome == ApiOutcome.Ok;
    public bool IsRefused => Outcome == ApiOutcome.Refused;
    public bool IsOffline => Outcome == ApiOutcome.Offline;

    /// <summary>The HTTP status of a refusal (or of an Ok); null when offline.</summary>
    public int? Status { get; }

    /// <summary>The ProblemDetails <c>detail</c> of a refusal: the message to show.</summary>
    public string? Detail { get; }

    /// <summary>The form field a refusal is about (<see cref="Contracts.ProblemFields"/>), if any.</summary>
    public string? Field { get; }

    /// <summary>What to show the user, or null on success.</summary>
    public string? Error => Outcome switch
    {
        ApiOutcome.Offline => OfflineMessage,
        ApiOutcome.Refused => Detail ?? (Status == 429 ? TooManyMessage : FallbackMessage),
        _ => null
    };

    public static ApiResult Ok(int status = 200) => new(ApiOutcome.Ok, status, null, null);
    public static ApiResult Refused(int status, string? detail, string? field = null) => new(ApiOutcome.Refused, status, detail, field);
    public static ApiResult Offline() => new(ApiOutcome.Offline, null, null, null);
}

/// <summary>The result of a call that returns a value; <see cref="Value"/> is set only when <see cref="ApiResult.IsOk"/>.</summary>
public sealed class ApiResult<T> : ApiResult
{
    private ApiResult(ApiOutcome outcome, T? value, int? status, string? detail, string? field)
        : base(outcome, status, detail, field) => Value = value;

    public T? Value { get; }

    public static ApiResult<T> Ok(T value, int status = 200) => new(ApiOutcome.Ok, value, status, null, null);
    public static new ApiResult<T> Refused(int status, string? detail, string? field = null) => new(ApiOutcome.Refused, default, status, detail, field);
    public static new ApiResult<T> Offline() => new(ApiOutcome.Offline, default, null, null, null);

    /// <summary>A failed result as one with another value type, for passing it on.</summary>
    internal static ApiResult<T> From(ApiResult failed) => failed.Outcome switch
    {
        ApiOutcome.Refused => Refused(failed.Status!.Value, failed.Detail, failed.Field),
        ApiOutcome.Offline => Offline(),
        _ => throw new ArgumentException("Only a failed result can be passed on.", nameof(failed))
    };
}
