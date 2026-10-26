using System.Diagnostics;
using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Tuigether.Server.Specs;

[BobcatFeature("Tracing a Command")]
public sealed class TracingACommand(ServerFixture server) : ServerSpec(server)
{
    [Fact]
    public async Task a_Command_appends_its_events_with_the_trace_id_of_its_request_and_a_causation_id()
    {
        var traceId = ActivityTraceId.CreateRandom();
        CreateSessionResponse? response = null;

        // Bobcat's own act sends an X-Correlation-ID header, which Wolverine prefers to the trace id. A Client sends
        // none, so this request goes past Bobcat. ASP.NET Core continues the trace of the traceparent header.
        await WhenActing(
            "ann creates a Session in a trace",
            async _ =>
            {
                var result = await Fixture.Host.Scenario(scenario =>
                {
                    As("ann")(scenario);
                    scenario.WithRequestHeader("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
                    scenario.Post.Json(new CreateSession("Mob on the timer", "tuigether", "main"), JsonStyle.MinimalApi).ToUrl(CommandRoutes.CreateSession);
                });
                response = await result.ReadAsJsonAsync<CreateSessionResponse>();
            });

        ThenTheActSucceeded();
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var events = await query.Events.FetchStreamAsync(response.ShouldNotBeNull().SessionId, token: TestContext.Current.CancellationToken);
        events.Select(static e => e.CorrelationId).ShouldBe([traceId.ToHexString()]);
        events.ShouldAllBe(static e => e.CausationId != null);
    }
}
