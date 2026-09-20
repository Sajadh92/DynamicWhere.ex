using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.Tests.Policies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests
{
    // EF Core 8 only (complex types, JSON columns): kept at the test project's root so the EF Core 6 floor leg,
    // which compiles Policies/** only, leaves it out.

    // ============================================================================ a converted member inside a complex or JSON type: models

    public class ZxComplexMeta
    {
        public string Tag { get; set; } = string.Empty;

        public object? Payload { get; set; }
    }

    public class ZxComplexEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public ZxComplexMeta Meta { get; set; } = new();
    }

    public class ZxJsonMeta
    {
        public string Tag { get; set; } = string.Empty;

        public object? Payload { get; set; }
    }

    public class ZxJsonEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public ZxJsonMeta? Meta { get; set; }
    }

    public sealed class ZxComplexContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZxComplexContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZxComplexEvent> ComplexEvents => Set<ZxComplexEvent>();

        public DbSet<ZxJsonEvent> JsonEvents => Set<ZxJsonEvent>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZxComplexEvent>().ComplexProperty(e => e.Meta, meta => meta.Property(m => m.Payload).HasConversion(new ZxPayloadConverter()));
            model.Entity<ZxJsonEvent>().OwnsOne(e => e.Meta, meta =>
            {
                meta.ToJson();
                meta.Property(m => m.Payload).HasConversion(new ZxPayloadConverter());
            });
        }
    }

    // ============================================================================ a converted member inside a complex or JSON type: tests

    /// <summary>
    /// RowShape.Converted asks the entity type for the column. A complex property is read whole as its CLR type, and
    /// the converter on the member inside it is never asked about.
    /// </summary>
    public sealed class ReviewComplexPropertyTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZxComplexContext _db;

        public ReviewComplexPropertyTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZxComplexContext(_connection);
            _db.Database.EnsureCreated();
            _db.ComplexEvents.Add(new ZxComplexEvent
            {
                Kind = "CardIssued", Actor = "complex-actor-secret",
                Meta = new ZxComplexMeta { Tag = "t", Payload = new ZxIssued { Label = "visa", Pan = "complex-pan-secret" } }
            });
            _db.JsonEvents.Add(new ZxJsonEvent
            {
                Kind = "CardIssued", Actor = "json-actor-secret",
                Meta = new ZxJsonMeta { Tag = "t", Payload = new ZxIssued { Label = "visa", Pan = "json-pan-secret" } }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_C2_a_converted_object_member_inside_a_complex_property_beside_a_top_level_denial(DwTier tier)
        {
            Assert.True(ZxKit.Holds(_db.ComplexEvents.AsNoTracking().ToList(), "complex-pan-secret"));

            PolicyQueryable<ZxComplexEvent> guarded = ZxKit.Guard(_db.ComplexEvents, tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "complex-actor-secret"));
            Assert.False(ZxKit.Holds(data, "complex-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_C2_dynamic_a_converted_object_member_inside_a_complex_property(DwTier tier)
        {
            PolicyQueryable<ZxComplexEvent> guarded = ZxKit.Guard(_db.ComplexEvents, tier);
            object? data = ZxKit.SafeRead(() => guarded.ToListDynamic(new Filter()).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "complex-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_C3_a_converted_object_member_inside_a_json_owned_type_beside_a_top_level_denial(DwTier tier)
        {
            List<ZxJsonEvent> raw;

            try
            {
                raw = _db.JsonEvents.AsNoTracking().ToList();
            }
            catch (Exception e)
            {
                _out.WriteLine($"unguarded threw: {e.GetType().Name}: {e.Message.Split('\n')[0]}");

                return;
            }

            _out.WriteLine("unguarded holds pan: " + ZxKit.Holds(raw, "json-pan-secret"));

            PolicyQueryable<ZxJsonEvent> guarded = ZxKit.Guard(_db.JsonEvents, tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "json-actor-secret"));
            Assert.False(ZxKit.Holds(data, "json-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public async Task Zx_C2_segment_a_converted_object_member_inside_a_complex_property(DwTier tier)
        {
            PolicyQueryable<ZxComplexEvent> guarded = ZxKit.Guard(_db.ComplexEvents, tier);
            object? data;

            try
            {
                data = (await guarded.ToListAsync(new Segment())).Data;
            }
            catch (Exception e)
            {
                _out.WriteLine($"{tier}: segment threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");

                return;
            }

            _out.WriteLine($"{tier}: segment sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "complex-pan-secret"));
        }
    }
}
