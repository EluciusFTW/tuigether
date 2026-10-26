using Marten;

namespace Tuigether.Server.Shared.Masking;

public static class Module
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSharedMasking() =>
            services.ConfigureMarten(static options => PersonalDataMasking.AddRules(options.Events));
    }
}
