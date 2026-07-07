using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordReviewReminder.Core;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static readonly JsonSerializerOptions Compact = new(Default)
    {
        WriteIndented = false
    };
}
