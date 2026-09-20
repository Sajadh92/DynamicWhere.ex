using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Tokens;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// Round six of the docs-against-code probe for 3.3.0. Every claim is read by running the library.
// Nothing here fixes anything, and nothing here drives DwPolicy's static fields, by reflection or
// otherwise: every handle is built from its own DwPolicyOptions and its own resolver.

namespace DynamicWhere.Tests.Policies
{
    // ---- models ---------------------------------------------------------------------------------

    /// <summary>
    /// A type whose transforms are NOT all masks: one truncation, one generalization, one mask.
    /// </summary>
    /// <remarks>
    /// The point the README and the security page make is that the refusal listed "every masked
    /// column". If a truncated-only and a generalized-only column appear in the list too, the list
    /// is of transformed columns, which is what DOC.md, the release notes and the breaking-changes
    /// page say.
    /// </remarks>
    internal class Hx6Staffer
    {
        public int Id { get; set; }

        public string Department { get; set; } = string.Empty;

        /// <summary>Shortened, never masked.</summary>
        [DwTruncate(3)]
        public string Note { get; set; } = string.Empty;

        /// <summary>Rounded, never masked.</summary>
        [DwGeneralize(GeneralizeMode.Round, Step = 100)]
        public decimal Salary { get; set; }

        /// <summary>The one column that really is masked.</summary>
        [DwMask(MaskStrategy.Full)]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>Two rows whose keys collide once rounded, so the grouping key is ambiguous.</summary>
    internal class Hx6Banded
    {
        public int Id { get; set; }

        [DwGeneralize(GeneralizeMode.Round, Step = 100)]
        public decimal Band { get; set; }
    }

