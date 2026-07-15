using System.Runtime.Serialization;

namespace Adan.Client.Plugins.AI.Inference
{
    [DataContract]
    public class LlmRequest
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "prompt")] public string Prompt { get; set; }
        [DataMember(Name = "max_tokens")] public int MaxTokens { get; set; }
        [DataMember(Name = "cancel")] public bool Cancel { get; set; }
    }

    [DataContract]
    public class LlmResponse
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "done")] public bool Done { get; set; }
    }

    [DataContract]
    public class HostCommand
    {
        [DataMember(Name = "cmd")] public string Cmd { get; set; }
        [DataMember(Name = "model_path")] public string ModelPath { get; set; }
        [DataMember(Name = "context_size")] public int ContextSize { get; set; }
        [DataMember(Name = "threads")] public int Threads { get; set; }
        [DataMember(Name = "temperature")] public float Temperature { get; set; }
        [DataMember(Name = "top_p")] public float TopP { get; set; }
        [DataMember(Name = "repeat_penalty")] public float RepeatPenalty { get; set; }
    }

    [DataContract]
    public class HostResponse
    {
        [DataMember(Name = "cmd")] public string Cmd { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
    }
}
