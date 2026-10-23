using Tuigether.Server.Shared.Startup;

namespace Tuigether.Server.Specs;

[BobcatFeature("Starting a node")]
public sealed class StartingANode(ServerFixture server) : CritterStackSpec(server.Host), IAsyncLifetime
{
    [Fact]
    public async Task the_first_node_writes_its_contracts_version()
    {
        await WhenActing("a node starts on an empty database", _ => StartNodeAsync());

        ThenTheActSucceeded();
        await ThenDocument<ContractsVersionDocument>(
            ContractsVersionDocument.TheId,
            new ContractsVersionDocument(ContractsVersionDocument.TheId, ContractsVersion.Current));
    }

    [Fact]
    public async Task a_node_refuses_to_start_on_another_contracts_version()
    {
        await GivenReadModel(new ContractsVersionDocument(ContractsVersionDocument.TheId, "0000000000000000"));

        await WhenActing("a node starts", _ => StartNodeAsync());

        ThenRefusedWith("the database holds version 0000000000000000");
    }

    private async Task StartNodeAsync()
    {
        await using var node = await server.StartNodeAsync();
    }

    public async ValueTask InitializeAsync() => await ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
