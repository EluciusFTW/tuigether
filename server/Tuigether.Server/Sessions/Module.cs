using Tuigether.Server.Sessions.DataView;
using Tuigether.Server.Sessions.Delete;
using Tuigether.Server.Sessions.ListView;

namespace Tuigether.Server.Sessions;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSessions() => services.AddSessionsListView().AddSessionsDataView().AddSessionsDelete();
    }
}
