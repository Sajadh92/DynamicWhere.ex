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
    // Round 6, adversarial security review of 3.3.0 at 7034717.
    //
    // Reviews round 5's first fix: an ambiguous name is recorded and refused as an unknown name is,
    // under Strict outside a dry run, instead of throwing AmbiguousFieldName.
    //
    // The three questions the fix has to answer:
    //   1. is the refusal byte-identical to an unknown name's, in EVERY clause?
    //   2. can the canonical path leak through the trace-in-result path?
    //   3. did anything downstream depend on the old exception?
    // =============================================================================================

    /// <summary>
    /// A type on which one spoken name means two different members: the real property
    /// <c>Salary</c>, and the alias <c>Salary</c> that <c>Notes</c> carries.
    /// </summary>
    internal class Sx6Colliding
    {
        public int Id { get; set; }

        public string Region { get; set; } = string.Empty;

        /// <summary>The real property.</summary>
        public string Salary { get; set; } = string.Empty;

        /// <summary>The alias, which collides with the property above.</summary>
        [DwAlias("Salary")]
        public string Notes { get; set; } = string.Empty;

        /// <summary>A denied field, for the three-way comparison.</summary>
        [DwDenied]
        public string Secret { get; set; } = string.Empty;
    }

    public sealed class Sx6AmbiguityProbes
    {
        private readonly ITestOutputHelper _out;

        public Sx6AmbiguityProbes(ITestOutputHelper output) => _out = output;

        // ---- harness ---------------------------------------------------------------------------

        private const string Ambiguous = "Salary";
        private const string Unknown = "Nope";
        private const string Denied = "Secret";

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict, bool dryRun = false) =>
            new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

        /// <summary>Everything a caller can read off a refusal.</summary>
        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.GetType().Name}|{refusal.ErrorCode}|path={refusal.FieldPath}"
                + $"|feature={refusal.Feature}|tier={refusal.Tier}|rule={refusal.RuleId ?? "-"}"
                + $"|origin={refusal.SourceOrigin ?? "-"}|message={refusal.Message}",
            LogicException failure =>
                $"{failure.GetType().Name}|{failure.Message}|subject={failure.Subject ?? "-"}",
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

        private static Condition On(string field) =>
            new() { Sort = 0, Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" } };

        private static ConditionGroup GroupOf(params Condition[] conditions)
        {
            ConditionGroup group = new() { Connector = Connector.And };

            for (int i = 0; i < conditions.Length; i++)
            {
                conditions[i].Sort = i;
                group.Conditions.Add(conditions[i]);
            }

            return group;
        }

        // Each clause, parameterised by the name the caller writes.

        private static Exception? Where(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Filter { ConditionGroup = GroupOf(On(name)) },
                Attributes(), Caller(), options, trace));

        private static Exception? Select(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Filter { Selects = new List<string> { "Id", name } },
                Attributes(), Caller(), options, trace));

        private static Exception? Order(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Filter { Orders = new List<OrderBy> { new() { Sort = 0, Field = name, Direction = Direction.Ascending } } },
                Attributes(), Caller(), options, trace));

        private static Exception? Group(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { name },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = "Id", Alias = "n", Aggregator = Aggregator.Count }
                        }
                    }
                },
                Attributes(), Caller(), options, trace));

        private static Exception? Aggregate(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Region" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = name, Alias = "n", Aggregator = Aggregator.Count }
                        }
                    }
                },
                Attributes(), Caller(), options, trace));

        private static Exception? SegmentWhere(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Segment
                {
                    ConditionSets = new List<ConditionSet>
                    {
                        new() { Sort = 0, Intersection = Intersection.Union, ConditionGroup = GroupOf(On(name)) }
                    },
                    Selects = new List<string> { "Id" }
                },
                Attributes(), Caller(), options, trace));

        private static Exception? SegmentSelect(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Segment
                {
                    ConditionSets = new List<ConditionSet>
                    {
                        new() { Sort = 0, Intersection = Intersection.Union, ConditionGroup = GroupOf(On("Region")) }
                    },
                    Selects = new List<string> { "Id", name }
                },
                Attributes(), Caller(), options, trace));

        private static Exception? SegmentOrder(string name, DwPolicyOptions options, PolicyTrace trace) => Catch(
            () => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Segment
                {
                    ConditionSets = new List<ConditionSet>
                    {
                        new() { Sort = 0, Intersection = Intersection.Union, ConditionGroup = GroupOf(On("Region")) }
                    },
                    Selects = new List<string> { "Id" },
                    Orders = new List<OrderBy> { new() { Sort = 0, Field = name, Direction = Direction.Ascending } }
                },
                Attributes(), Caller(), options, trace));

        private static readonly (string Clause, Func<string, DwPolicyOptions, PolicyTrace, Exception?> Run)[] Clauses =
        {
            ("where", Where),
            ("select", Select),
            ("order", Order),
            ("group", Group),
            ("aggregate", Aggregate),
            ("segment-where", SegmentWhere),
            ("segment-select", SegmentSelect),
            ("segment-order", SegmentOrder)
        };

        // =========================================================================================
        // Q1. Is the refusal byte-identical to an unknown name's, in every clause?
        // =========================================================================================

        [Fact]
        public void Strict_refuses_an_ambiguous_name_exactly_as_it_refuses_an_unknown_one()
        {
            List<string> mismatched = new();

            foreach ((string clause, Func<string, DwPolicyOptions, PolicyTrace, Exception?> run) in Clauses)
            {
                DwPolicyOptions options = Options();

                string ambiguous = Shape(run(Ambiguous, options, new PolicyTrace(DwTier.Strict, false)));
                string unknown = Shape(run(Unknown, options, new PolicyTrace(DwTier.Strict, false)));
                string denied = Shape(run(Denied, options, new PolicyTrace(DwTier.Strict, false)));

                _out.WriteLine($"[{clause}]");
                _out.WriteLine($"  ambiguous : {ambiguous}");
                _out.WriteLine($"  unknown   : {unknown}");
                _out.WriteLine($"  denied    : {denied}");

                if (!string.Equals(ambiguous, unknown, StringComparison.Ordinal))
                {
                    mismatched.Add($"{clause}: ambiguous='{ambiguous}' unknown='{unknown}'");
                }

                if (!string.Equals(unknown, denied, StringComparison.Ordinal))
                {
                    mismatched.Add($"{clause}: unknown='{unknown}' denied='{denied}'");
                }
            }

            Assert.Empty(mismatched);
        }

        // =========================================================================================
        // Q2. Can the canonical path leak through the trace?
        // =========================================================================================

        /// <summary>
        /// The trace records the candidates, which are canonical paths. A refused query returns no
        /// result, so the only way out is <c>LastTrace</c> — in process, never serialized.
        /// </summary>
        [Fact]
        public void The_trace_records_the_candidates_an_ambiguous_name_matched()
        {
            PolicyTrace trace = new(DwTier.Strict, dryRun: false);

            Exception? error = Where(Ambiguous, Options(), trace);

            foreach (PolicyDecision decision in trace.Decisions)
            {
                _out.WriteLine($"  {decision.FieldPath}|{decision.Feature}|{decision.Action}|{decision.Reason}");
            }

            Assert.IsType<PolicyException>(error);

            // The operator who has to fix the aliases can read what collided.
            Assert.Contains(
                trace.Decisions,
                d => d.Reason is not null && d.Reason.Contains("Notes", StringComparison.Ordinal));
        }

        /// <summary>
        /// The refusal itself never carries a candidate, whatever a caller reads off it.
        /// </summary>
        [Fact]
        public void The_refusal_never_carries_a_candidate_path()
        {
            foreach ((string clause, Func<string, DwPolicyOptions, PolicyTrace, Exception?> run) in Clauses)
            {
                Exception? error = run(Ambiguous, Options(), new PolicyTrace(DwTier.Strict, false));

                string text = Shape(error);

                _out.WriteLine($"[{clause}] {text}");

                Assert.DoesNotContain("Notes", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Salary", text, StringComparison.Ordinal);
            }
        }

        // =========================================================================================
        // Q3. Did anything downstream depend on the old exception?
        // =========================================================================================

        /// <summary>The convenience tier is unchanged: it still raises the ambiguity by name.</summary>
        [Fact]
        public void Convenience_still_raises_AmbiguousFieldName()
        {
            foreach ((string clause, Func<string, DwPolicyOptions, PolicyTrace, Exception?> run) in Clauses)
            {
                Exception? error = run(Ambiguous, Options(DwTier.Convenience), new PolicyTrace(DwTier.Convenience, false));

                _out.WriteLine($"[{clause}] {Shape(error)}");

                PolicyException refusal = Assert.IsType<PolicyException>(error);

                Assert.Equal(PolicyErrorCode.AmbiguousFieldName, refusal.ErrorCode);
            }
        }

        /// <summary>A dry run under Strict is unchanged: it still raises the ambiguity by name.</summary>
        [Fact]
        public void A_strict_dry_run_still_raises_AmbiguousFieldName()
        {
            Exception? error = Where(Ambiguous, Options(dryRun: true), new PolicyTrace(DwTier.Strict, true));

            _out.WriteLine(Shape(error));

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, refusal.ErrorCode);
        }

        /// <summary>
        /// The ambiguity is never silently resolved: no clause comes back holding either candidate.
        /// </summary>
        [Fact]
        public void No_clause_resolves_the_ambiguity_in_favour_of_one_candidate()
        {
            foreach ((string clause, Func<string, DwPolicyOptions, PolicyTrace, Exception?> run) in Clauses)
            {
                Exception? error = run(Ambiguous, Options(), new PolicyTrace(DwTier.Strict, false));

                _out.WriteLine($"[{clause}] {(error is null ? "ANSWERED" : error.GetType().Name)}");

                // An answer here would mean the library picked one of the two meanings.
                Assert.NotNull(error);
            }
        }

        // =========================================================================================
        // The per-context dry run, which the fix's own rule says names fields anyway.
        // =========================================================================================

        /// <summary>
        /// A canary subject running unenforced. The sanitizer's own <c>IsDryRun</c> is the union of
        /// the global switch and this one, so this is a dry run for every decision the gate makes.
        /// </summary>
        [Fact]
        public void A_context_dry_run_is_a_dry_run_for_the_sanitizer()
        {
            DwPolicyContext canary = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
            canary.DryRun = true;

            PolicyTrace trace = new(DwTier.Strict, dryRun: true);

            Exception? error = Catch(() => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Filter { ConditionGroup = GroupOf(On(Ambiguous)) },
                Attributes(), canary, Options(), trace));

            _out.WriteLine($"context dry run, ambiguous : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            // Not gated as an unknown name: the per-context dry run reaches HidesExistence too.
            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, refusal.ErrorCode);

            // And a denied field is not refused at all under a context dry run.
            DwPolicyContext second = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
            second.DryRun = true;

            Exception? denied = Catch(() => FilterSanitizer.Sanitize<Sx6Colliding>(
                new Filter { ConditionGroup = GroupOf(On(Denied)) },
                Attributes(), second, Options(), new PolicyTrace(DwTier.Strict, true)));

            _out.WriteLine($"context dry run, denied    : {Shape(denied)}");

            Assert.Null(denied);
        }
    }
}
