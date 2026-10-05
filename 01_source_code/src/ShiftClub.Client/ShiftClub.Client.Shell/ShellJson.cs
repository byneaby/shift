using System.Text.Json;
using System.Text.Json.Serialization;
using ShiftClub.Shared.Json;

namespace ShiftClub.Client.Shell;

internal static class ShellJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        // Числа и строки для enum (совместимость с любым ответом API).
        o.Converters.Add(new FlexibleJsonEnumConverterFactory());
        return o;
    }
}
