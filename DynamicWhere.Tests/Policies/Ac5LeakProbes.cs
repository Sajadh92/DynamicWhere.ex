using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
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
    // =============================================================================================
    // Round 5, adversarial security review of 3.3.0 at 692dd11.
    //
    // End-to-end probes, through PolicyQueryable and a real database, for the channels round 4's
    // fixes did not touch: what a refusal still names under Strict, and what an audited field's
    // record says when the caller names no projection.
    // =============================================================================================

    /// <summary>An employee whose identifier is audited and whose note is sealed.</summary>
    public class Ac5Emp
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Audited for every feature: one event per use.</summary>
        [DwAudit]
        public string NationalId { get; set; } = string.Empty;

        /// <summary>Denied outright, so a projection has to be synthesized.</summary>
        [DwDenied]
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>The same shape with nothing denied, so no projection is synthesized at all.</summary>
    public class Ac5Open
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAudit]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>A masked identifier, so the transform refusals can be reached.</summary>
    public class Ac5Masked
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Hash)]
        [DwNoOrder]
        public string NationalId { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Partial, KeepEnd = 2)]
        [DwNoOrder]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>A scope the caller has to supply themselves.</summary>
    public class Ac5Scoped
    {
        public int Id { get; set; }

        [DwRequireWhere]
        public int TenantId { get; set; }

        public decimal Amount { get; set; }
    }

    public sealed class Ac5LeakProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Ac5Db _db;

        public Ac5LeakProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Ac5Db(_connection);
            _db.Database.EnsureCreated();

            _db.Emps.Add(new Ac5Emp { Id = 1, Name = "Ada", NationalId = "AAA-111", Note = "founder" });
            _db.Opens.Add(new Ac5Open { Id = 1, Name = "Ada", NationalId = "AAA-111" });
            _db.Masked.Add(new Ac5Masked { Id = 1, Name = "Ada", NationalId = "AAA-111", Email = "ada@x.com" });
            _db.Scoped.Add(new Ac5Scoped { Id = 1, TenantId = 5, Amount = 10m });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness -------------------------------------------------------------------------

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict, string? salt = null)
        {
            DwPolicyOptions options = new() { Tier = tier, Caps = { MinGroupSize = 1 } };

            if (salt is not null)
            {
                options.HashSalt = salt;
            }

            return options;
        }

        private static PolicyQueryable<T> Guard<T>(
            IQueryable<T> source, DwPolicyContext context, DwPolicyOptions options)
            where T : class =>
            source.ApplyPolicy(context, options, Attributes());

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}",
            LogicException failure => $"LogicException|{failure.Message}|subject={failure.Subject ?? "-"}",
            _ => $"{error.GetType().Name}: {error.Message.Split('\n')[0]}"
        };

        private static Exception? Catch(Action run)
        {
            try
            {
                run();

                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }

        // =========================================================================================
        // FINDING. An audited field the caller never names is returned and never recorded.
        // =========================================================================================

        /// <summary>
        /// The caller sends a filter with no <c>Selects</c>. The library synthesizes one, keeps the
        /// audited field in it, and hands back its real value — with nothing written to the audit.
        /// </summary>
        [Fact]
        public void An_audited_field_is_recorded_whether_or_not_the_request_names_it()
        {
            DwPolicyOptions options = Options();

            // (a) The caller names nothing.
            DwPolicyContext silent = Caller();

            FilterResult<Ac5Emp> whole = Guard(_db.Emps.AsNoTracking(), silent, options).ToList(new Filter());

            _out.WriteLine($"no projection  : value={whole.Data[0].NationalId}"
                + $" note='{whole.Data[0].Note}' events={silent.PendingAuditEvents.Count}");

            // (b) The same caller names the field.
            DwPolicyContext named = Caller();

            FilterResult<Ac5Emp> spelled = Guard(_db.Emps.AsNoTracking(), named, options)
                .ToList(new Filter { Selects = new List<string> { "Id", "NationalId" } });

            _out.WriteLine($"named          : value={spelled.Data[0].NationalId}"
                + $" events={named.PendingAuditEvents.Count}"
                + $" [{string.Join(",", named.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}]");

            // The denial was honoured, so the projection really was synthesized.
            Assert.Equal(string.Empty, whole.Data[0].Note);

            // The audited value reached the caller either way.
            Assert.Equal("AAA-111", whole.Data[0].NationalId);
            Assert.Equal("AAA-111", spelled.Data[0].NationalId);

            // Naming it writes a record.
            Assert.Contains(
                named.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);

            // The caller receives the value, so the log says so: since 3.3.0 the members a
            // synthesized projection returns are recorded as read, which closes an empty Selects as
            // a way past the control.
            Assert.Contains(
                silent.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);
        }

        /// <summary>
        /// With nothing denied the row comes back whole, so the audited column is read straight off
        /// the table and the buffer is still empty.
        /// </summary>
        [Fact]
        public void An_audited_field_a_whole_entity_read_returns_is_recorded()
        {
            DwPolicyContext context = Caller();

            FilterResult<Ac5Open> rows =
                Guard(_db.Opens.AsNoTracking(), context, Options()).ToList(new Filter());

            _out.WriteLine($"value={rows.Data[0].NationalId} events={context.PendingAuditEvents.Count}");

            Assert.Equal("AAA-111", rows.Data[0].NationalId);
            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);
        }

        /// <summary>The convenience tier reads the same way, so the gap is not tier-specific.</summary>
        [Fact]
        public void The_record_is_written_in_both_tiers()
        {
            DwPolicyContext context = Caller();

            FilterResult<Ac5Open> rows = Guard(_db.Opens.AsNoTracking(), context, Options(DwTier.Convenience))
                .ToList(new Filter());

            Assert.Equal("AAA-111", rows.Data[0].NationalId);
            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);
        }

        // =========================================================================================
        // What a strict refusal still names, across the codes that are not field denials.
        // =========================================================================================

        /// <summary>
        /// <c>TransformRequiresMaterialization</c> carries every transformed path on the type as its
        /// <c>FieldPath</c>, under <c>Strict</c>.
        /// </summary>
        [Fact]
        public void Strict_transform_refusal_names_the_clause_and_not_the_masked_fields()
        {
            DwPolicyContext context = Caller();

            PolicyQueryable<Ac5Masked> guarded = Guard(_db.Masked.AsNoTracking(), context, Options());

            Exception? error = Catch(() => guarded.SelectDynamic(new List<string> { "Id" }));

            _out.WriteLine($"SelectDynamic : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.TransformRequiresMaterialization, refusal.ErrorCode);

            // The clause, not the columns: the list was every transformed column on the type, handed
            // to a caller who named none of them.
            Assert.Equal("*", refusal.FieldPath);
        }

        /// <summary>
        /// <c>MissingHashSalt</c> names the masked field under <c>Strict</c>, where a field refusal
        /// names none.
        /// </summary>
        [Fact]
        public void Strict_missing_salt_refusal_names_the_clause()
        {
            DwPolicyContext context = Caller();

            Exception? error = Catch(
                () => Guard(_db.Masked.AsNoTracking(), context, Options()).ToList(new Filter()));

            _out.WriteLine($"no salt : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.MissingHashSalt, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        /// <summary>
        /// <c>RequiredFilterMissing</c> names the force-filtered field under <c>Strict</c> and puts
        /// it in the origin as well. Documented as deliberate; recorded here so it stays a choice.
        /// </summary>
        [Fact]
        public void Strict_required_filter_refusal_names_the_field()
        {
            DwPolicyContext context = Caller();

            Exception? error = Catch(
                () => Guard(_db.Scoped.AsNoTracking(), context, Options()).ToList(new Filter()));

            _out.WriteLine($"required : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.RequiredFilterMissing, refusal.ErrorCode);
            Assert.Equal("TenantId", refusal.FieldPath);
            Assert.Contains("TenantId", refusal.SourceOrigin);
        }

        /// <summary>
        /// The salted deployment answers, so the refusal above is a deployment state rather than a
        /// standing one.
        /// </summary>
        [Fact]
        public void Salted_deployment_answers()
        {
            DwPolicyContext context = Caller();

            FilterResult<Ac5Masked> rows =
                Guard(_db.Masked.AsNoTracking(), context, Options(salt: "a-long-enough-salt-value"))
                    .ToList(new Filter());

            _out.WriteLine($"hashed : {rows.Data[0].NationalId}");

            Assert.NotEqual("AAA-111", rows.Data[0].NationalId);
        }

        public sealed class Ac5Db : DbContext
        {
            private readonly SqliteConnection _connection;

            public Ac5Db(SqliteConnection connection) => _connection = connection;

            public DbSet<Ac5Emp> Emps => Set<Ac5Emp>();

            public DbSet<Ac5Open> Opens => Set<Ac5Open>();

            public DbSet<Ac5Masked> Masked => Set<Ac5Masked>();

            public DbSet<Ac5Scoped> Scoped => Set<Ac5Scoped>();

            protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                options.UseSqlite(_connection);
        }
    }
}
