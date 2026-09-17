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

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Under the strict tier a name that matches nothing and a field the caller may not use answer alike
/// (DW-5).
/// </summary>
/// <remarks>
/// Refused differently, the pair is a schema oracle: a caller learns which columns exist, including
/// the ones they may never read, one guess at a time, and a refusal that carries the path or the
/// attribute behind it confirms every guess. Each test below sends the same request twice — once with
/// <c>NationalId</c>, which <c>[DwDenied]</c> refuses for every feature, once with a name that is not a
/// member at all — and requires the two refusals to be indistinguishable.
/// </remarks>
public class StrictUnknownFieldTests
{
    private const string Denied = "NationalId";
    private const string Missing = "NoSuchColumn";

    private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static PolicyResolver Attributes() => new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static DwPolicyOptions Options(DwTier tier = DwTier.Strict, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

    private static Filter Filter(Filter filter, DwTier tier = DwTier.Strict, bool dryRun = false) =>
        FilterSanitizer.Sanitize<SecuredEmployee>(
            filter, Attributes(), Caller(), Options(tier, dryRun), new PolicyTrace(tier, dryRun));

    private static Condition On(string field) =>
        new() { Sort = 0, Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" } };

    private static Filter Where(string field) => new() { ConditionGroup = new ConditionGroup { Conditions = { On(field) } } };

    /// <summary>Asserts two refusals carry nothing that tells them apart.</summary>
    private static void AssertAlike(Action denied, Action missing, PolicyErrorCode code, PolicyFeature feature)
    {
        PolicyException one = Assert.Throws<PolicyException>(denied);
        PolicyException two = Assert.Throws<PolicyException>(missing);

        Assert.Equal(code, one.ErrorCode);
        Assert.Equal(feature, one.Feature);
        Assert.Equal("*", one.FieldPath);
        Assert.Null(one.RuleId);
        Assert.Null(one.SourceOrigin);

        Assert.Equal(one.GetType(), two.GetType());
        Assert.Equal(one.ErrorCode, two.ErrorCode);
        Assert.Equal(one.Feature, two.Feature);
        Assert.Equal(one.FieldPath, two.FieldPath);
        Assert.Equal(one.Message, two.Message);
        Assert.Equal(one.RuleId, two.RuleId);
        Assert.Equal(one.SourceOrigin, two.SourceOrigin);
        Assert.Equal(one.Subject, two.Subject);
    }

    [Fact]
    public void A_condition_answers_alike()
    {
        AssertAlike(
            () => Filter(Where(Denied)),
            () => Filter(Where(Missing)),
            PolicyErrorCode.FieldDeniedForWhere,
            PolicyFeature.Where);
    }

    [Fact]
    public void A_condition_deep_in_a_subgroup_answers_alike()
    {
        static Filter Nested(string field) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                SubConditionGroups = { new ConditionGroup { Conditions = { On(field) } } }
            }
        };

        AssertAlike(
            () => Filter(Nested(Denied)),
            () => Filter(Nested(Missing)),
            PolicyErrorCode.FieldDeniedForWhere,
            PolicyFeature.Where);
    }

    [Fact]
    public void A_path_through_a_navigation_answers_alike()
    {
        // Contact.Email carries [DwNoWhere]; Contact.Nope is not a member of the contact.
        AssertAlike(
            () => Filter(Where("Contact.Email")),
            () => Filter(Where("Contact.Nope")),
            PolicyErrorCode.FieldDeniedForWhere,
            PolicyFeature.Where);
    }

    [Fact]
    public void An_order_answers_alike()
    {
        AssertAlike(
            () => Filter(new Filter { Orders = new List<OrderBy> { new() { Field = Denied } } }),
            () => Filter(new Filter { Orders = new List<OrderBy> { new() { Field = Missing } } }),
            PolicyErrorCode.FieldDeniedForOrder,
            PolicyFeature.Order);
    }

    [Fact]
    public void A_projection_answers_alike()
    {
        AssertAlike(
            () => Filter(new Filter { Selects = new List<string> { "Name", Denied } }),
            () => Filter(new Filter { Selects = new List<string> { "Name", Missing } }),
            PolicyErrorCode.FieldDeniedForSelect,
            PolicyFeature.Select);
    }

    [Fact]
    public void A_grouping_key_and_an_aggregate_answer_alike()
    {
        static Summary GroupedBy(string field) => new() { GroupBy = new GroupBy { Fields = new List<string> { field } } };

        static Summary Aggregating(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Name" },
                AggregateBy = new List<AggregateBy> { new() { Field = field, Alias = "n", Aggregator = Aggregator.CountDistinct } }
            }
        };

