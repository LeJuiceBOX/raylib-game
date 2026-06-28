using System.Text.Json;

public static class Json
{
    public static Dictionary<string, object?> Deserialize(string json)
    {
        var document = JsonDocument.Parse(json);
        return ParseObject(document.RootElement);
    }

    private static Dictionary<string, object?> ParseObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
            dict[property.Name] = ParseValue(property.Value);

        return dict;
    }

    private static object? ParseValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ParseObject(element),
        JsonValueKind.Array  => element.EnumerateArray().Select(ParseValue).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _                    => throw new JsonException($"Unsupported token: {element.ValueKind}")
    };
}