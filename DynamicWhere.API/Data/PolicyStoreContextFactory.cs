using DynamicWhere.ex.Policies.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DynamicWhere.API.Data;

/// <summary>
/// Builds a <see cref="DwPolicyDbContext"/> for the EF Core command-line tools.
/// </summary>
/// <remarks>
/// The context lives in the DynamicWhere.ex.Policies.EntityFrameworkCore package rather than in this
/// project, so <c>dotnet ef</c> has nothing to construct without this. The connection string is only
/// read to build a model — no migration is applied from here.
/// </remarks>
public sealed class PolicyStoreContextFactory : IDesignTimeDbContextFactory<DwPolicyDbContext>
{
    /// <summary>Where the policy store's migrations live.</summary>
    /// <remarks>
    /// EF Core puts migrations in the assembly that declares the context, and this one is declared
    /// in a NuGet package. Without redirecting it, <c>dotnet ef migrations add</c> refuses outright:
    /// "your target project doesn't match your migrations assembly". Every consumer of
    /// <see cref="DwPolicyDbContext"/> has to set this to their own assembly, so it is set in both
    /// places the context is built — here for the tools, and in Program.cs for the running app.
    /// </remarks>
    public const string MigrationsAssembly = "DynamicWhere.API";

    /// <summary>Creates the context the tools will read the model from.</summary>
    /// <param name="args">Command-line arguments, unused.</param>
    /// <returns>A context pointed at the configured database.</returns>
    public DwPolicyDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        string connection =
            configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=dynamicwhere;Username=postgres;Password=postgres";

        DbContextOptions<DwPolicyDbContext> options =
            new DbContextOptionsBuilder<DwPolicyDbContext>()
                .UseNpgsql(connection, sql => sql.MigrationsAssembly(MigrationsAssembly))
                .Options;

        return new DwPolicyDbContext(options);
    }
}