        static void Run(Summary summary) =>
            FilterSanitizer.Sanitize<SecuredEmployee>(summary, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, false));

        AssertAlike(() => Run(GroupedBy(Denied)), () => Run(GroupedBy(Missing)), PolicyErrorCode.FieldDeniedForGroup, PolicyFeature.Group);
        AssertAlike(() => Run(Aggregating(Denied)), () => Run(Aggregating(Missing)), PolicyErrorCode.FieldDeniedForAggregate, PolicyFeature.Aggregate);
    }

    [Fact]
    public void A_condition_inside_a_segment_answers_alike()
    {
        static Segment Of(string field) => new()
        {
            ConditionSets = { new ConditionSet { Sort = 1, ConditionGroup = new ConditionGroup { Conditions = { On(field) } } } }
        };

        static void Run(Segment segment) =>
            FilterSanitizer.Sanitize<SecuredEmployee>(segment, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, false));

        AssertAlike(() => Run(Of(Denied)), () => Run(Of(Missing)), PolicyErrorCode.FieldDeniedForSegment, PolicyFeature.Segment);
    }

    [Fact]
    public void A_projection_or_an_order_on_a_segment_answers_alike()
    {
        // Participation is decided over every name a segment carries, its selects and orders too, so
        // a denied field is refused for the segment before either clause is gated on its own.
        static Segment Selecting(string field) => new()
        {
            ConditionSets = { new ConditionSet { Sort = 1, ConditionGroup = new ConditionGroup { Conditions = { On("Name") } } } },
            Selects = new List<string> { "Name", field }
        };

        static Segment Ordering(string field) => new()
        {
            ConditionSets = { new ConditionSet { Sort = 1, ConditionGroup = new ConditionGroup { Conditions = { On("Name") } } } },
            Orders = new List<OrderBy> { new() { Field = field } }
        };

        static void Run(Segment segment) =>
            FilterSanitizer.Sanitize<SecuredEmployee>(segment, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, false));

        AssertAlike(() => Run(Selecting(Denied)), () => Run(Selecting(Missing)), PolicyErrorCode.FieldDeniedForSegment, PolicyFeature.Segment);
        AssertAlike(() => Run(Ordering(Denied)), () => Run(Ordering(Missing)), PolicyErrorCode.FieldDeniedForSegment, PolicyFeature.Segment);
    }

    [Fact]
    public void An_alias_answers_alike()
    {
        // phone_number is the alias of Contact.Phone, which carries [DwNoOrder].
        static void Run(string field) =>
            FilterSanitizer.Sanitize<AliasedCustomer>(
                new Filter { Orders = new List<OrderBy> { new() { Field = field } } },
                Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Strict, false));

        AssertAlike(() => Run("phone_number"), () => Run("phone_numbr"), PolicyErrorCode.FieldDeniedForOrder, PolicyFeature.Order);
    }

    [Fact]
    public void A_cap_the_request_breaks_is_reached_before_either_and_answers_alike()
    {
        // An unknown name refused while canonicalizing would come back ahead of the cap, and a real
        // field after it: the order of the two refusals would say which names exist.
        static Filter TooMany(string field)
        {
            ConditionGroup group = new() { Connector = Connector.And };

            for (int i = 0; i < 60; i++)
            {
                Condition condition = On(field);
                condition.Sort = i;
                group.Conditions.Add(condition);
            }

            return new Filter { ConditionGroup = group };
        }

        PolicyException denied = Assert.Throws<PolicyException>(() => Filter(TooMany(Denied)));
        PolicyException missing = Assert.Throws<PolicyException>(() => Filter(TooMany(Missing)));

        Assert.Equal(PolicyErrorCode.CapExceeded, denied.ErrorCode);
        Assert.Equal(denied.Message, missing.Message);
        Assert.Equal(denied.SourceOrigin, missing.SourceOrigin);
    }

    [Fact]
    public void A_navigation_too_deep_names_no_path_under_the_strict_tier()
    {
        // The canonical spelling of a real path would confirm the path exists.
        DwPolicyOptions shallow = new() { Tier = DwTier.Strict, Caps = { MaxNavigationDepth = 1 } };

        PolicyException real = Assert.Throws<PolicyException>(
            () => FilterSanitizer.Sanitize<SecuredEmployee>(
                Where("contact.phone"), Attributes(), Caller(), shallow, new PolicyTrace(DwTier.Strict, false)));
        PolicyException made = Assert.Throws<PolicyException>(
            () => FilterSanitizer.Sanitize<SecuredEmployee>(
                Where("contact.nope"), Attributes(), Caller(), shallow, new PolicyTrace(DwTier.Strict, false)));

        Assert.Equal(PolicyErrorCode.CapExceeded, real.ErrorCode);
        Assert.Equal("*", real.FieldPath);
        Assert.Equal(real.Message, made.Message);
    }

    [Fact]
    public void The_trace_still_says_which_is_which()
    {
        PolicyTrace trace = new(DwTier.Strict, dryRun: false);

        Assert.Throws<PolicyException>(
            () => FilterSanitizer.Sanitize<SecuredEmployee>(Where(Missing), Attributes(), Caller(), Options(), trace));

        PolicyDecision decision = Assert.Single(trace.Decisions);

        Assert.Equal(Missing, decision.FieldPath);
        Assert.Equal(PolicyAction.Denied, decision.Action);
        Assert.Equal("names nothing on SecuredEmployee", decision.Reason);
    }

    [Fact]
    public void The_convenience_tier_stays_talkative()
    {
        Assert.Throws<LogicException>(() => Filter(Where(Missing), DwTier.Convenience));

        PolicyException denied = Assert.Throws<PolicyException>(() => Filter(Where(Denied), DwTier.Convenience));

        Assert.Equal(Denied, denied.FieldPath);
        Assert.NotNull(denied.SourceOrigin);
    }

    [Fact]
    public void A_dry_run_refuses_nothing_so_an_unknown_name_fails_as_it_would_unguarded()
    {
        LogicException error = Assert.Throws<LogicException>(() => Filter(Where(Missing), DwTier.Strict, dryRun: true));

        Assert.IsNotType<PolicyException>(error);
    }

    [Fact]
    public void Through_the_guarded_query_the_two_answer_alike()
    {
        IQueryable<SecuredEmployee> people = new List<SecuredEmployee> { new() { Id = 1, Name = "Ada" } }.AsQueryable();

        AssertAlike(
            () => people.ApplyPolicy(Caller(), Options(), Attributes()).ToList(Where(Denied)),
            () => people.ApplyPolicy(Caller(), Options(), Attributes()).ToList(Where(Missing)),
            PolicyErrorCode.FieldDeniedForWhere,
            PolicyFeature.Where);
    }
}
