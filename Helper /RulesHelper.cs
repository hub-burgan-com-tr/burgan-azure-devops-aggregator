using System.Text.Json;

namespace BurganAzureDevopsAggregator.Helpers
{
    public  class RulesHelper
    {
        public  string GetField(Dictionary<string, object> fields, string key)
        {
            if (fields != null && fields.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
            return null;
        }
    public  object ExtractJsonValue(JsonElement jsonElement)
{
    return jsonElement.ValueKind switch
    {
        JsonValueKind.String => jsonElement.GetString(),
        JsonValueKind.Number => jsonElement.TryGetInt64(out var l) ? l : jsonElement.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => jsonElement.ToString() // fallback olarak JSON string'i
    };
}

    }
}
