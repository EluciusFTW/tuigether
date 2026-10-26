using Wolverine;

namespace Tuigether.Server.Sessions.Delete;

public static class Module
{
    extension(IServiceCollection services)
    {
        // A durable queue keeps the masking of a deleted Session across a node restart.
        public IServiceCollection AddSessionsDelete() =>
            services.ConfigureWolverine(static options =>
                options.PublishMessage<MaskDeletedSession>().ToLocalQueue("masking").UseDurableInbox());
    }
}
