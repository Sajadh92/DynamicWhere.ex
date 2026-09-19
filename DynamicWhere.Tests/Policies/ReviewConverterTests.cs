using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ converters the model does not name: models

    public class ZxIssued
    {
        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    /// <summary>The application's own converter: writes any object as JSON and reads it back as ZxIssued.</summary>
    public sealed class ZxPayloadConverter : ValueConverter<object?, string>
    {
        public ZxPayloadConverter()
            : base(v => Write(v), s => Read(s))
        {
        }

        private static string Write(object? value) =>
            value is null ? "null" : JsonSerializer.Serialize(value, value.GetType());

        private static object? Read(string text) => JsonSerializer.Deserialize<ZxIssued>(text);
    }

    public class ZxConvEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public object? Payload { get; set; }
    }

    /// <summary>How the converter is configured.</summary>
    public enum ZxConverterStyle
    {
        /// <summary>Control: HasConversion(new converter()).</summary>
        Instance,

        /// <summary>HasConversion&lt;TConverter&gt;().</summary>
        GenericType,

        /// <summary>HasConversion(typeof(TConverter)).</summary>
        TypeObject,

        /// <summary>ConfigureConventions: Properties&lt;object&gt;().HaveConversion&lt;TConverter&gt;().</summary>
        Convention
    }

    public abstract class ZxConvContextBase : DbContext
    {
        private readonly SqliteConnection _connection;

        protected ZxConvContextBase(SqliteConnection connection) => _connection = connection;

        public DbSet<ZxConvEvent> Events => Set<ZxConvEvent>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class ZxConvInstanceContext : ZxConvContextBase
    {
        public ZxConvInstanceContext(SqliteConnection connection) : base(connection) { }

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZxConvEvent>().Property(e => e.Payload).HasConversion(new ZxPayloadConverter());
    }

    public sealed class ZxConvGenericTypeContext : ZxConvContextBase
    {
        public ZxConvGenericTypeContext(SqliteConnection connection) : base(connection) { }

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZxConvEvent>().Property(e => e.Payload).HasConversion<ZxPayloadConverter>();
    }

    public sealed class ZxConvTypeObjectContext : ZxConvContextBase
    {
        public ZxConvTypeObjectContext(SqliteConnection connection) : base(connection) { }

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZxConvEvent>().Property(e => e.Payload).HasConversion(typeof(ZxPayloadConverter));
    }

    public sealed class ZxConvConventionContext : ZxConvContextBase
    {
        public ZxConvConventionContext(SqliteConnection connection) : base(connection) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configuration) =>
            configuration.Properties<object>().HaveConversion<ZxPayloadConverter>();

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZxConvEvent>().Property(e => e.Payload);
    }

    // ============================================================================ converters configured every way: tests

    /// <summary>
    /// A converted column that can hold any object is left out once a projection is built for another field,
    /// however the converter is configured: an instance, a type, or a lambda.
    /// </summary>
    public sealed class ReviewConverterTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewConverterTests(ITestOutputHelper output) => _out = output;

        private static ZxConvContextBase Create(ZxConverterStyle style, SqliteConnection connection) => style switch
        {
            ZxConverterStyle.Instance => new ZxConvInstanceContext(connection),
            ZxConverterStyle.GenericType => new ZxConvGenericTypeContext(connection),
            ZxConverterStyle.TypeObject => new ZxConvTypeObjectContext(connection),
            _ => new ZxConvConventionContext(connection)
        };

        [Theory]
        [InlineData(ZxConverterStyle.Instance, DwTier.Strict)]
        [InlineData(ZxConverterStyle.GenericType, DwTier.Strict)]
        [InlineData(ZxConverterStyle.GenericType, DwTier.Convenience)]
        [InlineData(ZxConverterStyle.TypeObject, DwTier.Strict)]
        [InlineData(ZxConverterStyle.Convention, DwTier.Strict)]
        public void Zx_C1_a_converted_object_column_beside_a_top_level_denial(ZxConverterStyle style, DwTier tier)
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            ZxConvContextBase db;

            try
            {
                db = Create(style, connection);
                db.Database.EnsureCreated();
            }
            catch (Exception e)
            {
                _out.WriteLine($"{style}: model not built: {e.GetType().Name}: {e.Message.Split('\n')[0]}");

                return;
            }

            using (db)
            {
                db.Events.Add(new ZxConvEvent
                {
                    Kind = "CardIssued", Actor = "conv-actor-secret", Payload = new ZxIssued { Label = "visa", Pan = $"conv-pan-{style}" }
                });
                db.SaveChanges();
                db.ChangeTracker.Clear();

                IProperty payload = db.Model.FindEntityType(typeof(ZxConvEvent))!.FindProperty(nameof(ZxConvEvent.Payload))!;

                _out.WriteLine($"{style}: GetValueConverter()={payload.GetValueConverter()?.GetType().Name ?? "null"} "
                               + $"mapping.Converter={payload.GetTypeMapping().Converter?.GetType().Name ?? "null"}");

                Assert.True(ZxKit.Holds(db.Events.AsNoTracking().ToList(), $"conv-pan-{style}"));

                PolicyQueryable<ZxConvEvent> guarded = ZxKit.Guard(db.Events, tier);
                object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

                _out.WriteLine($"{style} {tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

                Assert.False(ZxKit.Holds(data, "conv-actor-secret"));
                Assert.False(ZxKit.Holds(data, $"conv-pan-{style}"));

                object? dynamicData = ZxKit.SafeRead(() => ZxKit.Guard(db.Events, tier).ToListDynamic(new Filter()).Data);

                _out.WriteLine($"{style} {tier} dynamic: sent={ZxKit.Json(dynamicData)}");

                Assert.False(ZxKit.Holds(dynamicData, $"conv-pan-{style}"));
            }
        }
    }
}
