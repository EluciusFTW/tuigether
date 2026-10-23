using Alba;
using Tuigether.Server.Shared;

namespace Tuigether.Server.Specs;

[BobcatFeature("Checking each request")]
public sealed class CheckingEachRequest(ServerFixture server) : ServerSpec(server)
{
    private static readonly CreateSession Command = new("Mob on the timer", "tuigether", "main");

    [Fact]
    public async Task a_request_with_a_wrong_API_key_is_not_authorized()
    {
        await WhenPosted(Command, Headers("wrong-api-key", CurrentProtocol, "ann"));

        ThenResponseIs(401);
        ThenRefusedWith("The API key is wrong.");
        ThenNoEvents();
    }

    [Fact]
    public async Task a_request_with_another_protocol_version_tells_the_User_to_update()
    {
        await WhenPosted(Command, Headers(ServerFixture.ApiKey, "0", "ann"));

        ThenResponseIs(400);
        ThenRefusedWith("Update tuigether.");
        ThenNoEvents();
    }

    [Fact]
    public async Task a_request_without_a_user_name_is_rejected()
    {
        await WhenPosted(Command, Headers(ServerFixture.ApiKey, CurrentProtocol, ""));

        ThenResponseIs(400);
        ThenRefusedWith("The request has no user name.");
        ThenNoEvents();
    }

    [Fact]
    public async Task the_health_endpoint_needs_no_API_key()
    {
        // The health endpoint answers in plain text, so this act does not use WhenReading, which reads JSON.
        await WhenActing(
            "the health endpoint is read without headers",
            _ => AlbaHost.Scenario(static scenario =>
            {
                scenario.Get.Url(ServerRoutes.Health);
                scenario.StatusCodeShouldBeOk();
                scenario.ContentShouldBe("Healthy");
            }));

        ThenTheActSucceeded();
    }
}
