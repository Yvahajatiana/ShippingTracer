using Microsoft.Extensions.Hosting;

namespace ParcelFlow.BuildingBlocks.Api.Hosting;

public static class ParcelFlowEnvironments
{
    /// <summary>
    /// Public demonstration deployment: behaves as Production (HTTPS towards Keycloak and between services,
    /// no Swagger) except that the startup database initialization is allowed.
    /// </summary>
    public const string Demo = "Demo";

    public static bool IsDemo(this IHostEnvironment environment) => environment.IsEnvironment(Demo);

    /// <summary>
    /// Startup migrations and demo seeds (<c>Database:Initialize</c>, <c>Database:SeedDemoData</c>) only run in
    /// Development and in the public demo; Production migrates through dedicated jobs and never seeds.
    /// </summary>
    public static bool AllowsStartupDatabaseInitialization(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsDemo();
}
