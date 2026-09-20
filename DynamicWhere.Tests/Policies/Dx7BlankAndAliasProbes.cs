using System.Dynamic;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 7, documentation review of 3.3.0 at 893cadc.
    //
    // Claim 3: "A blank name fails a guarded query exactly as it fails an unguarded one, in every
    //           clause."   (DOC.md, breaking-changes page)
    // Claim 4: "An alias spelled like another member of the same type is a ValidateModel error, and
    //           a generated row keeps both columns under their own names."
    //
    // Round 6 covered where / order / select / group / segment-select. The clauses below are the
    // ones it did not: an aggregated field, a Having condition, a summary's own Orders, and a
    // segment's Orders — each named in llms.txt's "Checks by shape" table as a place a blank name
    // is checked.
    // =============================================================================================

    /// <summary>A plain type, so only the blank name decides what happens.</summary>
    public class Dx7Plain
    {
        public int Id { get; set; }

        public string Region { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    /// <summary>An alias spelled exactly like another member of the same type.</summary>
    public class Dx7Shadowing
    {
        public int Id { get; set; }

        /// <summary>Answers to the name <c>Code</c>, which <see cref="Code"/> already carries.</summary>
        [DwAlias("Code")]
        public string Reference { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;
    }

    /// <summary>The same collision, spelled in another case.</summary>
    public class Dx7ShadowingByCase
    {
        public int Id { get; set; }

        [DwAlias("code")]
        public string Reference { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;
    }

    /// <summary>An alias that shadows nothing, as the control.</summary>
    public class Dx7Aliased
    {
        public int Id { get; set; }

        [DwAlias("reference")]
        public string Number { get; set; } = string.Empty;
    }

    public sealed class Dx7BlankAndAliasProbes
    {
        private readonly ITestOutputHelper _out;

        public Dx7BlankAndAliasProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier) =>
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

        // ---- the clauses round 6 did not cover ---------------------------------------------------

        private static object AggregateField(string name) => new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Region" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = name, Alias = "total", Aggregator = Aggregator.Sumation }
                }
            }
        };

        private static object SummaryOrder(string name) => new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Region" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Alias = "n", Aggregator = Aggregator.Count }
                }
            },
            Orders = new List<OrderBy> { new() { Sort = 0, Field = name, Direction = Direction.Ascending } }
        };

        private static object Having(string name) => new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Region" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Alias = "n", Aggregator = Aggregator.Count }
                }
            },
            Having = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Sort = 0, Field = name, DataType = DataType.Number,
                        Operator = Operator.GreaterThan, Values = { "0" }
                    }
                }
            }
        };

        private static object SegmentOrder(string name) => new Segment
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
            Orders = new List<OrderBy> { new() { Sort = 0, Field = name, Direction = Direction.Ascending } },
            Selects = new List<string> { "Id" }
        };

        private static object SegmentCondition(string name) => new Segment
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
                                Sort = 0, Field = name, DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "x" }
                            }
                        }
                    }
                }
            },
            Selects = new List<string> { "Id" }
        };

        private static readonly (string Clause, Func<string, object> Build)[] Clauses =
        {
            ("aggregate-field", AggregateField),
            ("summary-order", SummaryOrder),
            ("having", Having),
            ("segment-order", SegmentOrder),
            ("segment-condition", SegmentCondition)
        };

        private static void Execute(IQueryable<Dx7Plain> source, object request)
        {
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
        }

        private static void Execute(PolicyQueryable<Dx7Plain> guarded, object request)
        {
            switch (request)
            {
                case Filter filter:
                    guarded.ToListDynamic(filter);
                    break;

                case Summary summary:
                    guarded.ToList(summary);
                    break;

                case Segment segment:
                    guarded.ToListAsync(segment).GetAwaiter().GetResult();
                    break;
            }
        }

        // =========================================================================================
        // Claim 3, widened.
        // =========================================================================================

        [Fact]
        public void A_blank_name_fails_alike_in_the_clauses_round_six_did_not_cover()
        {
            List<string> diverged = new();

            foreach (string blank in new[] { string.Empty, "   ", "\t" })
            {
                foreach ((string clause, Func<string, object> build) in Clauses)
                {
                    Exception? bare = Catch(() =>
                        Execute(Array.Empty<Dx7Plain>().AsQueryable(), build(blank)));

                    foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
                    {
                        Exception? guarded = Catch(() => Execute(
                            Array.Empty<Dx7Plain>().AsQueryable()
                                .ApplyPolicy(Caller(), Options(tier), Attributes()),
                            build(blank)));

                        _out.WriteLine(
                            $"[{clause}/{tier}] blank='{blank.Replace("\t", "\\t")}' "
                            + $"guarded={Shape(guarded)} unguarded={Shape(bare)}");

                        if (!string.Equals(Shape(guarded), Shape(bare), StringComparison.Ordinal))
                        {
                            diverged.Add($"{tier}/{clause}/'{blank}': {Shape(guarded)} vs {Shape(bare)}");
                        }
                    }
                }
            }

            foreach (string line in diverged)
            {
                _out.WriteLine($"DIVERGED {line}");
            }

            Assert.Empty(diverged);
        }

        // =========================================================================================
        // Claim 4, both halves.
        // =========================================================================================

        [Fact]
        public void An_alias_spelled_like_another_member_is_a_validate_model_error()
        {
            foreach (Type type in new[] { typeof(Dx7Shadowing), typeof(Dx7ShadowingByCase) })
            {
                PolicyModelReport report = PolicyModelValidator.Inspect(new[] { type });

                _out.WriteLine($"[{type.Name}] valid={report.IsValid}");

                foreach (string error in report.Errors)
                {
                    _out.WriteLine($"    error: {error}");
                }

                Assert.False(report.IsValid);
                Assert.Contains(report.Errors, e => e.Contains("Reference") && e.Contains("Code"));
            }

            // The control: an alias that shadows nothing is still fine.
            PolicyModelReport clean = PolicyModelValidator.Inspect(new[] { typeof(Dx7Aliased) });

            _out.WriteLine($"[Dx7Aliased] valid={clean.IsValid}");

            Assert.True(clean.IsValid);
        }

        /// <summary>
        /// A generated row keeps both columns under their own names, for a deployment that never
        /// ran the scan.
        /// </summary>
        [Fact]
        public void A_generated_row_keeps_both_columns_under_their_own_names()
        {
            foreach (Type type in new[] { typeof(Dx7Shadowing), typeof(Dx7ShadowingByCase) })
            {
                IDictionary<string, object?> row = type == typeof(Dx7Shadowing)
                    ? Dynamic(new[] { new Dx7Shadowing { Id = 1, Reference = "R-1", Code = "C-1" } })
                    : Dynamic(new[] { new Dx7ShadowingByCase { Id = 1, Reference = "R-1", Code = "C-1" } });

                _out.WriteLine(
                    $"[{type.Name}] columns: {string.Join(", ", row.Select(c => $"{c.Key}={c.Value}"))}");

                // Both values survive, each under the name of the member that holds it.
                Assert.Equal("R-1", row["Reference"]);
                Assert.Equal("C-1", row["Code"]);
            }
        }

        /// <summary>The control: an alias that shadows nothing still renames the column.</summary>
        [Fact]
        public void An_alias_that_shadows_nothing_still_renames_the_column()
        {
            IDictionary<string, object?> row =
                Dynamic(new[] { new Dx7Aliased { Id = 1, Number = "N-1" } });

            _out.WriteLine($"[Dx7Aliased] columns: {string.Join(", ", row.Select(c => $"{c.Key}={c.Value}"))}");

            Assert.Equal("N-1", row["reference"]);
            Assert.False(row.ContainsKey("Number"));
        }

        private static IDictionary<string, object?> Dynamic<T>(T[] rows) where T : class
        {
            DwPolicyOptions options = new() { Tier = DwTier.Convenience, Caps = { MinGroupSize = 1 } };

            dynamic first = rows.AsQueryable()
                .ApplyPolicy(Caller(), options, Attributes())
                .ToListDynamic(new Filter())
                .Data[0];

            return first is ExpandoObject expando
                ? (IDictionary<string, object?>)expando
                : ((object)first).GetType()
                    .GetProperties()
                    .ToDictionary(p => p.Name, p => (object?)p.GetValue((object)first));
        }
    }
}
