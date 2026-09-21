using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
    /// <summary>A department an employee points at, which nothing includes.</summary>
    public class Ar7Dept
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    /// <summary>An employee whose audited navigation is never loaded.</summary>
    public class Ar7Employee
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int DeptId { get; set; }

        /// <summary>Audited, allowed, and not loaded by a query that does not include it.</summary>
        [DwAudit]
        public Ar7Dept? Dept { get; set; }
    }

    /// <summary>A staff record whose audited column a source projection may leave unassigned.</summary>
    public class Ar7Partial
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Audited and allowed: the question is whether a use is recorded when it is not read.</summary>
        [DwAudit]
        public string NationalId { get; set; } = string.Empty;
    }

    public sealed class Ar7EfContext : DbContext
    {
        public Ar7EfContext(DbContextOptions<Ar7EfContext> options) : base(options)
        {
        }

        public DbSet<Ar7Dept> Depts => Set<Ar7Dept>();

        public DbSet<Ar7Employee> Employees => Set<Ar7Employee>();

        public DbSet<Ar7Partial> Partials => Set<Ar7Partial>();
    }

    public sealed class Ar7EfAuditProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<Ar7EfContext> _options;

        public Ar7EfAuditProbes(ITestOutputHelper output)
        {
            _out = output;

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<Ar7EfContext>().UseSqlite(_connection).Options;

            using Ar7EfContext seed = new(_options);

            seed.Database.EnsureCreated();
            seed.Depts.Add(new Ar7Dept { Id = 1, Title = "Engineering" });
            seed.Employees.Add(new Ar7Employee { Id = 1, Name = "Ada", DeptId = 1 });
            seed.Partials.Add(new Ar7Partial { Id = 1, Name = "Ada", NationalId = "AAA-111" });
            seed.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private static DwPolicyContext Caller()
            => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, AuditRefusals = true, Caps = { MinGroupSize = 1 } };

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing recorded)"
                : string.Join(
                    "; ",
                    context.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}:{e.Effect}"));

        /// <summary>
        /// CANDIDATE. Nothing is denied on this type, so no projection is synthesized and every
        /// member goes into <c>readable</c> — the audited navigation included, although the query
        /// never loads it and the caller receives null.
        /// </summary>
        [Fact]
        public void An_unloaded_audited_navigation_is_recorded_as_used()
        {
            using Ar7EfContext db = new(_options);

            DwPolicyContext context = Caller();

            List<Ar7Employee> got = db.Employees
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows  : Dept is {(got[0].Dept is null ? "null (never loaded)" : "loaded")}");
            _out.WriteLine($"audit : {Recorded(context)}");

            Assert.Null(got[0].Dept);

            // Nothing is denied on this type, so no projection is synthesized and the row comes
            // back whole — but a navigation nothing loads is not part of it. The audit records what
            // the query hands back, not every member the caller may select.
            Assert.DoesNotContain(context.PendingAuditEvents, e => e.FieldPath == "Dept");
        }

        /// <summary>
        /// CANDIDATE. The source already projected, leaving the audited column unassigned, and the
        /// guarded read still records a use of it.
        /// </summary>
        [Fact]
        public void An_unassigned_audited_column_of_a_projected_source_is_recorded_as_used()
        {
            using Ar7EfContext db = new(_options);

            DwPolicyContext context = Caller();

            IQueryable<Ar7Partial> projected =
                db.Partials.Select(p => new Ar7Partial { Id = p.Id, Name = p.Name });

            List<Ar7Partial> got = projected
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows  : NationalId='{got[0].NationalId}' (source never read the column)");
            _out.WriteLine($"audit : {Recorded(context)}");

            Assert.Equal(string.Empty, got[0].NationalId);

            // The same, for a column the source projection never assigned: the row carries no value
            // for it, so reading it is not something the log has to answer for.
            Assert.DoesNotContain(context.PendingAuditEvents, e => e.FieldPath == "NationalId");
        }

        /// <summary>
        /// What the over-recording costs: the buffer is spent on members the query never read, and
        /// <c>MaxAuditEvents</c> fails closed, so a read the caller is entitled to is refused.
        /// </summary>
        [Fact]
        public void A_use_recorded_for_a_member_never_read_can_refuse_the_query()
        {
            using Ar7EfContext db = new(_options);

            DwPolicyContext context = Caller();

            DwPolicyOptions options = Posture();

            // Room for nothing beyond the members the query does read. Employees reads Id, Name and
            // DeptId; only the unloaded Dept is audited.
            options.Caps.MaxAuditEvents = 1;

            List<Ar7Employee> first = db.Employees
                .ApplyPolicy(context, options, Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"first read  : {first.Count} row(s), audit={Recorded(context)}");

            Exception? second = null;

            try
            {
                db.Employees.ApplyPolicy(context, options, Attributes()).ToList(new Filter());
            }
            catch (Exception error)
            {
                second = error;
            }

            _out.WriteLine($"second read : {second?.GetType().Name ?? "OK"} "
                           + $"{(second as DynamicWhere.ex.Exceptions.PolicyException)?.ErrorCode.ToString() ?? string.Empty}");

            // The second read is answered: nothing was spent on a navigation neither read loaded,
            // so the buffer a fail-closed cap watches is not filled by reads that did not happen.
            Assert.Null(second);
            Assert.DoesNotContain(context.PendingAuditEvents, e => e.FieldPath == "Dept");
        }
    }
}
