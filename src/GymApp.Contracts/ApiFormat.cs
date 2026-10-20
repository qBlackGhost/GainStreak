using System.Text.Json;
using System.Text.Json.Serialization;

namespace GymApp.Contracts;

/// <summary>Wire format shared by the server and the app: JSON with camelCase names, enums as camelCase strings,
/// dates (DateOnly) as yyyy-MM-dd, which is System.Text.Json's default for DateOnly. Request and response records
/// live next to this file.</summary>
public static class ApiFormat
{
    public const string Date = "yyyy-MM-dd";

    /// <summary>Options for a client reading or writing the API.</summary>
    public static JsonSerializerOptions Json { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the API's settings on top of web defaults (camelCase, case-insensitive reads).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
