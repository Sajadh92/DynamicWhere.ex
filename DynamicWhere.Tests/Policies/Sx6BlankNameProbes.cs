using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 6. A blank field name in each clause, under Strict.
    //
    // CanonicalizeGroup and CanonicalizeOrder both refuse a blank field with LogicException before
    // ResolveName sees it. Selects and GroupBy.Fields have no such guard, and under Strict a blank
    // name reaches Gate.Unknown, which keeps it as the empty string — the one field path
    // PolicyDecision refuses to be constructed with.
    // =============================================================================================

    /// <summary>A plain type, so nothing but the blank name decides what happens.</summary>
    public class Sx6Blankable
    {
        public int Id { get; set; }

        public string Region { get; set; } = string.Empty;
    }

    public sealed class Sx6BlankNameProbes
    {
        private readonly ITestOutputHelper _out;

        public Sx6BlankNameProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, Caps = { MinGroupSize = 1 } };

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal => $"PolicyException|{refusal.ErrorCode}|path={refusal.FieldPath}",
            LogicException failure => $"LogicException|{failure.Message}",
            _ => $"{error.GetType().Name}|{error.Message.Split('\n')[0]}"
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

        private static Exception? Run(string blank, DwTier tier, Func<string, object> build) => Catch(() =>
        {
            object request = build(blank);

            PolicyQueryable<Sx6Blankable> guarded =
                Array.Empty<Sx6Blankable>().AsQueryable().ApplyPolicy(Caller(), Options(tier), Attributes());

            switch (request)
            {
                case Filter filter:
                    guarded.ToList(filter);
                    break;

                case Summary summary:
                    guarded.ToList(summary);
                    break;

                case Segment segment:
                    guarded.ToListAsync(segment).GetAwaiter().GetResult();
                    break;
            }
        });

        private static object SelectClause(string name) =>
            new Filter { Selects = new List<string> { "Id", name } };

        private static object WhereClause(string name) => new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Sort = 0, Field = name, DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { "x" }
                    }
                }
            }
        };

        private static object OrderClause(string name) =>
            new Filter { Orders = new List<OrderBy> { new() { Sort = 0, Field = name, Direction = Direction.Ascending } } };

        private static object GroupClause(string name) => new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { name },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Alias = "n", Aggregator = Aggregator.Count }
                }
            }
        };

        private static object SegmentSelectClause(string name) => new Segment
        {
            ConditionSets = new List<ConditionSet>
            {
                new()
                {
                    Sort = 0,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Sort = 0, Field = "Region", DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "x" }
                            }
                        }
                    }
                }
            },
            Selects = new List<string> { "Id", name }
        };

        private static readonly (string Clause, Func<string, object> Build)[] Clauses =
        {
            ("where", WhereClause),
            ("order", OrderClause),
            ("select", SelectClause),
            ("group", GroupClause),
            ("segment-select", SegmentSelectClause)
        };

        /// <summary>What the same request does with no policy in front of it.</summary>
        private static Exception? Unguarded(string blank, Func<string, object> build) => Catch(() =>
        {
            object request = build(blank);

            IQueryable<Sx6Blankable> source = Array.Empty<Sx6Blankable>().AsQueryable();

            switch (request)
            {
                case Filter filter:
                    source.ToListDynamic(filter);
                    break;

                case Summary summary:
                    source.ToList(summary);
                    break;

                case Segment segment:
                    source.ToListAsync(segment).GetAwaiter().GetResult();
                    break;
            }
        });

        // =========================================================================================
        // A blank name in a clause with no InvalidField guard — Selects, GroupBy.Fields and a
        // segment's Selects — leaves the library as ArgumentNullException rather than as one of its
        // own two exception types, in both tiers.
        // =========================================================================================

        [Fact]
        public void A_blank_name_leaves_a_guarded_query_exactly_as_it_leaves_an_unguarded_one()
        {
            List<string> diverged = new();

            foreach (string blank in new[] { string.Empty, "   ", "\t" })
            {
                foreach ((string clause, Func<string, object> build) in Clauses)
                {
                    Exception? strict = Run(blank, DwTier.Strict, build);
                    Exception? convenience = Run(blank, DwTier.Convenience, build);
                    Exception? bare = Unguarded(blank, build);

                    _out.WriteLine($"[{clause}] blank='{blank.Replace("\t", "\\t")}'");
                    _out.WriteLine($"    strict      : {Shape(strict)}");
                    _out.WriteLine($"    convenience : {Shape(convenience)}");
                    _out.WriteLine($"    unguarded   : {Shape(bare)}");

                    // The standard the library holds itself to for a name it cannot resolve: the
                    // guarded query fails exactly as the unguarded one does.
                    if (!string.Equals(Shape(strict), Shape(bare), StringComparison.Ordinal))
                    {
                        diverged.Add($"strict/{clause}/'{blank}': {Shape(strict)} vs {Shape(bare)}");
                    }

                    if (!string.Equals(Shape(convenience), Shape(bare), StringComparison.Ordinal))
                    {
                        diverged.Add($"convenience/{clause}/'{blank}': {Shape(convenience)} vs {Shape(bare)}");
                    }
                }
            }

            foreach (string line in diverged)
            {
                _out.WriteLine($"DIVERGED {line}");
            }

            // A blank name fails a guarded query exactly as it fails an unguarded one, in every
            // clause. The grouping key used to be the exception: its loop handed the blank straight
            // to the resolver, which reached Validate<T>'s argument check, so a malformed clause
            // came back as an ArgumentNullException rather than the failure the endpoint turns into
            // a four-hundred. It guards the blank first now, as every sibling clause does.
            Assert.Empty(diverged);
        }

        /// <summary>
        /// The divergence on its own, spelled out: the guarded summary leaves as a framework
        /// exception where the unguarded one leaves as the library's own malformed-request error.
        /// </summary>
        [Fact]
        public void A_blank_grouping_key_fails_the_same_way_once_the_query_is_guarded()
        {
            Exception? guarded = Run(string.Empty, DwTier.Strict, GroupClause);
            Exception? bare = Unguarded(string.Empty, GroupClause);

            _out.WriteLine($"guarded   : {Shape(guarded)}");
            _out.WriteLine($"unguarded : {Shape(bare)}");

            LogicException malformed = Assert.IsType<LogicException>(bare);

            Assert.Equal("ConditionMustHasValidFieldName", malformed.Message);

            // The same failure, guarded: a malformed clause, not an exception from inside the
            // sanitizer that nothing above it catches.
            LogicException guardedMalformed = Assert.IsType<LogicException>(guarded);

            Assert.Equal(malformed.Message, guardedMalformed.Message);
        }

        /// <summary>
        /// The same blank name straight through the sanitizer, so the failure is attributable to it
        /// rather than to anything the terminal does afterwards.
        /// </summary>
        [Fact]
        public void A_blank_projection_name_reaches_the_gate_under_strict()
        {
            Exception? error = Catch(() => FilterSanitizer.Sanitize<Sx6Blankable>(
                new Filter { Selects = new List<string> { "Id", string.Empty } },
                Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, dryRun: false)));

            _out.WriteLine($"sanitizer, blank select : {Shape(error)}");

            // Validator.Validate<T>(string) is documented to throw ArgumentNullException for a name
            // that is null or whitespace, and ResolveName hands the raw name to it. Recorded rather
            // than asserted as a defect: the unguarded surface does the same, which is the standard
            // the library applies to every name it cannot resolve.
            Assert.IsType<ArgumentNullException>(error);
        }

        /// <summary>The same for a blank grouping key.</summary>
        [Fact]
        public void A_blank_grouping_key_is_refused_before_it_reaches_the_gate()
        {
            Exception? error = Catch(() => FilterSanitizer.Sanitize<Sx6Blankable>(
                (Summary)GroupClause(string.Empty),
                Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, dryRun: false)));

            _out.WriteLine($"sanitizer, blank group key : {Shape(error)}");

            Assert.IsType<LogicException>(error);
        }
    }
}
