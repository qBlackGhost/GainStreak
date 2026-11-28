namespace GymApp.Server;

/// <summary>ProblemDetails results carrying the user-facing message in <c>detail</c>, plus the form field it's about if any.</summary>
public static class Problems
{
    public static IResult BadRequest(string detail, string? field = null) => Problem(StatusCodes.Status400BadRequest, detail, field);

    public static IResult Unauthorized(string detail) => Problem(StatusCodes.Status401Unauthorized, detail);

    public static IResult Forbidden(string detail) => Problem(StatusCodes.Status403Forbidden, detail);

    public static IResult NotFound(string detail) => Problem(StatusCodes.Status404NotFound, detail);

    public static IResult Conflict(string detail, string? field = null) => Problem(StatusCodes.Status409Conflict, detail, field);

    /// <summary>A 429 that says why and until when, unlike the rate limiter's (which has no body).</summary>
    public static IResult TooManyRequests(string detail, string? field = null, IDictionary<string, object?>? extensions = null) =>
        Problem(StatusCodes.Status429TooManyRequests, detail, field, extensions);

    private static IResult Problem(int status, string detail, string? field = null, IDictionary<string, object?>? extensions = null)
    {
        Dictionary<string, object?>? all = null;
        if (field is not null || extensions is not null)
        {
            all = extensions is null ? [] : new Dictionary<string, object?>(extensions);
            if (field is not null) all["field"] = field;
        }
        return Results.Problem(detail: detail, statusCode: status, extensions: all);
    }
}
