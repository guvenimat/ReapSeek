using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Engagement.Contracts;
using ReapSeek.Identity.Contracts;
using ReapSeek.Marketing.Contracts;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Engagement.Wishlist;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Infrastructure;

namespace ReapSeek.UnitTests;

public sealed record TestPingV1(long Id);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TestPingV1))]
public sealed partial class TestEventsJsonContext : JsonSerializerContext;

public sealed class RecordingPingHandler : IInboxHandler<TestPingV1>
{
    public List<(TestPingV1 Message, long InboxId)> Seen { get; } = [];

    public Task HandleAsync(TestPingV1 message, InboxMessage envelope, CancellationToken ct)
    {
        Seen.Add((message, envelope.Id));
        return Task.CompletedTask;
    }
}

public class MessagingTests
{
    const string Ping = "catalog.ping.v1";

    static Subscriptions Registry() => new Subscriptions()
        .Add<TestPingV1, RecordingPingHandler>(MessagingModules.Catalog, Ping, TestEventsJsonContext.Default.TestPingV1, MessagingModules.Ordering, "count-pings")
        .Add<TestPingV1, RecordingPingHandler>(MessagingModules.Catalog, Ping, TestEventsJsonContext.Default.TestPingV1, MessagingModules.Finance, "count-pings", 3);

    static InboxMessage Message(long id, string payload) => new(id, "catalog", 7, Ping, "count-pings", payload, 1, DateTime.UnixEpoch, null);

    [Fact]
    public void Subscriptions_fan_out_one_event_to_every_subscriber()
    {
        var registry = Registry();
        Assert.Equal(2, registry.Of(MessagingModules.Catalog, Ping).Count);
        Assert.Empty(registry.Of(MessagingModules.Catalog, "catalog.other.v1"));
        Assert.Empty(registry.Of(MessagingModules.Ordering, Ping));
        Assert.Equal([MessagingModules.Ordering, MessagingModules.Finance], registry.Subscribers);
        Assert.Equal(3, registry.Find(MessagingModules.Finance, "count-pings")!.MaxAttempts);
        Assert.Equal(RetryPolicy.MaxAttempts, registry.Find(MessagingModules.Ordering, "count-pings")!.MaxAttempts);
        Assert.Null(registry.Find(MessagingModules.Marketing, "count-pings"));
    }

    [Fact]
    public void A_handler_name_is_registered_once_per_module()
    {
        var registry = Registry();
        Assert.Throws<InvalidOperationException>(() =>
            registry.Add<TestPingV1, RecordingPingHandler>(MessagingModules.Catalog, Ping, TestEventsJsonContext.Default.TestPingV1, MessagingModules.Ordering, "count-pings"));
    }

    [Theory]
    [InlineData("catalog.ping.v1", "Bad Name")]
    [InlineData("catalog.ping.v1", "")]
    [InlineData("catalog.ping", "count")]
    [InlineData("ordering.ping.v1", "count")]
    [InlineData("Catalog.Ping.V1", "count")]
    public void Event_and_handler_names_are_validated(string eventType, string handler) =>
        Assert.Throws<ArgumentException>(() => new Subscriptions()
            .Add<TestPingV1, RecordingPingHandler>(MessagingModules.Catalog, eventType, TestEventsJsonContext.Default.TestPingV1, MessagingModules.Ordering, handler));

    [Fact]
    public async Task A_subscription_deserializes_the_payload_and_calls_its_handler()
    {
        var handler = new RecordingPingHandler();
        var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        var subscription = Registry().Find(MessagingModules.Ordering, "count-pings")!;
        await subscription.InvokeAsync(services, Message(5_000_000_000_000_001, """{"id":42}"""), CancellationToken.None);
        Assert.Equal([(new TestPingV1(42), 5_000_000_000_000_001L)], handler.Seen);
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.InvokeAsync(services, Message(2, "null"), CancellationToken.None));
    }

    [Fact]
    public void Inbox_retries_use_the_shared_policy_with_the_subscription_limit()
    {
        var now = DateTime.UnixEpoch;
        var finance = Registry().Find(MessagingModules.Finance, "count-pings")!;
        Assert.Equal(now.AddSeconds(5), RetryPolicy.RetryAt(1, now, finance.MaxAttempts));
        Assert.Equal(now.AddSeconds(10), RetryPolicy.RetryAt(2, now, finance.MaxAttempts));
        Assert.Null(RetryPolicy.RetryAt(3, now, finance.MaxAttempts));
        Assert.Equal(now.AddSeconds(1280), RetryPolicy.RetryAt(RetryPolicy.MaxAttempts - 1, now));
    }

    [Fact]
    public void Failed_jobs_are_found_by_id_in_the_platform_outbox_or_a_module_inbox()
    {
        Assert.Equal(JobTables.Platform, JobTables.Of(12));
        Assert.Equal("engagement.inbox", JobTables.Of(MessagingModules.Engagement.FirstInboxId + 9).Table);
        Assert.Equal("('engagement/' || handler_name)", JobTables.Of(MessagingModules.Engagement.FirstInboxId).TypeColumn);
        Assert.Equal(1 + MessagingModules.All.Count, JobTables.All.Count);
        Assert.Equal(JobTables.All.Count - 1, JobTables.Union.Split(" UNION ALL ").Length - 1);
        Assert.Equal("engagement/question-asked-seller-email", MessagingModules.Engagement.JobType(QuestionHandler.AskedHandler));
        Assert.Throws<ArgumentException>(() => MessagingModules.Get("platform"));
    }

    [Fact]
    public void The_worker_subscribes_only_to_known_integration_events()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Username=test;Database=test";
        builder.AddReapSeekWorker();
        var registry = (Subscriptions)builder.Services.Single(d => d.ServiceType == typeof(Subscriptions)).ImplementationInstance!;
        var question = Assert.Single(registry.Of(MessagingModules.Engagement, QuestionAskedV1.EventName));
        Assert.Equal((MessagingModules.Engagement, QuestionHandler.AskedHandler), (question.Subscriber, question.Handler));
        (MessagingModule Module, string Event, string Handler)[] expected =
        [
            (MessagingModules.Engagement, QuestionAskedV1.EventName, QuestionHandler.AskedHandler),
            (MessagingModules.Engagement, QuestionAnsweredV1.EventName, QuestionHandler.AnsweredHandler),
            (MessagingModules.Engagement, PriceDroppedV1.EventName, PriceDropHandler.NotifyHandler),
            (MessagingModules.Marketing, CampaignSendV1.EventName, CampaignHandler.SendHandler),
            (MessagingModules.Marketing, CouponIssuedV1.EventName, CampaignHandler.CouponHandler),
            (MessagingModules.Marketing, AlertReceivedV1.EventName, AlertMailHandler.NotifyHandler),
            (MessagingModules.Identity, UserRegisteredV1.EventName, AccountEmailHandler.WelcomeHandler),
            (MessagingModules.Identity, AccountEmailV1.EventName, AccountEmailHandler.AccountHandler),
        ];
        foreach (var (module, eventName, handler) in expected)
        {
            var subscription = Assert.Single(registry.Of(module, eventName));
            Assert.Equal((module, handler, RetryPolicy.MaxAttempts), (subscription.Subscriber, subscription.Handler, subscription.MaxAttempts));
        }
        Assert.Equal(expected.Length, registry.All.Count);
        var known = ArchitectureTests.IntegrationEvents().Select(e => e.Name).ToHashSet();
        Assert.All(registry.All, s => Assert.Contains(s.EventType, known));
        Assert.Equal(registry.Subscribers.Count, builder.Services.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory is not null));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MessageRelay));
    }
}
