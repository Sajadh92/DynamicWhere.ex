using DynamicWhere.API.Data;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Source;
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

// Configure DynamicWhere cache
ConfigureDynamicWhereCache(builder.Configuration);

// Add controllers and JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Handle reference loops and ignore null values
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

        // Ignore null values during serialization
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

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
app.MapControllers();
app.Run();

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
