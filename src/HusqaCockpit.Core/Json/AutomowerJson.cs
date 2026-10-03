using System.Text.Json;
using System.Text.Json.Serialization;

namespace HusqaCockpit.Core.Json;

public static class AutomowerJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new TolerantEnumConverterFactory());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