    /// <summary>A hashed column with no salt configured, and a tokenized one with no vault.</summary>
    internal class Hx6Secret
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Hash)]
        public string Hashed { get; set; } = string.Empty;
    }

    internal class Hx6Tokenized
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Tokenize)]
        public string Ticket { get; set; } = string.Empty;
    }

    /// <summary>An audited field, recorded for every feature.</summary>
    internal class Hx6Audited
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwAudit]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>A line of an order, with a getter no database computes.</summary>
    public class Hx6Line
    {
        public int Id { get; set; }

        public string Sku { get; set; } = string.Empty;

        public int Hx6OrderId { get; set; }

        /// <summary>Computed in memory from the column beside it.</summary>
        public bool IsBlank => Sku.Length == 0;
    }

    public class Hx6Order
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public List<Hx6Line> Lines { get; set; } = new();
    }

    /// <summary>The row a caller projects before the guard sees it.</summary>
    public class Hx6Row
    {
        public int Id { get; set; }

        public List<Hx6Line> Lines { get; set; } = new();
    }

    /// <summary>The same row, but its lines are rebuilt by a subquery of its own.</summary>
    public class Hx6LineRow
    {
        public string Sku { get; set; } = string.Empty;

        public bool IsBlank => Sku.Length == 0;
    }

    public class Hx6BuiltRow
    {
        public int Id { get; set; }

        public List<Hx6LineRow> Lines { get; set; } = new();
    }

    public sealed class Hx6Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public Hx6Context(SqliteConnection connection) => _connection = connection;

        public DbSet<Hx6Order> Orders => Set<Hx6Order>();

        public DbSet<Hx6Line> Lines => Set<Hx6Line>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    // ---- shared plumbing --------------------------------------------------------------------------

    internal static class Hx6
    {
        private static readonly MethodInfo OfMethod = typeof(RowShape)
            .GetMethod("Of", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        private static readonly MethodInfo ExpressesMethod = typeof(RowShape)
            .GetMethod("Expresses", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>What the shape of a source says about a path: true, false, or "cannot say".</summary>
        internal static bool? Ask(IQueryable source, string path)
        {
            object shape = OfMethod.MakeGenericMethod(source.ElementType).Invoke(null, new object[] { source })!;

            return (bool?)ExpressesMethod.Invoke(shape, new object[] { path });
        }

        internal static string Show(bool? answer) => answer switch
        {
            null => "null  (left alone)",
            true => "true  (nothing to refuse)",
            _ => "false (refused)"
        };

        internal static DwPolicyOptions Options(
            DwTier tier,
            bool dryRun = false,
            string? salt = null,
            IDwTokenVault? vault = null,
            int? floor = null)
        {
            DwPolicyOptions options = new() { Tier = tier, DryRun = dryRun };

            if (floor is { } size)
            {
                // The shipped floor is 5, which removes a group of one before anything collides.
                options.Caps.MinGroupSize = size;
            }

            if (salt is not null)
            {
                options.HashSalt = salt;
            }

            if (vault is not null)
            {
                options.TokenVault = vault;
            }

            options.Freeze();

            return options;
        }

        internal static PolicyQueryable<T> Guard<T>(
            IQueryable<T> source, DwPolicyOptions options, DwPolicyContext? context = null)
            where T : class =>
            source.ApplyPolicy(
                context ?? new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                options,
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        internal static Filter Where(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field,
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { value }
                    }
                }
            }
        };

        internal static Summary ByBand() => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Band" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
                }
            }
        };

        internal static string Origin(PolicyException error) =>
            error.SourceOrigin is null ? "<null>" : $"\"{error.SourceOrigin}\"";
    }

    // ---- 1. the four refusals that name the clause ------------------------------------------------

    /// <summary>
    /// Whether each of the four really reports <c>"*"</c> <i>and no origin</i> under Strict.
    /// </summary>
    public class Hx6NamesTheClauseProbe
    {
        private readonly ITestOutputHelper _out;

        public Hx6NamesTheClauseProbe(ITestOutputHelper output) => _out = output;

        private static Hx6Staffer[] Staff() => new[]
        {
            new Hx6Staffer { Id = 1, Department = "Ops", Note = "abcdef", Salary = 120m, NationalId = "A1" }
        };

        private static Hx6Banded[] Banded() => new[]
        {
            new Hx6Banded { Id = 1, Band = 100m },
            new Hx6Banded { Id = 2, Band = 149m }
        };

        /// <summary>
        /// <c>TransformRequiresMaterialization</c> under Strict: the field path, and the origin.
        /// </summary>
        [Fact]
        public void Transform_requires_materialization_reports_star_and_an_origin()
        {
            PolicyQueryable<Hx6Staffer> guarded =
                Hx6.Guard(Staff().AsQueryable(), Hx6.Options(DwTier.Strict, salt: new string('s', 16)));

            PolicyException error = Assert.Throws<PolicyException>(
                () => guarded.SelectDynamic(new List<string> { "Id" }));

            _out.WriteLine($"code      = {error.ErrorCode}");
            _out.WriteLine($"FieldPath = \"{error.FieldPath}\"");
            _out.WriteLine($"origin    = {Hx6.Origin(error)}");

            Assert.Equal(PolicyErrorCode.TransformRequiresMaterialization, error.ErrorCode);
            Assert.Equal("*", error.FieldPath);

            // The documents that say "the three report '*' and no origin" are wrong about this one.
            Assert.NotNull(error.SourceOrigin);
        }

        /// <summary>
        /// And the other three really do carry no origin, so the difference is this one refusal.
        /// </summary>
        [Fact]
        public void The_other_three_report_star_with_no_origin()
        {
            PolicyException group = Assert.Throws<PolicyException>(
                () => Hx6.Guard(Banded().AsQueryable(), Hx6.Options(DwTier.Strict, floor: 1)).ToList(Hx6.ByBand()));

            PolicyException salt = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Secret { Id = 1, Hashed = "AAA-111" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict))
                    .ToList(new Filter()));

            PolicyException vault = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Tokenized { Id = 1, Ticket = "T-1" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict))
                    .ToList(new Filter()));

            foreach (PolicyException error in new[] { group, salt, vault })
            {
                _out.WriteLine($"{error.ErrorCode,-34} FieldPath=\"{error.FieldPath}\" origin={Hx6.Origin(error)}");

                Assert.Equal("*", error.FieldPath);
                Assert.Null(error.SourceOrigin);
            }

            Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, group.ErrorCode);
            Assert.Equal(PolicyErrorCode.MissingHashSalt, salt.ErrorCode);
            Assert.Equal(PolicyErrorCode.MissingTokenVault, vault.ErrorCode);
        }

        /// <summary>
        /// Convenience is unchanged: each names the field, and the list is of transformed columns.
        /// </summary>
        [Fact]
        public void Convenience_still_names_the_field()
        {
            PolicyException listed = Assert.Throws<PolicyException>(
                () => Hx6.Guard(Staff().AsQueryable(), Hx6.Options(DwTier.Convenience, salt: new string('s', 16)))
                    .SelectDynamic(new List<string> { "Id" }));

            PolicyException group = Assert.Throws<PolicyException>(
                () => Hx6.Guard(Banded().AsQueryable(), Hx6.Options(DwTier.Convenience, floor: 1)).ToList(Hx6.ByBand()));

            PolicyException salt = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Secret { Id = 1, Hashed = "AAA-111" } }.AsQueryable(),
                        Hx6.Options(DwTier.Convenience))
                    .ToList(new Filter()));

            _out.WriteLine($"TransformRequiresMaterialization FieldPath = \"{listed.FieldPath}\"");
            _out.WriteLine($"AmbiguousGroupKey                FieldPath = \"{group.FieldPath}\" origin={Hx6.Origin(group)}");
            _out.WriteLine($"MissingHashSalt                  FieldPath = \"{salt.FieldPath}\"");

            // Every transformed column, not every masked one: Note is truncated and Salary rounded.
            Assert.Contains("Note", listed.FieldPath);
            Assert.Contains("Salary", listed.FieldPath);
            Assert.Contains("NationalId", listed.FieldPath);

            Assert.Equal("Band", group.FieldPath);
            Assert.Equal("Hashed", salt.FieldPath);
        }

        /// <summary>
        /// The global dry run is unchanged — and so, the documents say, is "a dry run". The
        /// per-context one is the other half of what the library calls a dry run everywhere else.
        /// </summary>
        [Fact]
        public void A_dry_run_names_the_field_whichever_switch_declares_it()
        {
            PolicyException global = Assert.Throws<PolicyException>(
                () => Hx6.Guard(Banded().AsQueryable(), Hx6.Options(DwTier.Strict, dryRun: true, floor: 1))
                    .ToList(Hx6.ByBand()));

            PolicyException percall = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        Banded().AsQueryable(),
                        Hx6.Options(DwTier.Strict, floor: 1),
                        new DwPolicyContext { DryRun = true }.WithSubject(DwSubjectKind.User, "u1"))
                    .ToList(Hx6.ByBand()));

            _out.WriteLine($"options.DryRun = true  -> FieldPath=\"{global.FieldPath}\" origin={Hx6.Origin(global)}");
            _out.WriteLine($"context.DryRun = true  -> FieldPath=\"{percall.FieldPath}\" origin={Hx6.Origin(percall)}");

            Assert.Equal("Band", global.FieldPath);
            Assert.NotNull(global.SourceOrigin);

            // A dry run is either switch, the posture's or the caller's, as it is everywhere else in
            // the layer: a canary subject previewing a posture must not meet the hidden refusal.
            Assert.Equal("Band", percall.FieldPath);
            Assert.NotNull(percall.SourceOrigin);
        }

        /// <summary>
        /// The per-context dry run is honoured by the refusal the same page describes one section
        /// earlier, so the two are not consistent with each other.
        /// </summary>
        [Fact]
        public void A_context_dry_run_is_honoured_by_the_unknown_name_gate()
        {
            // Strict, but the caller's own context is the canary: the gate stops hiding existence,
            // so an unknown name fails as it would unguarded rather than as a field refusal.
            LogicException unguarded = Assert.Throws<LogicException>(
                () => Hx6.Guard(
                        Staff().AsQueryable(),
                        Hx6.Options(DwTier.Strict, salt: new string('s', 16)),
                        new DwPolicyContext { DryRun = true }.WithSubject(DwSubjectKind.User, "u1"))
                    .ToList(Hx6.Where("NoSuchField", "x")));

            _out.WriteLine($"context dry run, unknown name -> {unguarded.GetType().Name}: {unguarded.Message}");

            // Without it, the same name is a field refusal naming nothing.
            PolicyException hidden = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        Staff().AsQueryable(),
                        Hx6.Options(DwTier.Strict, salt: new string('s', 16)))
                    .ToList(Hx6.Where("NoSuchField", "x")));

            _out.WriteLine($"no dry run,      unknown name -> {hidden.ErrorCode} \"{hidden.FieldPath}\"");
        }
    }

    // ---- 2. an ambiguous name -----------------------------------------------------------------------

    /// <summary>A type with one alias pointing at two different members.</summary>
    internal class Hx6Ambiguous
    {
        public int Id { get; set; }

        [DwAlias("ref")]
        public string Primary { get; set; } = string.Empty;

        [DwAlias("ref")]
        public string Secondary { get; set; } = string.Empty;
    }

    public class Hx6AmbiguousNameProbe
    {
        private readonly ITestOutputHelper _out;

        public Hx6AmbiguousNameProbe(ITestOutputHelper output) => _out = output;

        private static Hx6Ambiguous[] Rows() => new[]
        {
            new Hx6Ambiguous { Id = 1, Primary = "a", Secondary = "b" }
        };

        /// <summary>
        /// Under Strict the ambiguous name is refused as an unknown name is, and the trace names the
        /// fields it matched.
        /// </summary>
        [Fact]
        public void An_ambiguous_name_is_refused_as_an_unknown_name_is()
        {
            PolicyQueryable<Hx6Ambiguous> guarded =
                Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Strict));

            PolicyException ambiguous = Assert.Throws<PolicyException>(
                () => guarded.ToList(Hx6.Where("ref", "a")));

            PolicyQueryable<Hx6Ambiguous> second =
                Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Strict));

            PolicyException unknown = Assert.Throws<PolicyException>(
                () => second.ToList(Hx6.Where("NoSuchField", "a")));

            _out.WriteLine($"ambiguous: {ambiguous.ErrorCode} \"{ambiguous.FieldPath}\" origin={Hx6.Origin(ambiguous)}");
            _out.WriteLine($"unknown  : {unknown.ErrorCode} \"{unknown.FieldPath}\" origin={Hx6.Origin(unknown)}");

            Assert.Equal(unknown.ErrorCode, ambiguous.ErrorCode);
            Assert.Equal(unknown.FieldPath, ambiguous.FieldPath);
            Assert.Equal(unknown.SourceOrigin, ambiguous.SourceOrigin);

            PolicyTrace? trace = guarded.LastTrace;

            Assert.NotNull(trace);

            foreach (PolicyDecision decision in trace!.Decisions)
            {
                _out.WriteLine($"  trace: {decision.FieldPath} {decision.Action} — {decision.Reason}");
            }

            Assert.Contains(
                trace.Decisions,
                decision => decision.Reason is not null
                            && decision.Reason.Contains("Primary")
                            && decision.Reason.Contains("Secondary"));
        }

        /// <summary>Convenience still answers <c>AmbiguousFieldName</c>.</summary>
        [Fact]
        public void Convenience_still_answers_ambiguous_field_name()
        {
            PolicyException error = Assert.Throws<PolicyException>(
                () => Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Convenience))
                    .ToList(Hx6.Where("ref", "a")));

            _out.WriteLine($"{error.ErrorCode} \"{error.FieldPath}\"");

            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, error.ErrorCode);
        }
    }

    // ---- 3. what [DwAudit] records ------------------------------------------------------------------

    public class Hx6AuditProbe
    {
        private readonly ITestOutputHelper _out;

        public Hx6AuditProbe(ITestOutputHelper output) => _out = output;

        private static Hx6Audited[] Rows() => new[]
        {
            new Hx6Audited { Id = 1, Code = "c1", NationalId = "A-1" }
        };

        /// <summary>
        /// A request that sends no <c>Selects</c> reads the audited field and records nothing.
        /// </summary>
        [Fact]
        public void A_request_with_no_selects_reads_the_field_and_records_it()
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            FilterResult<Hx6Audited> result =
                Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Strict), context).ToList(new Filter());

            _out.WriteLine($"rows           = {result.Data.Count}");
            _out.WriteLine($"NationalId     = \"{result.Data[0].NationalId}\"");
            _out.WriteLine($"audit events   = {context.PendingAuditEvents.Count}");

            Assert.Equal("A-1", result.Data[0].NationalId);

            // The caller receives the value, so the log says so: a use is what the request reads,
            // not only what it spells out.
            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);
        }

        /// <summary>Naming the field records it, which is the documented way to be recorded.</summary>
        [Fact]
        public void Naming_the_field_records_it()
        {
            DwPolicyContext named = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Strict), named)
                .ToList(new Filter { Selects = new List<string> { "Id", "NationalId" } });

            DwPolicyContext filtered = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            Hx6.Guard(Rows().AsQueryable(), Hx6.Options(DwTier.Strict), filtered)
                .ToList(Hx6.Where("NationalId", "A-1"));

            foreach (DwAuditEvent recorded in named.PendingAuditEvents)
            {
                _out.WriteLine($"Selects  -> {recorded.FieldPath} {recorded.Feature}");
            }

            foreach (DwAuditEvent recorded in filtered.PendingAuditEvents)
            {
                _out.WriteLine($"Where    -> {recorded.FieldPath} {recorded.Feature}");
            }

            Assert.Contains(named.PendingAuditEvents, e => e.FieldPath == "NationalId");
            Assert.Contains(filtered.PendingAuditEvents, e => e.FieldPath == "NationalId");
        }
    }

    // ---- 4. the member a sequence operator hides ----------------------------------------------------

    public class Hx6SequenceOperatorProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Hx6Context _db;

        public Hx6SequenceOperatorProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();
            _db = new Hx6Context(_connection);
            _db.Database.EnsureCreated();

            _db.Orders.Add(new Hx6Order
            {
                Id = 1,
                Code = "o1",
                Lines = { new Hx6Line { Id = 1, Sku = "s1" } }
            });

            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>
        /// Each of the three shapes the documents name, and the entity for contrast.
        /// </summary>
        [Fact]
        public void A_member_assigned_through_a_sequence_operator_is_left_alone()
        {
            IQueryable<Hx6Row> plain = _db.Orders
                .Select(o => new Hx6Row { Id = o.Id, Lines = o.Lines.ToList() });

            IQueryable<Hx6Row> filtered = _db.Orders
                .Select(o => new Hx6Row { Id = o.Id, Lines = o.Lines.Where(l => l.Sku != "").ToList() });

            IQueryable<Hx6BuiltRow> built = _db.Orders
                .Select(o => new Hx6BuiltRow
                {
                    Id = o.Id,
                    Lines = o.Lines.Select(l => new Hx6LineRow { Sku = l.Sku }).ToList()
                });

            bool? plainAnswer = Hx6.Ask(plain, "Lines.IsBlank");
            bool? filteredAnswer = Hx6.Ask(filtered, "Lines.IsBlank");
            bool? builtAnswer = Hx6.Ask(built, "Lines.IsBlank");
            bool? entityAnswer = Hx6.Ask(_db.Orders, "Lines.IsBlank");

            _out.WriteLine($"Lines = o.Lines.ToList()                        -> {Hx6.Show(plainAnswer)}");
            _out.WriteLine($"Lines = o.Lines.Where(...).ToList()             -> {Hx6.Show(filteredAnswer)}");
            _out.WriteLine($"Lines = o.Lines.Select(l => new ...).ToList()   -> {Hx6.Show(builtAnswer)}");
            _out.WriteLine($"the entity itself                               -> {Hx6.Show(entityAnswer)}");

            Assert.Null(plainAnswer);
            Assert.Null(filteredAnswer);
            Assert.Null(builtAnswer);

            // The entity is the contrast: there the member is refused.
            Assert.False(entityAnswer);
        }

        /// <summary>
        /// End to end: the entity refuses the path, the projection with the sequence operator does
        /// not — it reaches the provider and fails there, as an unguarded query does.
        /// </summary>
        [Fact]
        public void The_entity_refuses_and_the_projection_reaches_the_provider()
        {
            PolicyException refused = Assert.Throws<PolicyException>(
                () => Hx6.Guard(_db.Orders, Hx6.Options(DwTier.Strict))
                    .ToList(Hx6.Where("Lines.IsBlank", "true")));

            _out.WriteLine($"entity     : {refused.ErrorCode} \"{refused.FieldPath}\"");

            IQueryable<Hx6Row> projected = _db.Orders
                .Select(o => new Hx6Row { Id = o.Id, Lines = o.Lines.ToList() });

            Exception passed = Assert.ThrowsAny<Exception>(
                () => Hx6.Guard(projected, Hx6.Options(DwTier.Strict))
                    .ToList(Hx6.Where("Lines.IsBlank", "true")));

            _out.WriteLine($"projection : {passed.GetType().Name}");

            Assert.IsNotType<PolicyException>(passed);
        }
    }
}
