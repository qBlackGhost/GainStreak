namespace GymApp.Server;

/// <summary>ProblemDetails results carrying the user-facing message in <c>detail</c>, plus the form field it's about if any.</summary>
public static class Problems
{
    public static IResult BadRequest(string detail, string? field = null) => Problem(StatusCodes.Status400BadRequest, detail, field);

    public static IResult Unauthorized(string detail) => Problem(StatusCodes.Status401Unauthorized, detail);

    public static IResult Forbidden(string detail) => Problem(StatusCodes.Status403Forbidden, detail);

    public static IResult NotFound(string detail) => Problem(StatusCodes.Status404NotFound, detail);

    public static IResult Conflict(string detail, string? field = null) => Problem(StatusCodes.Status409Conflict, detail, field);

    private static IResult Problem(int status, string detail, string? field = null) =>
        Results.Problem(detail: detail, statusCode: status,
            extensions: field is null ? null : new Dictionary<string, object?> { ["field"] = field });
}
