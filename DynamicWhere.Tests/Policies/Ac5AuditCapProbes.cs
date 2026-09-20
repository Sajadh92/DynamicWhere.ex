using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 5, adversarial security review of 3.3.0 at 692dd11.
    //
    // Round 4's fix: when DwCaps.MaxAuditEvents is reached and the tier hides existence, the
    // refusal is raised through Gate.Exception(...DenialFor(feature)...) rather than as
    // CapExceeded + SourceOrigin. These probes ask whether that refusal really is the same
    // refusal a denied field and an unknown name get, in every clause and both terminals, and
    // whether anything else can still tell the three apart.
    //
    // Everything here drives FilterSanitizer directly with an explicit posture, so no probe
    // touches DwPolicy's process-wide state.
    // =============================================================================================

    /// <summary>The model the round-5 audit-cap probes gate.</summary>
    /// <remarks>
    /// EF Core 6 compatible on purpose, so the floor leg runs every probe.
    /// </remarks>
    internal class Ac5Staff
    {
        public int Id { get; set; }

        /// <summary>Plain: allowed everywhere, audited nowhere.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Allowed everywhere and audited everywhere: one event per use.</summary>
        [DwAudit]
        public string Tag { get; set; } = string.Empty;

        /// <summary>Refused for every feature.</summary>
        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Weighed, so a cost refusal can be told from a structural one.</summary>
        [DwCost(50)]
        public string Heavy { get; set; } = string.Empty;

        /// <summary>Confirmable but not searchable.</summary>
        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Badge { get; set; } = string.Empty;

        public Ac5Contact? Contact { get; set; }
    }

    /// <summary>A nested node, so an audited field can sit behind a navigation.</summary>
    internal class Ac5Contact
    {
        [DwAudit]
        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;
    }

    /// <summary>
    /// An audited field on a type with nothing denied, so no projection is synthesized at all and
    /// the whole entity comes back.
    /// </summary>
    internal class Ac5Plain
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwAudit]
        public string Tag { get; set; } = string.Empty;
    }

    /// <summary>An audited field the type's declared default order names.</summary>
    [DwEntity(DefaultOrder = "Tag")]
    internal class Ac5Ordered
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwAudit]
        public string Tag { get; set; } = string.Empty;
    }

    public class Ac5AuditCapProbes
    {
        private readonly ITestOutputHelper _out;

        public Ac5AuditCapProbes(ITestOutputHelper output) => _out = output;

        private const string Audited = "Tag";
        private const string Denied = "Secret";
        private const string Missing = "NoSuchColumn";

        // ---- harness -------------------------------------------------------------------------

        private static DwPolicyContext Caller(bool dryRun = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            if (dryRun)
            {
                context.DryRun = true;
            }

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict, bool dryRun = false, int audits = 1) =>
            new()
            {
                Tier = tier,
                DryRun = dryRun,
                Caps = { MinGroupSize = 1, MaxAuditEvents = audits }
            };

        private static Condition On(string field, Operator op = Operator.Equal) =>
            new() { Sort = 0, Field = field, DataType = DataType.Text, Operator = op, Values = { "x" } };

        // Every clause names a projection, and names a field nothing audits. A request that sends no
        // Selects has one synthesized, and since 3.3.0 the members it returns are recorded as read —
        // so leaving Selects out here would spend the buffer before the clause under test is reached.
        private static Filter Where(string field, Operator op = Operator.Equal) =>
            new()
            {
                ConditionGroup = new ConditionGroup { Conditions = { On(field, op) } },
                Selects = new List<string> { "Code" }
            };

        private static Filter Order(string field) =>
            new()
            {
                Orders = new List<OrderBy> { new() { Field = field, Direction = Direction.Ascending } },
                Selects = new List<string> { "Code" }
            };

        private static Filter Select(params string[] fields) => new() { Selects = fields.ToList() };

        private static Summary Group(string field) => new()
        {
            GroupBy = new GroupBy { Fields = { field } }
        };

        private static Summary Aggregate(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = { "Code" },
                AggregateBy = { new AggregateBy { Field = field, Aggregator = Aggregator.Count, Alias = "n" } }
            }
        };

        private static Segment Set(string field) => new()
        {
            Selects = new List<string> { "Code" },
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup { Conditions = { On(field) } }
                }
            }
        };

        /// <summary>Runs one clause against one caller, so the audit buffer carries across calls.</summary>
        private static void Run(
            object clause, DwPolicyContext context, DwPolicyOptions options, PolicyTrace trace)
        {
            switch (clause)
            {
                case Filter filter:
                    FilterSanitizer.Sanitize<Ac5Staff>(filter, Attributes(), context, options, trace);

                    break;

                case Summary summary:
                    FilterSanitizer.Sanitize<Ac5Staff>(summary, Attributes(), context, options, trace);

                    break;

                case Segment segment:
                    FilterSanitizer.Sanitize<Ac5Staff>(segment, Attributes(), context, options, trace);

                    break;

                default:
                    throw new InvalidOperationException("unknown clause");
            }
        }

        /// <summary>
        /// Fills the caller's audit buffer to capacity, then runs <paramref name="clause"/> on the
        /// same caller so the next audited use overflows it.
        /// </summary>
        private static (Exception? Error, PolicyTrace Trace) WithFullBuffer(
            object clause, DwTier tier = DwTier.Strict, bool dryRun = false)
        {
            DwPolicyOptions options = Options(tier, dryRun);
            DwPolicyContext context = Caller();

            // One audited use fills a one-event buffer.
            Run(Where(Audited), context, options, new PolicyTrace(tier, dryRun));

            Assert.Single(context.PendingAuditEvents);

            PolicyTrace trace = new(tier, dryRun || options.DryRun);

            try
            {
                Run(clause, context, options, trace);

                return (null, trace);
            }
            catch (Exception error)
            {
                return (error, trace);
            }
        }

        /// <summary>Runs a clause on a fresh caller with a roomy buffer.</summary>
        private static (Exception? Error, PolicyTrace Trace) Plain(
            object clause, DwTier tier = DwTier.Strict, bool dryRun = false)
        {
            DwPolicyOptions options = Options(tier, dryRun, audits: 1000);
            DwPolicyContext context = Caller(dryRun);
            PolicyTrace trace = new(tier, dryRun);

            try
            {
                Run(clause, context, options, trace);

                return (null, trace);
            }
            catch (Exception error)
            {
                return (error, trace);
            }
        }

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|tier={refusal.Tier}|rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}"
                + $"|msg={refusal.Message}",
            LogicException failure => $"LogicException|{failure.Message}|subject={failure.Subject ?? "-"}",
            _ => error.GetType().Name
        };

        /// <summary>
        /// The three refusals a caller can tell apart if anything differs: the audit cap on a real
        /// audited field, the denial of a real field, and a name matching nothing.
        /// </summary>
        private void AssertThreeAlike(string what, object capped, object denied, object missing)
        {
            (Exception? cap, PolicyTrace capTrace) = WithFullBuffer(capped);
            (Exception? deny, _) = Plain(denied);
            (Exception? miss, _) = Plain(missing);

            _out.WriteLine($"--- {what}");
            _out.WriteLine($"  audit cap : {Shape(cap)}");
            _out.WriteLine($"  denied    : {Shape(deny)}");
            _out.WriteLine($"  unknown   : {Shape(miss)}");
            _out.WriteLine($"  trace     : {string.Join(" / ", capTrace.Decisions.Select(d => $"{d.FieldPath}:{d.Action}:{d.Reason}"))}");

            Assert.NotNull(cap);
            Assert.NotNull(deny);
            Assert.NotNull(miss);

            // The denial and the unknown name are the established pair; the cap has to join them.
            Assert.Equal(Shape(deny), Shape(miss));
            Assert.Equal(Shape(deny), Shape(cap));

            // And the trace still says which refusal it really was.
            Assert.Contains(
                capTrace.Decisions,
                decision => decision.Reason is { } reason && reason.Contains("MaxAuditEvents"));
        }

        // ---- 1. the cap refusal is the field refusal, in every clause --------------------------

        [Fact]
        public void Where_clause_audit_cap_is_indistinguishable()
            => AssertThreeAlike("Where", Where(Audited), Where(Denied), Where(Missing));

        [Fact]
        public void Order_clause_audit_cap_is_indistinguishable()
            => AssertThreeAlike("Order", Order(Audited), Order(Denied), Order(Missing));

        [Fact]
        public void Select_clause_audit_cap_is_indistinguishable()
            => AssertThreeAlike(
                "Select", Select("Code", Audited), Select("Code", Denied), Select("Code", Missing));

        [Fact]
        public void Group_clause_audit_cap_is_indistinguishable()
            => AssertThreeAlike("Group", Group(Audited), Group(Denied), Group(Missing));

        [Fact]
        public void Aggregate_clause_audit_cap_is_indistinguishable()
            => AssertThreeAlike("Aggregate", Aggregate(Audited), Aggregate(Denied), Aggregate(Missing));

        [Fact]
        public void Segment_audit_cap_is_indistinguishable()
            => AssertThreeAlike("Segment", Set(Audited), Set(Denied), Set(Missing));

        [Fact]
        public void Nested_audited_field_audit_cap_is_indistinguishable()
            => AssertThreeAlike(
                "Nested where",
                Where("Contact.Email"),
                Where("Contact." + Denied),
                Where("Contact." + Missing));

        // ---- 2. the cap still refuses; no record is dropped -------------------------------------

        [Fact]
        public void Cap_refuses_rather_than_dropping_the_record()
        {
            DwPolicyOptions options = Options();
            DwPolicyContext context = Caller();

            Run(Where(Audited), context, options, new PolicyTrace(DwTier.Strict, false));

            Assert.Single(context.PendingAuditEvents);

            Assert.Throws<PolicyException>(
                () => Run(Where(Audited), context, options, new PolicyTrace(DwTier.Strict, false)));

            // Still one: the overflowing use was refused, never silently written and never dropped
            // into a query that carried on.
            Assert.Single(context.PendingAuditEvents);
        }

        // ---- 3. Convenience and a dry run still answer CapExceeded ------------------------------

        [Fact]
        public void Convenience_still_answers_cap_exceeded()
        {
            (Exception? error, _) = WithFullBuffer(Where(Audited), DwTier.Convenience);

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            _out.WriteLine(Shape(refusal));

            Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
            Assert.Equal(Audited, refusal.FieldPath);
            Assert.Contains("MaxAuditEvents", refusal.SourceOrigin);
        }

        [Fact]
        public void Strict_dry_run_still_answers_cap_exceeded()
        {
            DwPolicyOptions options = Options(DwTier.Strict, dryRun: true);
            DwPolicyContext context = Caller();

            Run(Where(Audited), context, options, new PolicyTrace(DwTier.Strict, true));

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Run(Where(Audited), context, options, new PolicyTrace(DwTier.Strict, true)));

            _out.WriteLine(Shape(refusal));

            Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
            Assert.Contains("MaxAuditEvents", refusal.SourceOrigin);
        }

        // ---- 4. the cap is reached by uses the caller never wrote --------------------------------

        // ---- 4b. the other cap that fires before the policy is consulted -------------------------

        /// <summary>
        /// <c>MaxNavigationDepth</c> is measured after canonicalization and before any field is
        /// gated, so it is the other place a refusal is raised before the caller's policy is read.
        /// A real path, a denied one, and a name matching nothing must all answer alike.
        /// </summary>
        [Fact]
        public void Navigation_depth_cap_refuses_a_real_path_and_a_missing_one_alike()
        {
            DwPolicyOptions options = new()
            {
                Tier = DwTier.Strict,
                Caps = { MinGroupSize = 1, MaxNavigationDepth = 1 }
            };

            string Run(string field)
            {
                try
                {
                    FilterSanitizer.Sanitize<Ac5Staff>(
                        Where(field), Attributes(), Caller(), options, new PolicyTrace(DwTier.Strict, false));

                    return "OK";
                }
                catch (Exception error)
                {
                    return Shape(error);
                }
            }

            string real = Run("Contact.Phone");
            string audited = Run("Contact.Email");
            string missing = Run("NoSuch.Column");

            _out.WriteLine($"real deep    : {real}");
            _out.WriteLine($"audited deep : {audited}");
            _out.WriteLine($"missing deep : {missing}");

            Assert.StartsWith("CapExceeded", real);
            Assert.Equal(real, audited);
            Assert.Equal(real, missing);
        }

        // ---- 5. FINDING: a projection the library synthesizes audits nothing ---------------------

        /// <summary>
        /// A caller who names <c>Tag</c> in <c>Selects</c> is recorded. A caller who names no
        /// projection at all receives <c>Tag</c> in every row and is recorded nowhere.
        /// </summary>
        [Fact]
        public void Synthesized_projection_returns_an_audited_field_without_recording_it()
        {
            DwPolicyOptions options = Options(audits: 1000);

            // (a) The caller names the audited field: one Select event.
            DwPolicyContext named = Caller();

            Filter spelled = FilterSanitizer.Sanitize<Ac5Staff>(
                Select("Code", Audited), Attributes(), named, options, new PolicyTrace(DwTier.Strict, false));

            _out.WriteLine($"named selects   : {string.Join(",", spelled.Selects ?? new List<string>())}");
            _out.WriteLine($"named events    : {named.PendingAuditEvents.Count}"
                + $" [{string.Join(",", named.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}]");

            // (b) The same caller sends no projection. The library synthesizes one.
            DwPolicyContext silent = Caller();

            Filter synthesized = FilterSanitizer.Sanitize<Ac5Staff>(
                new Filter(), Attributes(), silent, options, new PolicyTrace(DwTier.Strict, false));

            _out.WriteLine($"synth selects   : {string.Join(",", synthesized.Selects ?? new List<string>())}");
            _out.WriteLine($"synth events    : {silent.PendingAuditEvents.Count}"
                + $" [{string.Join(",", silent.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}]");

            // The audited field really is in the projection the caller receives.
            Assert.NotNull(synthesized.Selects);
            Assert.Contains(Audited, synthesized.Selects!);

            // Naming it is recorded.
            Assert.Contains(
                named.PendingAuditEvents,
                e => e.FieldPath == Audited && e.Feature == PolicyFeature.Select);

            // A documented limit, not a defect fixed here: [DwAudit] records a field the request
            // names, and a field of the type's default order that the query adds. A request naming
            // no projection reads the row without naming anything, and records nothing. Closing that
            // redefines what a use is, which is a decision for its own release.
            Assert.DoesNotContain(
                silent.PendingAuditEvents,
                e => e.FieldPath == Audited && e.Feature == PolicyFeature.Select);
        }

        /// <summary>
        /// With nothing denied on the type no projection is synthesized either, so the whole entity
        /// comes back — audited field included — and the buffer is empty.
        /// </summary>
        [Fact]
        public void Whole_entity_read_returns_an_audited_field_without_recording_it()
        {
            DwPolicyOptions options = Options(audits: 1000);
            DwPolicyContext context = Caller();

            Filter sanitized = FilterSanitizer.Sanitize<Ac5Plain>(
                new Filter(), Attributes(), context, options, new PolicyTrace(DwTier.Strict, false));

            _out.WriteLine($"selects : {(sanitized.Selects is null ? "<whole entity>" : string.Join(",", sanitized.Selects))}");
            _out.WriteLine($"events  : {context.PendingAuditEvents.Count}");

            // Nothing is denied, so the row comes back whole and carries Tag.
            Assert.Null(sanitized.Selects);

            // A documented limit, not a defect fixed here: [DwAudit] records a field the request
            // names, and a field of the type's default order that the query adds. A request naming
            // no projection reads the row without naming anything, and records nothing. Closing that
            // redefines what a use is, which is a decision for its own release.
            Assert.Empty(context.PendingAuditEvents);
        }

        /// <summary>
        /// The order half of the same question, for contrast: a default-order field the library
        /// adds <i>is</i> recorded, which is what the synthesized projection does not do.
        /// </summary>
        [Fact]
        public void Default_order_records_the_use_the_library_adds()
        {
            DwPolicyOptions options = Options(audits: 1000);
            DwPolicyContext context = Caller();

            Filter sanitized = FilterSanitizer.Sanitize<Ac5Ordered>(
                new Filter(), Attributes(), context, options, new PolicyTrace(DwTier.Strict, false));

            _out.WriteLine($"orders : {string.Join(",", (sanitized.Orders ?? new List<OrderBy>()).Select(o => o.Field))}");
            _out.WriteLine($"events : {string.Join(",", context.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}");

            Assert.Contains(sanitized.Orders ?? new List<OrderBy>(), o => o.Field == Audited);

            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == Audited && e.Feature == PolicyFeature.Order);
        }
    }
}
