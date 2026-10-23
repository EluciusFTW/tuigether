namespace Tuigether.Server.Specs;

[BobcatFeature("Connecting a Client")]
public sealed class ConnectingAClient(ServerFixture server) : ServerSpec(server)
{
    private HubClient? _client;

    [Fact]
    public async Task a_connect_without_the_API_key_is_rejected()
    {
        await WhenActing("a Client connects without the API key", _ => ConnectAsync("", CurrentProtocol));

        await ThenTheConnectionIsClosedWith("401 (Unauthorized)");
    }

    [Fact]
    public async Task a_connect_with_another_protocol_version_tells_the_User_to_update()
    {
        await WhenActing("a Client connects with protocol version 0", _ => ConnectAsync(ServerFixture.ApiKey, "0"));

        await ThenTheConnectIsRejectedWith(ConnectRejection.IncompatibleProtocol, "Update tuigether.");
    }

    [Fact]
    public async Task a_connect_without_a_user_name_is_rejected()
    {
        await WhenActing("a Client connects without a user name", _ => ConnectAsync(ServerFixture.ApiKey, CurrentProtocol, ""));

        await ThenTheConnectIsRejectedWith(ConnectRejection.MissingUserName, "The request has no user name.");
    }

    [Then("the node closes the connection with a reason")]
    internal async Task ThenTheConnectionIsClosedWith(string reason)
    {
        var closed = _client?.Closed ?? Task.FromResult<Exception?>(null);
        var error = await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(10))) == closed ? await closed : null;
        SpecAssert.Fact(
            error?.Message.Contains(reason, StringComparison.Ordinal) == true,
            $"the connection closed with '{error?.Message}', not with '{reason}'");
    }

    [Then("the node rejects the connect with a reason and closes the connection")]
    internal async Task ThenTheConnectIsRejectedWith(ConnectRejection reason, string detail)
    {
        var rejected = _client?.Rejected ?? Task.FromResult<ConnectRejected?>(null);
        var message = await Task.WhenAny(rejected, Task.Delay(TimeSpan.FromSeconds(10))) == rejected ? await rejected : null;
        SpecAssert.Check("reason", message?.Reason, reason);
        SpecAssert.Fact(
            message?.Detail.Contains(detail, StringComparison.Ordinal) == true,
            $"the detail is '{message?.Detail}', not '{detail}'");
        await ThenTheConnectionIsClosedWith(detail);
    }

    private async Task ConnectAsync(string apiKey, string protocolVersion, string userName = "ann") =>
        _client = Own(await HubClient.ConnectAsync(Fixture.Host, apiKey, protocolVersion, userName));
}
