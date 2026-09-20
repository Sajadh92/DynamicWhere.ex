using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// DCMP's DW-13 shapes after 6c9e171's column-only rule: a projected row must still take its declared
// default when it assigns every default field a mapped column, directly, through a reference
// navigation or through EF.Property on a shadow property.

namespace DynamicWhere.Tests.Policies
{
    public class ZbTask
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int? ZbTaskGroupId { get; set; }

        public ZbTaskGroup? Group { get; set; }
    }

    public class ZbTaskGroup
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    [DwEntity(DefaultOrder = "Code desc")]
    public class ZbTaskRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    /// <summary>DCMP's shape with a denied member, so the guard also synthesizes a projection.</summary>
    [DwEntity(DefaultOrder = "Code desc")]
    public class ZbTaskSecretRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwDenied]
        public string? Secret { get; set; }
    }

    [DwEntity(DefaultOrder = "Seq")]
    public class ZbTaskSeqRow
    {
        public int Id { get; set; }

        public short Seq { get; set; }
    }

    [DwEntity(DefaultOrder = "GroupName desc")]
    public class ZbTaskGroupRow
    {
        public int Id { get; set; }

        public string GroupName { get; set; } = string.Empty;
    }

    public sealed class ZbDefaultOrderContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZbDefaultOrderContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZbTask> Tasks => Set<ZbTask>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZbTask>().Property<short>("status_seq");
    }

    public sealed class ReviewDefaultOrderTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZbDefaultOrderContext _db;

        public ReviewDefaultOrderTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ZbDefaultOrderContext(_connection);
            _db.Database.EnsureCreated();

            // Inserted B, C, A: none of the expected orders is the insertion order.
            foreach ((string code, short seq, string group) in new[] { ("B", (short)2, "g2"), ("C", (short)1, "g3"), ("A", (short)3, "g1") })
            {
                ZbTask task = new() { Code = code, Group = new ZbTaskGroup { Name = group } };

                _db.Tasks.Add(task);
                _db.Entry(task).Property("status_seq").CurrentValue = seq;
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private string Log(string label, IEnumerable<int> ids)
        {
            string order = string.Join(",", ids.Select(id => _db.Tasks.AsNoTracking().Single(t => t.Id == id).Code));
            string line = $"{label}: {order}";

            _out.WriteLine(line);

            return order;
        }

        [Fact]
        public void Zb_G1_a_projected_row_assigning_a_column_takes_Code_desc()
        {
            IQueryable<ZbTaskRow> rows = _db.Tasks.Select(t => new ZbTaskRow { Id = t.Id, Code = t.Code });

            Assert.Equal("C,B,A", Log("G1 typed", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id)));
            Assert.Equal("C,B", Log("G1 paged", Guard(rows).ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 2 } }).Data.Select(r => r.Id)));
        }

        [Fact]
        public void Zb_G1b_a_projected_row_with_a_denied_member_still_takes_Code_desc()
        {
            IQueryable<ZbTaskSecretRow> rows = _db.Tasks.Select(t => new ZbTaskSecretRow { Id = t.Id, Code = t.Code, Secret = t.Code });

            Assert.Equal("C,B,A", Log("G1b typed", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id)));
        }

        [Fact]
        public void Zb_G2_a_projected_row_bound_through_EF_Property_takes_its_default()
        {
            IQueryable<ZbTaskSeqRow> rows = _db.Tasks.Select(t => new ZbTaskSeqRow { Id = t.Id, Seq = EF.Property<short>(t, "status_seq") });

            Assert.Equal("C,B,A", Log("G2 typed", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id)));
        }

        [Fact]
        public void Zb_G3_a_projected_row_reading_through_a_reference_navigation_takes_its_default()
        {
            IQueryable<ZbTaskGroupRow> rows = _db.Tasks.Select(t => new ZbTaskGroupRow { Id = t.Id, GroupName = t.Group!.Name });

            Assert.Equal("C,B,A", Log("G3 typed", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id)));
        }

        /// <summary>Informational: a computed value is left unordered by design since 6c9e171.</summary>
        [Fact]
        public void Zb_G4_a_projected_row_assigning_a_computed_value()
        {
            IQueryable<ZbTaskRow> rows = _db.Tasks.Select(t => new ZbTaskRow { Id = t.Id, Code = t.Code.Trim() });

            Log("G4 typed (Trim)", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id));
        }

        /// <summary>Informational: a projection over an anonymous intermediate row.</summary>
        [Fact]
        public void Zb_G5_a_projected_row_over_an_anonymous_intermediate()
        {
            IQueryable<ZbTaskRow> rows = _db.Tasks.Select(t => new { t.Id, t.Code }).Select(x => new ZbTaskRow { Id = x.Id, Code = x.Code });

            Log("G5 typed (anonymous intermediate)", Guard(rows).ToList(new Filter()).Data.Select(r => r.Id));
        }
    }
}
