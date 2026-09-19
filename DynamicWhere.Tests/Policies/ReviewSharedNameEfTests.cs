using System.Linq.Expressions;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// Members sharing a name on an EF Core entity, read from what the query loads; a denied value a derived type hides with
// new, read as its base; and a join on a filtered set, which holds its query inline.

namespace DynamicWhere.Tests.Policies
{
    public class ZwAuthorCard
    {
        public int Id { get; set; }

        public int ZwAuthorId { get; set; }

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZwAuthor
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Never loaded by any probe; the only denial is beneath it.</summary>
        public List<ZwAuthorCard> Cards { get; set; } = new();
    }

    public class ZwDocBase
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public int? ZwAuthorId { get; set; }

        public virtual ZwAuthor? Author { get; set; }
    }

    /// <summary>Re-declares the navigation with new, the same type (to change an annotation, say).</summary>
    public class ZwDoc : ZwDocBase
    {
        public new ZwAuthor? Author
        {
            get => base.Author;
            set => base.Author = value;
        }
    }

    public sealed class ZwDocContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZwDocContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZwDoc> Docs => Set<ZwDoc>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public class ZwInfoBase
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Info { get; set; }
    }

    public class ZwPlainThing
    {
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Hides the denied value with an object of a clean type.</summary>
    public class ZwInfoDerived : ZwInfoBase
    {
        public new ZwPlainThing? Info { get; set; }
    }

    /// <summary>The same beside a top-level denial, so a projection is built.</summary>
    public class ZwInfoDerivedDenied : ZwInfoBase
    {
        [DwDenied]
        public string? Ssn { get; set; }

        public new ZwPlainThing? Info { get; set; }
    }

    public class ZwVipAuthor : ZwAuthor
    {
        public string Level { get; set; } = string.Empty;
    }

    public class ZwDocBase2
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public int? ZwAuthorId { get; set; }

        public ZwAuthor? Author { get; set; }
    }

    /// <summary>Re-declares the navigation with new, under a derived type.</summary>
    public class ZwDoc2 : ZwDocBase2
    {
        public new ZwVipAuthor? Author { get; set; }
    }

    public sealed class ZwDoc2Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZwDoc2Context(SqliteConnection connection) => _connection = connection;

        public DbSet<ZwDoc2> Docs => Set<ZwDoc2>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class ReviewSharedNameEfTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewSharedNameEfTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Zw_N1_an_entity_redeclaring_an_unloaded_navigation_with_new_is_not_projected()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ZwDocContext db = new(connection);
            string model;

            try
            {
                db.Database.EnsureCreated();
                model = "built";
            }
            catch (Exception e)
            {
                model = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            _out.WriteLine($"N1 model {model}; Author members: " + string.Join(", ", typeof(ZwDoc).GetProperties().Where(p => p.Name == "Author").Select(p => p.DeclaringType!.Name)));

            if (!model.StartsWith("built", StringComparison.Ordinal))
            {
                return;
            }

            db.Docs.Add(new ZwDoc { Title = "d1", Author = new ZwAuthor { Name = "a1", Cards = { new ZwAuthorCard { Pan = "zw-author-pan" } } } });
            db.SaveChanges();
            db.ChangeTracker.Clear();

            PolicyQueryable<ZwDoc> guarded = ZwKit.Guard<ZwDoc>(db.Docs);
            object? data = null;
            string code = ZwKit.Code(() => data = guarded.ToList(new Filter()).Data);

            _out.WriteLine($"N1 code={code} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            Assert.Equal("ran", code);
            Assert.False(ZwKit.AnyDropped(guarded), ZwKit.Trace(guarded));
        }

        [Fact]
        public void Zw_N1b_an_entity_redeclaring_a_navigation_with_new_under_a_derived_type()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ZwDoc2Context db = new(connection);
            string model;

            try
            {
                db.Database.EnsureCreated();
                model = "built: " + string.Join(", ", db.Model.FindEntityType(typeof(ZwDoc2))!.GetNavigations().Select(n => $"{n.Name}->{n.TargetEntityType.ClrType.Name}"));
            }
            catch (Exception e)
            {
                model = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            _out.WriteLine($"N1b model {model}");

            if (!model.StartsWith("built", StringComparison.Ordinal))
            {
                return;
            }

            db.Docs.Add(new ZwDoc2 { Title = "d1", Author = new ZwVipAuthor { Name = "a1", Level = "gold", Cards = { new ZwAuthorCard { Pan = "zw-author-pan" } } } });
            db.SaveChanges();
            db.ChangeTracker.Clear();

            PolicyQueryable<ZwDoc2> guarded = ZwKit.Guard<ZwDoc2>(db.Docs);
            object? data = null;
            string code = ZwKit.Code(() => data = guarded.ToList(new Filter()).Data);

            _out.WriteLine($"N1b code={code} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            // The caller includes the author (not its cards, beneath which the only denial sits).
            PolicyQueryable<ZwDoc2> included = ZwKit.Guard<ZwDoc2>(db.Docs.Include(d => d.Author));
            List<ZwDoc2>? rows = null;
            string code2 = ZwKit.Code(() => rows = included.ToList(new Filter()).Data);

            _out.WriteLine($"N1b Include(d => d.Author): code={code2} sent={ZwKit.Json(rows)} trace=[{ZwKit.Trace(included)}]");

            Assert.Equal("ran", code);
            Assert.False(ZwKit.AnyDropped(guarded), ZwKit.Trace(guarded));
            Assert.Equal("ran", code2);
            Assert.Equal("a1", rows![0].Author?.Name);
        }

        [Fact]
        public void Zw_N2_a_hidden_denied_value_serialized_through_the_base_type()
        {
            ZwHideValueDerived row = new() { Id = 1, Code = "open" };
            ((ZwHideValueBase)row).Code = "zw-hidden-base-secret";

            PolicyQueryable<ZwHideValueDerived> guarded = ZwKit.Guard(new[] { row }.AsQueryable());
            List<ZwHideValueDerived> sent = guarded.ToList(new Filter()).Data;

            // An API whose response type is the base class, as a controller returning List<Base> does.
            string asBase = JsonSerializer.Serialize(sent.Cast<ZwHideValueBase>().ToList());

            _out.WriteLine($"N2 as derived={JsonSerializer.Serialize(sent)} as base={asBase} trace=[{ZwKit.Trace(guarded)}] denials(Code)="
                           + string.Join(",", new AttributePolicyProvider()
                               .GetFragments(typeof(ZwHideValueDerived), new DwPolicyContext())
                               .Where(f => f.FieldPath == "Code").Select(f => f.Effect)));

            Assert.DoesNotContain("zw-hidden-base-secret", asBase);
        }

        [Fact]
        public void Zw_N3_why_a_join_on_a_filtered_set_reads_as_building()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ZwContext db = new(connection);
            IQueryable<ZwCustomer> source = db.Orders.Join(db.Customers.Where(c => c.Region != string.Empty), o => o.ZwCustomerId, c => c.Id, (o, c) => c);
            MethodCallExpression join = (MethodCallExpression)source.Expression;
            Expression inner = join.Arguments[1];
            string evaluated;

            try
            {
                object? value = Expression.Lambda<Func<object?>>(Expression.Convert(inner, typeof(object))).Compile(preferInterpretation: true)();

                evaluated = "ok " + value?.GetType().Name;
            }
            catch (Exception e)
            {
                evaluated = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            _out.WriteLine($"N3 inner={inner.NodeType} {inner.GetType().Name} arg0={((MethodCallExpression)inner).Arguments[0].GetType().Name} evaluated: {evaluated} {ZwKit.Shape(source.Expression)}");

            // Diagnostic: the inner sequence cannot be compiled on its own (an inline EF Core root is not reducible), and
            // the guard must not read that as building.
            Assert.Contains("builds=False", ZwKit.Shape(source.Expression));
        }
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zw_N4_a_denied_value_hidden_by_an_object_of_a_clean_type(DwTier tier)
        {
            ZwInfoDerived row = new() { Id = 1, Info = new ZwPlainThing { Label = "x" } };
            ((ZwInfoBase)row).Info = "zw-info-base-secret";
            ZwInfoDerivedDenied denied = new() { Id = 2, Ssn = "ssn", Info = new ZwPlainThing { Label = "y" } };
            ((ZwInfoBase)denied).Info = "zw-info-base-secret-2";

            PolicyQueryable<ZwInfoDerived> guarded = ZwKit.Guard(new[] { row }.AsQueryable(), tier);
            object? data = ZwKit.SafeRead(() => guarded.ToList(new Filter()).Data);
            PolicyQueryable<ZwInfoDerivedDenied> guarded2 = ZwKit.Guard(new[] { denied }.AsQueryable(), tier);
            object? data2 = ZwKit.SafeRead(() => guarded2.ToList(new Filter()).Data);

            string asBase = data is List<ZwInfoDerived> a ? JsonSerializer.Serialize(a.Cast<ZwInfoBase>().ToList()) : "null";
            string asBase2 = data2 is List<ZwInfoDerivedDenied> b ? JsonSerializer.Serialize(b.Cast<ZwInfoBase>().ToList()) : "null";

            _out.WriteLine($"N4 {tier}: as base={asBase} trace=[{ZwKit.Trace(guarded)}]");
            _out.WriteLine($"N4 {tier} projected: as base={asBase2} trace=[{ZwKit.Trace(guarded2)}]");

            Assert.False(ZwKit.Holds(data, "zw-info-base-secret"));
            Assert.False(ZwKit.Holds(data2, "zw-info-base-secret-2"));
        }
    }
}
