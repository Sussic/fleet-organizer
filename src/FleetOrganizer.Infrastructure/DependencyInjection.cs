using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using FleetOrganizer.Infrastructure.Esi;
using FleetOrganizer.Infrastructure.Persistence;
using FleetOrganizer.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetOrganizer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFleetOrganizerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<EveDeveloperOptions>()
            .Bind(configuration.GetSection(EveDeveloperOptions.SectionName));

        services.AddSingleton<IAppDataPaths, AppDataPaths>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ =>
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "FleetOrganizer/0.3 (+https://github.com/Sussic/fleet-organizer)");
            return client;
        });
        services.AddSingleton<AuthenticatedCharacterRepository>();
        services.AddSingleton<EveJwtValidator>();
        services.AddSingleton<IEveAuthenticationService, EveAuthenticationService>();
        services.AddSingleton<EveEsiClient>();
        services.AddSingleton<ILiveFleetService, EveFleetService>();
        services.AddSingleton<ICharacterNameResolver, EveCharacterNameResolver>();
        services.AddSingleton<IFleetProfileRepository, FleetProfileRepository>();
        services.AddSingleton<SqliteDatabaseInitializer>();

        return services;
    }
}
