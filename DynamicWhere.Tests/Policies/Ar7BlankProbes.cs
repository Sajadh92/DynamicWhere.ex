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
using DynamicWhere.ex.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A plain type with an alias, so both branches of ResolveName are reachable.</summary>
    public class Ar7Blank
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAlias("label")]
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>The same shape with no alias at all, which takes the other branch of ResolveName.</summary>
    public class Ar7Plain
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class Ar7BlankProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7BlankProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller()
            => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(DwTier tier) =>
            new() { Tier = tier, AuditRefusals = true, Caps = { MinGroupSize = 1 } };

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal => $"PolicyException {refusal.ErrorCode} path='{refusal.FieldPath}'",
            LogicException logic => $"LogicException {logic.Message}",
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

        private static Ar7Blank[] Rows() => new[] { new Ar7Blank { Id = 1, Name = "a", Title = "t" } };

        private static Ar7Plain[] Plain() => new[] { new Ar7Plain { Id = 1, Name = "a" } };

        // =========================================================================================
        // The blank guard: which clauses have one, and what the ones without do.
        // =========================================================================================

        /// <summary>The fixed clause: a blank grouping key now fails as validation, not as an argument.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_blank_group_by_key_fails_as_validation(DwTier tier)
        {
            Exception? guarded = Catch(() => Rows().AsQueryable()
                .ApplyPolicy(Caller(), Posture(tier), Attributes())
                .ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "  " },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
                        }
                    }
                }));

            _out.WriteLine($"{tier} group-by blank : {Shape(guarded)}");

            Assert.IsType<LogicException>(guarded);
            Assert.IsNotType<ArgumentNullException>(guarded);
        }

        /// <summary>
        /// CANDIDATE. A blank projection entry has no such guard on either the aliased or the
        /// unaliased branch of ResolveName.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_blank_select_entry(DwTier tier)
        {
            Exception? aliased = Catch(() => Rows().AsQueryable()
                .ApplyPolicy(Caller(), Posture(tier), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "  " } }));

            Exception? unaliased = Catch(() => Plain().AsQueryable()
                .ApplyPolicy(Caller(), Posture(tier), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "  " } }));

            Exception? unguarded = Catch(() => Plain().AsQueryable()
                .ToList(new Filter { Selects = new List<string> { "Id", "  " } }));

            _out.WriteLine($"{tier} guarded, type with an alias : {Shape(aliased)}");
            _out.WriteLine($"{tier} guarded, type with none     : {Shape(unaliased)}");
            _out.WriteLine($"     unguarded                     : {Shape(unguarded)}");
        }

        /// <summary>CANDIDATE. The same for a segment's projection.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public async Task A_blank_segment_select_entry(DwTier tier)
        {
            Exception? guarded = null;

            try
            {
                await Plain().AsQueryable()
                    .ApplyPolicy(Caller(), Posture(tier), Attributes())
                    .ToListAsync(new Segment
                    {
                        Selects = new List<string> { "Id", "  " },
                        ConditionSets = new List<ConditionSet>
                        {
                            new()
                            {
                                Sort = 0,
                                Intersection = Intersection.Union,
                                ConditionGroup = new ConditionGroup
                                {
                                    Sort = 0,
                                    Connector = Connector.And,
                                    Conditions = new List<Condition>
                                    {
                                        new()
                                        {
                                            Sort = 0, Field = "Id", DataType = DataType.Number,
                                            Operator = Operator.GreaterThan,
                                            Values = new List<object> { "0" }
                                        }
                                    }
                                }
                            }
                        }
                    });
            }
            catch (Exception error)
            {
                guarded = error;
            }

            Exception? unguarded = null;

            try
            {
                await Plain().AsQueryable().ToListAsync(new Segment
                {
                    Selects = new List<string> { "Id", "  " },
                    ConditionSets = new List<ConditionSet>
                    {
                        new()
                        {
                            Sort = 0,
                            Intersection = Intersection.Union,
                            ConditionGroup = new ConditionGroup
                            {
                                Sort = 0,
                                Connector = Connector.And,
                                Conditions = new List<Condition>
                                {
                                    new()
                                    {
                                        Sort = 0, Field = "Id", DataType = DataType.Number,
                                        Operator = Operator.GreaterThan,
                                        Values = new List<object> { "0" }
                                    }
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception error)
            {
                unguarded = error;
            }

            _out.WriteLine($"{tier} guarded segment : {Shape(guarded)}");
            _out.WriteLine($"     unguarded        : {Shape(unguarded)}");
        }

        /// <summary>A blank order field, for comparison: guarded there since before round 6.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_blank_order_field_fails_as_validation(DwTier tier)
        {
            Exception? guarded = Catch(() => Plain().AsQueryable()
                .ApplyPolicy(Caller(), Posture(tier), Attributes())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy> { new() { Sort = 0, Field = " ", Direction = Direction.Ascending } }
                }));

            _out.WriteLine($"{tier} order blank : {Shape(guarded)}");

            Assert.IsType<LogicException>(guarded);
        }

        /// <summary>The guard must not swallow a real name that merely has padding around it.</summary>
        [Fact]
        public void A_padded_real_group_key_is_still_resolved()
        {
            Exception? error = Catch(() => Rows().AsQueryable()
                .ApplyPolicy(Caller(), Posture(DwTier.Convenience), Attributes())
                .ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { " label " },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
                        }
                    }
                }));

            _out.WriteLine($"padded alias : {Shape(error)}");

            Assert.Null(error);
        }
    }
}
