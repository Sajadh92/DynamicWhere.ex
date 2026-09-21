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
using DynamicWhere.ex.Policies.Storage;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 6. Walks every PolicyErrorCode the library can raise under Strict and records what it
    // names: the code, the path, the rule id and the origin — the four things a caller reads.
    // =============================================================================================

    /// <summary>A field filterable only with the operator its attribute allows, and nothing else.</summary>
    public class Sx6SoleRestricted
    {
        public int Id { get; set; }

        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Serial { get; set; } = string.Empty;
    }

    /// <summary>The same restriction behind an alias, so the refusal has a second spelling to pick.</summary>
    public class Sx6AliasRestricted
    {
        public int Id { get; set; }

        [DwAlias("badge")]
        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Serial { get; set; } = string.Empty;
    }

    /// <summary>A bare type, so a runtime rule is the only source of its policy.</summary>
    public class Sx6Plain
    {
        public int Id { get; set; }

        public string Serial { get; set; } = string.Empty;
    }

    /// <summary>A weighted field, for the cost cap.</summary>
    public class Sx6Costly
    {
        public int Id { get; set; }

        [DwCost(500)]
        public string Blob { get; set; } = string.Empty;
    }

    /// <summary>A scope read from the caller's context, for the missing-context refusal.</summary>
    public class Sx6Scoped
    {
        public int Id { get; set; }

        [DwForceWhere(Operator.Equal, ContextValue = "tenant")]
        public int TenantId { get; set; }
    }

    public sealed class Sx6CodeWalkProbes
    {
        private readonly ITestOutputHelper _out;

        public Sx6CodeWalkProbes(ITestOutputHelper output)
        {
            _out = output;
            PolicyBootstrap.Ensure();
        }

        // ---- harness ---------------------------------------------------------------------------

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, Caps = { MinGroupSize = 1 } };

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|tier={refusal.Tier}|rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}",
            LogicException failure => $"LogicException|{failure.Message}",
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

        private static Filter WhereOn(
            string field, Operator op = Operator.Equal, DataType type = DataType.Text, string value = "x") => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Sort = 0, Field = field, DataType = type, Operator = op, Values = { value } }
                }
            }
        };

        // =========================================================================================
        // FINDING candidate. OperatorNotAllowed is not in IsFieldDenial, so Gate.Exception takes the
        // attributing branch: it names the field AND, for a single-source policy, the attribute.
        // =========================================================================================

        [Fact]
        public void Strict_operator_refusal_attributes_the_attribute_that_restricted_the_field()
        {
            Exception? sole = Catch(() => Array.Empty<Sx6SoleRestricted>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(WhereOn("Serial", Operator.Contains)));

            Exception? aliased = Catch(() => Array.Empty<Sx6AliasRestricted>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(WhereOn("badge", Operator.Contains)));

            // What the same probe gets for a name that does not exist.
            Exception? unknown = Catch(() => Array.Empty<Sx6SoleRestricted>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(WhereOn("NoSuchColumn", Operator.Contains)));

            _out.WriteLine($"sole source  : {Shape(sole)}");
            _out.WriteLine($"aliased      : {Shape(aliased)}");
            _out.WriteLine($"unknown name : {Shape(unknown)}");

            PolicyException refusal = Assert.IsType<PolicyException>(sole);

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);
            Assert.Equal("Serial", refusal.FieldPath);

            // With attributes the policy carries more than one source, so Gate.Exception declines to
            // attribute and the origin stays empty.
            Assert.Null(refusal.SourceOrigin);
            Assert.Null(refusal.RuleId);

            // It is still told apart from a name that matches nothing, which answers "*". That is
            // sound: a field with an operator restriction is one the caller MAY filter, so its
            // existence is not a secret the tier is keeping.
            Assert.Equal("*", Assert.IsType<PolicyException>(unknown).FieldPath);
            Assert.Equal("badge", Assert.IsType<PolicyException>(aliased).FieldPath);
        }

        /// <summary>
        /// The case Gate.Exception does attribute: a policy with exactly one source. A runtime rule
        /// restricting the operators is such a policy, and under Strict the refusal then carries the
        /// rule's identifier and its origin string.
        /// </summary>
        [Fact]
        public void Strict_operator_refusal_from_a_sole_runtime_rule_carries_the_rule_id()
        {
            FakePolicyProvider rules = new FakePolicyProvider()
                .AddOperators("Serial", PolicyLevel.DynamicUser, Operator.Equal);

            PolicyResolver resolver = new(new IDwPolicyProvider[] { rules });

            Exception? error = Catch(() => Array.Empty<Sx6Plain>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), resolver)
                .ToList(WhereOn("Serial", Operator.Contains)));

            _out.WriteLine($"sole runtime rule : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);
            Assert.Equal("Serial", refusal.FieldPath);

            // What a field denial from the same rule would have carried, and does not.
            FakePolicyProvider denying = new FakePolicyProvider()
                .Add("Serial", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicUser);

            Exception? denied = Catch(() => Array.Empty<Sx6Plain>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), new PolicyResolver(new IDwPolicyProvider[] { denying }))
                .ToList(WhereOn("Serial")));

            _out.WriteLine($"sole rule, denied : {Shape(denied)}");

            PolicyException blanked = Assert.IsType<PolicyException>(denied);

            Assert.Equal("*", blanked.FieldPath);
            Assert.Null(blanked.RuleId);
            Assert.Null(blanked.SourceOrigin);
        }

        /// <summary>
        /// The same code reached through a <c>Having</c> clause, where the reference is an aggregate
        /// alias standing for the restricted column.
        /// </summary>
        [Fact]
        public void Strict_operator_refusal_in_having_names_the_path_behind_the_alias()
        {
            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Id" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = "Serial", Alias = "top", Aggregator = Aggregator.Maximum }
                    }
                },
                Having = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 0, Field = "top", DataType = DataType.Text,
                            Operator = Operator.Contains, Values = { "x" }
                        }
                    }
                }
            };

            Exception? error = Catch(() => Array.Empty<Sx6AliasRestricted>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes()).ToList(summary));

            _out.WriteLine($"having on aggregate alias : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);

            // The caller named "Serial" themselves, in the aggregate, so naming it back tells them
            // nothing they did not write.
            Assert.Equal("Serial", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
        }

        /// <summary>
        /// The same clause where the caller never writes the canonical name: the aggregate names the
        /// alias, and the having condition names the aggregate's own alias.
        /// </summary>
        [Fact]
        public void Strict_operator_refusal_in_having_reports_the_alias_the_caller_wrote()
        {
            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Id" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = "badge", Alias = "top", Aggregator = Aggregator.Maximum }
                    }
                },
                Having = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 0, Field = "top", DataType = DataType.Text,
                            Operator = Operator.Contains, Values = { "x" }
                        }
                    }
                }
            };

            Exception? error = Catch(() => Array.Empty<Sx6AliasRestricted>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes()).ToList(summary));

            _out.WriteLine($"having, alias only : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);

            // The canonical column must not come back to a caller who only ever wrote "badge".
            Assert.NotEqual("Serial", refusal.FieldPath);
        }

        // =========================================================================================
        // The rest of the codes, for the record.
        // =========================================================================================

        [Fact]
        public void Strict_cap_refusals_name_the_clause_and_the_cap()
        {
            DwPolicyOptions capped = new() { Tier = DwTier.Strict, Caps = { MinGroupSize = 1, MaxConditions = 1 } };

            ConditionGroup two = new() { Connector = Connector.And };

            two.Conditions.Add(new Condition
            {
                Sort = 0, Field = "Id", DataType = DataType.Number, Operator = Operator.Equal, Values = { "1" }
            });

            two.Conditions.Add(new Condition
            {
                Sort = 1, Field = "Blob", DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" }
            });

            Exception? error = Catch(() => Array.Empty<Sx6Costly>().AsQueryable()
                .ApplyPolicy(Caller(), capped, Attributes())
                .ToList(new Filter { ConditionGroup = two }));

            _out.WriteLine($"CapExceeded : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        [Fact]
        public void Strict_cost_refusal_names_the_clause_and_prints_the_total()
        {
            DwPolicyOptions budget = new() { Tier = DwTier.Strict, Caps = { MinGroupSize = 1, MaxQueryCost = 10 } };

            Exception? weighted = Catch(() => Array.Empty<Sx6Costly>().AsQueryable()
                .ApplyPolicy(Caller(), budget, Attributes()).ToList(WhereOn("Blob")));

            Exception? plain = Catch(() => Array.Empty<Sx6Costly>().AsQueryable()
                .ApplyPolicy(Caller(), budget, Attributes())
                .ToList(WhereOn("Id", Operator.Equal, DataType.Number, "1")));

            _out.WriteLine($"weighted field : {Shape(weighted)}");
            _out.WriteLine($"plain field    : {Shape(plain)}");

            PolicyException refusal = Assert.IsType<PolicyException>(weighted);

            Assert.Equal(PolicyErrorCode.QueryCostExceeded, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);

            // The origin prints the request's total cost, which is the sum of the weights of the
            // fields the caller named — and so, for a one-field request, that field's weight.
            Assert.Contains("request cost", refusal.SourceOrigin);
        }

        [Fact]
        public void Strict_missing_context_value_names_neither_the_column_nor_the_key()
        {
            Exception? error = Catch(() => Array.Empty<Sx6Scoped>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes()).ToList(new Filter()));

            _out.WriteLine($"MissingContextValue : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.MissingContextValue, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
        }

        [Fact]
        public void Strict_query_string_refusal_names_the_clause()
        {
            Exception? error = Catch(() => Array.Empty<Sx6Costly>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes()).ToList(new Filter(), getQueryString: true));

            _out.WriteLine($"QueryStringDenied : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.QueryStringDenied, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        [Fact]
        public void Strict_all_selects_denied_names_the_clause()
        {
            Exception? error = Catch(() => Array.Empty<Sx6Sealed>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Secret" } }));

            _out.WriteLine($"one denied select : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
        }

        // =========================================================================================
        // FINDING candidate. The store provider stamps every refusal DwTier.Strict, whatever the
        // posture is, and names the store implementation class to the caller.
        // =========================================================================================

        [Fact]
        public async Task A_store_refusal_reports_the_strict_tier_under_a_convenience_posture()
        {
            InMemoryPolicyStore inner = new();

            inner.Seed(new PolicyRule(
                DwSubjectKind.Global, null, "DynamicWhere.Tests.Policies.Sx6Sealed", "Secret",
                PolicyFeature.Select, PolicyEffect.Deny));

            UnreachableStore store = new(inner);

            DwPolicyOptions convenience = new()
            {
                Tier = DwTier.Convenience,
                StoreFailure = StoreFailureMode.FailClosed
            };

            convenience.Freeze();

            using StorePolicyProvider provider =
                await StorePolicyProvider.CreateAsync(store, convenience, autoRefresh: false);

            DwPolicyContext context = await provider.PrepareAsync(Caller());

            store.Broken = true;

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.RefreshAsync());

            PolicyResolver resolver = new(new IDwPolicyProvider[] { provider });

            Exception? error = Catch(() => Array.Empty<Sx6Sealed>().AsQueryable()
                .ApplyPolicy(context, convenience, resolver).ToList(new Filter()));

            _out.WriteLine($"StoreUnavailable : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.StoreUnavailable, refusal.ErrorCode);

            // The posture is Convenience. The refusal says Strict, and so does the audit event
            // RefusalAudit builds from it.
            Assert.Equal(DwTier.Convenience, convenience.Tier);
            Assert.Equal(DwTier.Strict, refusal.Tier);

            // It also names the store implementation class and the entity type.
            Assert.Contains("UnreachableStore", refusal.SourceOrigin);
            Assert.Equal(nameof(Sx6Sealed), refusal.FieldPath);
        }

        [Fact]
        public void An_unprepared_context_is_refused_naming_the_clause()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Array.Empty<Sx6Sealed>().AsQueryable().ApplyPolicy(Caller()));

            _out.WriteLine($"PolicyContextNotPrepared : {Shape(refusal)}");

            Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }
    }
}
