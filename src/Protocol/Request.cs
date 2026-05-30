using System.Text.Json.Serialization;

namespace SeelessUIA.Protocol;

/// <summary>
/// JSON request model received from the client.
/// </summary>
public class Request
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("processId")]
    public int? ProcessId { get; set; }

    [JsonPropertyName("hwnd")]
    public long? Hwnd { get; set; }

    [JsonPropertyName("windowRef")]
    public string? WindowRef { get; set; }

    [JsonPropertyName("interactive")]
    public bool? Interactive { get; set; }

    [JsonPropertyName("compact")]
    public bool? Compact { get; set; }

    [JsonPropertyName("depth")]
    public int? Depth { get; set; }

    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("selector")]
    public string? Selector { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("delay")]
    public int? Delay { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("button")]
    public string? Button { get; set; }

    [JsonPropertyName("clickCount")]
    public int? ClickCount { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("amount")]
    public double? Amount { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("dx")]
    public double? Dx { get; set; }

    [JsonPropertyName("dy")]
    public double? Dy { get; set; }

    [JsonPropertyName("attr")]
    public string? Attr { get; set; }

    [JsonPropertyName("full")]
    public bool? Full { get; set; }

    [JsonPropertyName("noClean")]
    public bool? NoClean { get; set; }

    [JsonPropertyName("diff")]
    public bool? Diff { get; set; }

    [JsonPropertyName("searchText")]
    public string? SearchText { get; set; }

    [JsonPropertyName("expandAll")]
    public bool? ExpandAll { get; set; }
}
