using DynamicWhere.API.Data;
using DynamicWhere.API.Models;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Configure Services
// --------------------------------------------------

// Configure Npgsql to support dynamic JSON types (Dictionary, etc.)
var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection")
);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dataSource)
);

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// The policy store lives in the same database on its own context, so its two tables migrate
// independently of the demo schema and a host that wants policies without them can drop this.
// MigrationsAssembly is not optional here: DwPolicyDbContext is declared in a NuGet package, and
// EF Core looks for migrations in the assembly that declares the context. Left alone, the tooling
// refuses to scaffold and the running app finds no migrations to apply.
var policyDbOptions = new DbContextOptionsBuilder<DwPolicyDbContext>()
    .UseNpgsql(dataSource, sql => sql.MigrationsAssembly(PolicyStoreContextFactory.MigrationsAssembly))
    .Options;

builder.Services.AddDbContext<DwPolicyDbContext>(options =>
    options.UseNpgsql(dataSource, sql => sql.MigrationsAssembly(PolicyStoreContextFactory.MigrationsAssembly)));

// MapDwPolicyAdmin refuses to mount without both of these by name — there is deliberately no
// default, because POST /rules changes what every caller may see. This demo gates them on a header
// so Swagger can drive them; a real deployment would require a role or a scope here instead.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DwPolicyRead", policy => policy.RequireAssertion(HasAdminHeader));
    options.AddPolicy("DwPolicyWrite", policy => policy.RequireAssertion(HasAdminHeader));
});

// Configure DynamicWhere cache
ConfigureDynamicWhereCache(builder.Configuration);

// Add controllers and JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Handle reference loops and ignore null values
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

        // Include null values during serialization so explicitly selected
        // nullable fields (e.g. DateTime?, TimeOnly?) appear as null in the output.
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.Never;

        // Allow serialization of Infinity and NaN values
        options.JsonSerializerOptions.NumberHandling =
            System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
    });

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DynamicWhere.API",
        Version = "v1", // keep this a simple doc name like v1
        Description = "API for testing DynamicWhere package with cache management",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "DynamicWhere API",
            Url = new Uri("https://github.com/Sajadh92/DynamicWhere.ex")
        }
    });

    c.EnableAnnotations();

    //XML comments (optional)
    var xml = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xml);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// --------------------------------------------------
// Database Initialization
// --------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();

        // Apply pending migrations
        await context.Database.MigrateAsync();

        // Seed initial data
        await DataSeeder.SeedDataAsync(context);

        // The two policy tables, on their own migration history.
        var policyContext = services.GetRequiredService<DwPolicyDbContext>();
        await policyContext.Database.MigrateAsync();

        await ConfigureDynamicWherePoliciesAsync(policyDbOptions, services.GetRequiredService<ILogger<Program>>());

        // The admin demo controller opens its own short-lived contexts rather than sharing a scoped
        // one, because the store is also read by the provider's background refresh timer.
        DynamicWhere.API.Controllers.PolicyAdminController.UseConnection(
            builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
        throw;
    }
}

// --------------------------------------------------
// Configure HTTP Request Pipeline
// --------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "DynamicWhere API Documentation";
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DynamicWhere.API v1");

        c.DisplayRequestDuration();
        c.DocExpansion(DocExpansion.None);
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Drains whatever a request recorded against its context to the configured sink, once, at the end
// of the request. Without it the audit entries are built and never written.
app.UseDwPolicyAudit();

app.MapControllers();

// The package's own administrative surface: schema, rules, explain, simulate, health. It refuses to
// map without both policy names, which is why they are registered above rather than defaulted here.
app.MapDwPolicyAdmin(options =>
{
    options.RoutePrefix = "/dw-policies";
    options.ReadPolicy = "DwPolicyRead";
    options.WritePolicy = "DwPolicyWrite";
    options.Claims.Purpose = "admin";
});

app.Run();

// --------------------------------------------------
// Helper: policy administration authorization
// --------------------------------------------------

