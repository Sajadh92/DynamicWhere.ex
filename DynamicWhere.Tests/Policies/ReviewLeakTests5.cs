using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- P12b: an entity that takes the DbContext in its constructor and loads its navigation with it -----

    public class ZrCtxBlog
    {
        private readonly ZrProbeContext5? _context;
        private List<ZrCtxPost>? _posts;

        public ZrCtxBlog()
        {
        }

        private ZrCtxBlog(ZrProbeContext5 context) => _context = context;

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Loaded on first read through the context EF Core injected, the pattern EF Core documents.</summary>
        public List<ZrCtxPost> Posts
        {
            get => _posts ??= _context?.Set<ZrCtxPost>().AsNoTracking().Where(p => p.ZrCtxBlogId == Id).ToList()
                              ?? new List<ZrCtxPost>();
            set => _posts = value;
        }
    }

    public class ZrCtxPost
    {
        public int Id { get; set; }

        public int ZrCtxBlogId { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwDenied]
        public string? Draft { get; set; }
    }

    public sealed class ZrProbeContext5 : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZrProbeContext5(SqliteConnection connection) => _connection = connection;

        public DbSet<ZrCtxBlog> Blogs => Set<ZrCtxBlog>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZrCtxBlog>().Navigation(b => b.Posts).HasField("_posts");
    }

    public sealed class ReviewLeakTests5 : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext5 _db;

        public ReviewLeakTests5(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZrProbeContext5(_connection);
            _db.Database.EnsureCreated();
            _db.Blogs.Add(new ZrCtxBlog { Name = "B1", Posts = { new ZrCtxPost { Title = "T", Draft = "context-draft" } } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P12b_a_navigation_its_entity_loads_through_an_injected_DbContext(DwTier tier)
        {
            ZrCtxBlog unguarded = _db.Blogs.AsNoTracking().ToList().Single();

            _out.WriteLine($"unguarded drafts: {string.Join(",", unguarded.Posts.Select(p => p.Draft))}");

            ZrCtxBlog guarded;

            try
            {
                guarded = Guard(_db.Blogs, tier).ToList(new Filter()).Data.Single();
            }
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
                return;
            }

            Assert.DoesNotContain(guarded.Posts, post => post.Draft == "context-draft");
        }
    }
}
