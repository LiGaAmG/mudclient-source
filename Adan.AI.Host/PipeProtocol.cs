using System.Text.Json.Serialization;

namespace Adan.AI.Host
{
    public class LlmRequest
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 200;
        [JsonPropertyName("cancel")] public bool Cancel { get; set; } = false;
    }

    public class LlmResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; } = false;
    }

    public class HostCommand
    {
        [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
        [JsonPropertyName("model_path")] public string? ModelPath { get; set; }
        [JsonPropertyName("context_size")] public int ContextSize { get; set; } = 4096;
        [JsonPropertyName("threads")] public int Threads { get; set; } = 4;
        [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.6f;
        [JsonPropertyName("top_p")] public float TopP { get; set; } = 0.9f;
        [JsonPropertyName("repeat_penalty")] public float RepeatPenalty { get; set; } = 1.1f;
    }

    public class HostResponse
    {
        [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