// Deliberately not AllowAnonymousAccess. That escape hatch exists for a deployment where something
// in front of the application authorizes, and using it in a sample would teach the wrong shape for
// an endpoint that rewrites what every caller may see.
static bool HasAdminHeader(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context)
{
    if (context.Resource is not HttpContext http)
    {
        return false;
    }

    return http.Request.Headers.TryGetValue("X-Dw-Admin", out var value)
        && string.Equals(value, "demo", StringComparison.Ordinal);
}

// --------------------------------------------------
// Helper: DynamicWhere policy configuration
// --------------------------------------------------

// Called once, at startup. DwPolicy.Configure refuses a second call: the tier is read by every
// request thread without synchronization, and a posture that can change mid-flight is one that can
// be relaxed by a code path nobody expected to be security-relevant.
static async Task ConfigureDynamicWherePoliciesAsync(
    DbContextOptions<DwPolicyDbContext> policyDbOptions, ILogger logger)
{
    if (DwPolicy.IsConfigured)
    {
        return;
    }

    var options = new DwPolicyOptions
    {
        // Convenience rather than Strict so the demo can show getQueryString and the generated SQL.
        // Strict would throw on both — see design section 7.5.
        Tier = DwTier.Convenience,

        // Salt for MaskStrategy.Hash. A real deployment keeps this out of source.
        HashSalt = "dynamicwhere-demo-salt",

        // A store that cannot be reached serves the last good snapshot rather than failing the
        // request, bounded by MaxSnapshotAge below.
        StoreFailure = StoreFailureMode.LastKnownGood,
        MaxSnapshotAge = TimeSpan.FromMinutes(15),
        RefreshInterval = TimeSpan.FromSeconds(30),
    };

    options.Entities.Expose<Employee>("Employee");

    // Contradictions in the model, reported rather than thrown, so every one shows up in a single
    // run instead of one per restart. The Email/[DwNoOrder] pairing on Employee exists because this
    // check names it: a masked field that can still be sorted on ranks the real values.
    PolicyModelReport report = DwPolicy.ValidateModel(typeof(Employee));

    foreach (string warning in report.Warnings)
    {
        logger.LogWarning("Policy model: {Warning}", warning);
    }

    if (report.Errors.Count > 0)
    {
        foreach (string error in report.Errors)
        {
            logger.LogError("Policy model: {Error}", error);
        }

        throw new InvalidOperationException(
            "The policy model does not validate. See the errors above.");
    }

    // A new context per read: the store is polled on a background timer and a pooled context shared
    // with request threads would be used concurrently.
    var store = new EfPolicyStore(() => new DwPolicyDbContext(policyDbOptions));

    StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(store, options);

    DwPolicy.Configure(options, provider);

    logger.LogInformation(
        "DynamicWhere policies configured. Store version {Version}.", provider.Version);
}

// --------------------------------------------------
// Helper: DynamicWhere Cache Configuration
// --------------------------------------------------
static void ConfigureDynamicWhereCache(IConfiguration configuration)
{
    var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

    var maxCacheSize = configuration.GetValue<int?>("DynamicWhere:Cache:MaxCacheSize");
    var evictionStrategy = configuration.GetValue<string?>("DynamicWhere:Cache:EvictionStrategy");

    if (maxCacheSize.HasValue || !string.IsNullOrEmpty(evictionStrategy))
    {
        // Custom configuration from appsettings
        CacheExpose.Configure(options =>
        {
            if (maxCacheSize.HasValue)
                options.MaxCacheSize = maxCacheSize.Value;

            if (!string.IsNullOrEmpty(evictionStrategy))
            {
                options.EvictionStrategy =
                    Enum.TryParse<CacheEvictionStrategy>(evictionStrategy, true, out var strategy)
                        ? strategy
                        : CacheEvictionStrategy.LRU;
            }
        });
    }
    else
    {
        // Environment-based defaults
        CacheExpose.Configure(environment switch
        {
            "Development" => CacheOptions.ForDevelopment(),
            "Production" => CacheOptions.ForHighMemoryEnvironment(),
            _ => CacheOptions.ForHighMemoryEnvironment()
        });
    }
}
