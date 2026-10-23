using Marten;
using JasperFx;

namespace Tuigether.Server.Startup;

/// <summary>
/// Compares the contracts version of this node with the version in the database. It runs in
/// <see cref="StartingAsync"/>, so it runs before Wolverine starts to handle messages.
/// </summary>
public sealed class ContractsVersionCheck(IDocumentStore store) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var stored = await LoadOrInsertAsync(ContractsVersion.Current, cancellationToken);

        // The host refuses to start when a hosted service throws at startup.
        if (ContractsVersionRules.Check(stored.Version, ContractsVersion.Current) is Result<string, string>.Error { Value: var reason })
        {
            throw new InvalidOperationException(reason);
        }
    }

    private async Task<ContractsVersionDocument> LoadOrInsertAsync(string current, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        if (await session.LoadAsync<ContractsVersionDocument>(ContractsVersionDocument.TheId, cancellationToken) is { } stored)
        {
            return stored;
        }

        var inserted = new ContractsVersionDocument(ContractsVersionDocument.TheId, current);
        session.Insert(inserted);
        try
        {
            await session.SaveChangesAsync(cancellationToken);
            return inserted;
        }
        catch (DocumentAlreadyExistsException)
        {
            // Another node inserted the document after the load above.
            await using var reload = store.QuerySession();
            return await reload.LoadAsync<ContractsVersionDocument>(ContractsVersionDocument.TheId, cancellationToken) ?? inserted;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
