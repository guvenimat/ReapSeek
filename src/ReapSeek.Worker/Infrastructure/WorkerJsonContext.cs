using System.Text.Json.Serialization;

namespace ReapSeek.Worker;

[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class WorkerJsonContext : JsonSerializerContext;
