using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using FleetOrganizer.Infrastructure.Esi;
using FleetOrganizer.Infrastructure.Diagnostics;
using FleetOrganizer.Infrastructure.Operations;
using FleetOrganizer.Infrastructure.Persistence;
using FleetOrganizer.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

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
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development";
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"FleetOrganizer/{version} (+https://github.com/Sussic/fleet-organizer)");
            return client;
        });
        services.AddSingleton<AuthenticatedCharacterRepository>();
        services.AddSingleton<EveJwtValidator>();
        services.AddSingleton<IEveAuthenticationService, EveAuthenticationService>();
        services.AddSingleton<EveEsiClient>();
        services.AddSingleton<ILiveFleetService, EveFleetService>();
        services.AddSingleton<IFleetWriteService, EveFleetWriteService>();
        services.AddSingleton<ICharacterNameResolver, EveCharacterNameResolver>();
        services.AddSingleton<IFleetProfileRepository, FleetProfileRepository>();
        services.AddSingleton<IFleetDeskPreferencesRepository, FleetDeskPreferencesRepository>();
        services.AddSingleton<IFleetOperationRepository, FleetOperationRepository>();
        services.AddSingleton<IFleetOperationService, FleetOperationService>();
        services.AddSingleton<IDiagnosticExportService, DiagnosticExportService>();
        services.AddSingleton<ILocalDataService, LocalDataService>();
        services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();
        services.AddSingleton<SqliteDatabaseInitializer>();

        return services;
    }
}
