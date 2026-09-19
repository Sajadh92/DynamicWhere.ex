using System.Collections;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ round 3, batch 2: models

    // ---- P10: a derived type overrides a member its base declares, and denies it there ----------------

    public class ZrVehicle
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public virtual string? Code { get; set; }
    }

    public class ZrArmored : ZrVehicle
    {
        /// <summary>Overridden to deny it here; it reads and writes the base's storage, which EF Core maps.</summary>
        [DwDenied]
        public override string? Code
        {
            get => base.Code;
            set => base.Code = value;
        }
    }

    public class ZrConvoy
    {
        public int Id { get; set; }

        public string Route { get; set; } = string.Empty;

        public int ZrVehicleId { get; set; }

        public ZrVehicle? Lead { get; set; }
    }

    public class ZrConvoyRow
    {
        public int Id { get; set; }

        public ZrVehicle? Lead { get; set; }
    }

    // ---- P11: a projection that builds a subtype of T --------------------------------------------------

    public class ZrOrgDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZrBankDto : ZrOrgDto
    {
        [DwDenied]
        public string? Swift { get; set; }
    }

    // ---- P3d: a base type whose only subtype is generic, under a policy denying what it does not name ---

    public class ZrShape
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class ZrShapeOf<TUnit> : ZrShape
    {
        public string? Hidden { get; set; }
    }

    public class ZrCanvas
    {
        public int Id { get; set; }

        public ZrShape? Shape { get; set; }
    }

    // ---- P12: an asynchronous lazy-loader delegate ------------------------------------------------------

    public class ZrAsyncBlog
    {
        private readonly Func<object, CancellationToken, string, Task>? _loader;
        private List<ZrAsyncPost>? _posts;

        public ZrAsyncBlog()
        {
        }

        private ZrAsyncBlog(Func<object, CancellationToken, string, Task> lazyLoader) => _loader = lazyLoader;

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZrAsyncPost> Posts
        {
            get
            {
                _loader?.Invoke(this, CancellationToken.None, nameof(Posts)).GetAwaiter().GetResult();

                return _posts ??= new List<ZrAsyncPost>();
            }
            set => _posts = value;
        }

        public bool HasLoader => _loader is not null;
    }

    public class ZrAsyncPost
    {
        public int Id { get; set; }

        public int ZrAsyncBlogId { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwDenied]
        public string? Draft { get; set; }
    }

    public sealed class ZrProbeContext2 : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZrProbeContext2(SqliteConnection connection) => _connection = connection;

        public DbSet<ZrVehicle> Vehicles => Set<ZrVehicle>();

        public DbSet<ZrConvoy> Convoys => Set<ZrConvoy>();

        public DbSet<ZrAsyncBlog> AsyncBlogs => Set<ZrAsyncBlog>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZrArmored>();
            model.Entity<ZrAsyncBlog>().Navigation(b => b.Posts).HasField("_posts");
            model.Entity<ZrAsyncBlog>().Ignore(b => b.HasLoader);
        }
    }

    // ============================================================================ round 3, batch 2: tests

    public sealed class ReviewLeakTests2 : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext2 _db;

        public ReviewLeakTests2(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ZrProbeContext2(_connection);
            _db.Database.EnsureCreated();

            _db.Convoys.Add(new ZrConvoy { Route = "R1", Lead = new ZrArmored { Name = "A1", Code = "override-secret" } });
            _db.AsyncBlogs.Add(new ZrAsyncBlog { Name = "B1", Posts = { new ZrAsyncPost { Title = "T", Draft = "async-draft" } } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict, params IDwPolicyProvider[] more)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray()));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        private static FakePolicyProvider DenyingAllBut(params string[] allowed)
        {
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            foreach (string path in allowed)
            {
                rules.Add(path, PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);
            }

            return rules;
        }

        private static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is ValueType || current is MemberInfo)
                {
                    continue;
                }

                if (current is string held)
                {
                    if (held == text)
                    {
                        return true;
                    }

                    continue;
                }

                if (!seen.Add(current))
                {
                    continue;
                }

                if (current is IEnumerable items)
                {
                    foreach (object? item in items)
                    {
                        pending.Push(item);
                    }

                    continue;
                }

                foreach (PropertyInfo property in current.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    try
                    {
                        pending.Push(property.GetValue(current));
                    }
                    catch
                    {
                        // A getter that throws holds nothing readable.
                    }
                }
            }

            return false;
        }

        private static object? SafeRead(Func<object?> read)
        {
            try
            {
                return read();
            }
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------------ P10

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_rows_in_memory_of_a_subtype_that_denies_an_overridden_member(DwTier tier)
        {
            ZrVehicle[] rows = { new ZrArmored { Id = 1, Name = "A1", Code = "override-secret" } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data), "override-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_an_entity_hierarchy_root_whose_derived_type_denies_an_overridden_column(DwTier tier)
        {
            Assert.True(Holds(_db.Vehicles.AsNoTracking().ToList(), "override-secret"));

            Assert.False(Holds(SafeRead(() => Guard(_db.Vehicles, tier).ToList(new Filter()).Data), "override-secret"));
            Assert.False(Holds(SafeRead(() => Guard(_db.Vehicles, tier).ToListDynamic(new Filter()).Data), "override-secret"));
        }

        /// <summary>The same member read through the base type is also groupable, so a summary returns it as a key.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_a_summary_grouped_by_the_overridden_member(DwTier tier)
        {
            Summary summary = new()
            {
                GroupBy = new DynamicWhere.ex.Classes.Core.GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<DynamicWhere.ex.Classes.Core.AggregateBy>
                    {
                        new() { Alias = "Total", Aggregator = DynamicWhere.ex.Enums.Aggregator.Count }
                    }
                }
            };

            string sent;

            try
            {
                sent = System.Text.Json.JsonSerializer.Serialize(Guard(_db.Vehicles, tier).ToList(summary).Data);
            }
            catch (PolicyException refusal) when (refusal.ErrorCode is PolicyErrorCode.FieldDeniedForGroup or PolicyErrorCode.FieldDeniedForSelect)
            {
                return;
            }

            _out.WriteLine(sent);

            Assert.DoesNotContain("override-secret", sent);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_an_included_base_typed_navigation(DwTier tier)
        {
            IQueryable<ZrConvoy> source = _db.Convoys.Include(c => c.Lead);

            Assert.True(Holds(source.AsNoTracking().ToList(), "override-secret"));

            Assert.False(Holds(SafeRead(() => Guard(source, tier).ToList(new Filter()).Data), "override-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_a_base_typed_navigation_named_in_Selects(DwTier tier)
        {
            Assert.False(Holds(SafeRead(() => Guard(_db.Convoys, tier).ToList(Selecting("Id", "Lead")).Data), "override-secret"));
            Assert.False(Holds(SafeRead(() => Guard(_db.Convoys, tier).ToList(Selecting("Id", "Lead.Code")).Data), "override-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10_control_a_projected_base_typed_member_is_scanned(DwTier tier)
        {
            IQueryable<ZrConvoyRow> rows = _db.Convoys.Select(c => new ZrConvoyRow { Id = c.Id, Lead = c.Lead });

            Assert.False(Holds(SafeRead(() => Guard(rows, tier).ToList(new Filter()).Data), "override-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P11

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P11_a_projection_that_builds_a_subtype_of_T(DwTier tier)
        {
            // A repository returning IQueryable<ZrOrgDto> from a projection into the derived DTO.
            IQueryable<ZrOrgDto> covariant = _db.Convoys.Select(c => new ZrBankDto { Id = c.Id, Name = c.Route, Swift = c.Route + "-swift-secret" });
            IQueryable<ZrOrgDto> declared = _db.Convoys.Select<ZrConvoy, ZrOrgDto>(c => new ZrBankDto { Id = c.Id, Name = c.Route, Swift = c.Route + "-swift-secret" });

            Assert.True(Holds(covariant.ToList(), "R1-swift-secret"));

            Assert.False(Holds(SafeRead(() => Guard(covariant, tier).ToList(new Filter()).Data), "R1-swift-secret"));
            Assert.False(Holds(SafeRead(() => Guard(declared, tier).ToList(new Filter()).Data), "R1-swift-secret"));
            Assert.False(Holds(SafeRead(() => Guard(covariant, tier).ToListDynamic(new Filter()).Data), "R1-swift-secret"));
        }

        [Fact]
        public void Zr_P11_control_the_same_rows_in_memory_are_projected()
        {
            ZrOrgDto[] rows = { new ZrBankDto { Id = 1, Name = "R1", Swift = "R1-swift-secret" } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "R1-swift-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P3d

        [Fact]
        public void Zr_P3d_a_generic_only_subtype_under_a_deny_by_default_policy()
        {
            ZrCanvas[] rows = { new() { Id = 1, Shape = new ZrShapeOf<int> { Id = 2, Kind = "k", Hidden = "unnamed-secret" } } };

            FakePolicyProvider rules = DenyingAllBut("Id", "Shape", "Shape.Id", "Shape.Kind");

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), DwTier.Strict, rules).ToList(Selecting("Id", "Shape")).Data), "unnamed-secret"));
        }

        [Fact]
        public void Zr_P3d_control_a_non_generic_subtype_under_the_same_policy_is_refused()
        {
            ZrPen[] rows = { new() { Id = 1, Label = "pen", Pet = new ZrCat { Id = 2, Name = "Tom", Microchip = "unnamed-secret" } } };

            FakePolicyProvider rules = DenyingAllBut("Id", "Pet", "Pet.Id", "Pet.Name");

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), DwTier.Strict, rules).ToList(Selecting("Id", "Pet")).Data), "unnamed-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P12

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P12_an_asynchronous_lazy_loader_delegate(DwTier tier)
        {
            ZrAsyncBlog unguarded = _db.AsyncBlogs.AsNoTracking().ToList().Single();

            try
            {
                _out.WriteLine($"EF injected the async loader: {unguarded.HasLoader}; unguarded posts: {unguarded.Posts.Count}");
            }
            catch (InvalidOperationException detached)
            {
                // EF Core 6 refuses to lazy-load an entity read with AsNoTracking, so nothing can load after the query.
                _out.WriteLine($"no lazy loading of an untracked entity here: {detached.Message}");

                return;
            }

            ZrAsyncBlog guarded = Guard(_db.AsyncBlogs, tier).ToList(new Filter()).Data.Single();

            _out.WriteLine($"guarded row has loader: {guarded.HasLoader}");

            Assert.DoesNotContain(guarded.Posts, post => post.Draft == "async-draft");
        }
    }
}
