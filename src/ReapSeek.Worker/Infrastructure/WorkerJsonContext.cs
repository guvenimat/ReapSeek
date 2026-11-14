using System.Text.Json.Serialization;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;
namespace ReapSeek.Worker;

[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class WorkerJsonContext : JsonSerializerContext;
