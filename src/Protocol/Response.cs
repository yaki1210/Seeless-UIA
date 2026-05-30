using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeelessUIA.Protocol;

/// <summary>
/// JSON response model sent to the client.
/// </summary>
public class Response
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public static Response Ok(string id, object data)
    {
        return new Response { Id = id, Success = true, Data = data };
    }

    public static Response Fail(string id, string error)
    {
        return new Response { Id = id, Success = false, Error = error };
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _jsonOptions);
    }
}
