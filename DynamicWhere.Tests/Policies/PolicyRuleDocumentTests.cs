using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The whole-rule serializer, which is the only place a rule is written to or read from a store.
/// </summary>
/// <remarks>
/// Design section 5.1 gives a rule one payload column, for the transform. The rule Phase 5 shipped
/// carries four more things — a forced predicate, an operator restriction, a filtering requirement
/// and an alias — and three of those four fail <em>open</em> when a serializer drops them: the rule
/// still loads, the snapshot still counts it, and the control it describes is gone. Every test here
/// exists because dropping the thing it checks would be silent.
/// </remarks>
public class PolicyRuleDocumentTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    // ---------------------------------------------------------------- the scalar half

    [Fact]
    public void Every_scalar_survives_the_round_trip()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3));
        DateTimeOffset to = new(2026, 12, 31, 0, 0, 0, TimeSpan.FromHours(3));

        PolicyRule rule = new(
            DwSubjectKind.Role,
            "Manager",
            StaffType,
            "Salary",
            PolicyFeature.Where | PolicyFeature.Select,
            PolicyEffect.Mask,
            priority: 10,
            enabled: false,
            validFrom: from,
            validTo: to,
            purpose: "billing",
            id: id,
            createdBy: "sajjad",
            createdAt: from,
            updatedBy: "auditor",
            updatedAt: to);

        PolicyRule back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule));

        Assert.Equal(id, back.Id);
        Assert.Equal(DwSubjectKind.Role, back.SubjectKind);
        Assert.Equal("Manager", back.SubjectKey);
        Assert.Equal(StaffType, back.EntityType);
        Assert.Equal("Salary", back.FieldPath);
        Assert.Equal(PolicyFeature.Where | PolicyFeature.Select, back.Features);
        Assert.Equal(PolicyEffect.Mask, back.Effect);
        Assert.Equal(10, back.Priority);
        Assert.False(back.Enabled);
        Assert.Equal(from, back.ValidFrom);
        Assert.Equal(to, back.ValidTo);
        Assert.Equal("billing", back.Purpose);
        Assert.Equal("sajjad", back.CreatedBy);
        Assert.Equal(from, back.CreatedAt);
        Assert.Equal("auditor", back.UpdatedBy);
        Assert.Equal(to, back.UpdatedAt);
    }

    [Fact]
    public void A_global_rule_round_trips_without_a_subject_key()
    {
        PolicyRule rule = new(
            DwSubjectKind.Global, null, StaffType, "*", PolicyFeature.Select, PolicyEffect.Deny);

        PolicyRule back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule));

        Assert.Equal(DwSubjectKind.Global, back.SubjectKind);
        Assert.Null(back.SubjectKey);
        Assert.Equal("*", back.FieldPath);
        Assert.Equal(PolicyLevel.DynamicGlobal, back.Level);
    }

    // ---------------------------------------------------------------- the four uncolumned carriers

    [Fact]
    public void A_forced_predicate_reading_the_context_survives()
    {
        // The one that matters most. A tenant scope that silently stops applying is a cross-tenant
        // disclosure, and nothing about the loaded rule would look wrong.
        PolicyRule rule = Rule(
            forced: ForcedPredicate.FromContext(
                "TenantId", Operator.Equal, DataType.Number, "TenantId"));

        ForcedPredicate back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).Forced!;

        Assert.Equal("TenantId", back.FieldPath);
        Assert.Equal(Operator.Equal, back.Operator);
        Assert.Equal(DataType.Number, back.DataType);
        Assert.Equal("TenantId", back.ContextValue);
        Assert.Null(back.Value);
        Assert.True(back.ReadsContext);
    }

    [Fact]
    public void A_forced_predicate_holding_a_constant_survives()
    {
        PolicyRule rule = Rule(
            forced: ForcedPredicate.FromConstant(
                "IsVoid", Operator.Equal, DataType.Boolean, "false"));

        ForcedPredicate back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).Forced!;

        Assert.Equal("false", back.Value);
        Assert.Null(back.ContextValue);
        Assert.False(back.ReadsContext);
    }

    [Fact]
    public void A_forced_null_check_survives()
    {
        // The commonest forced predicate of all — soft deletion — and the one shape that compares
        // against nothing, so a reader keying off the presence of a value would lose it.
        PolicyRule rule = Rule(
            forced: ForcedPredicate.FromNullCheck("DeletedAt", Operator.IsNull, DataType.DateTime));

        ForcedPredicate back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).Forced!;

        Assert.Equal(Operator.IsNull, back.Operator);
        Assert.True(back.IsNullCheck);
        Assert.Null(back.Value);
        Assert.Null(back.ContextValue);
    }

    [Fact]
    public void An_operator_restriction_survives()
    {
        PolicyRule rule = Rule(allowed: new[] { Operator.Equal, Operator.In });

        IReadOnlyList<Operator> back =
            PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).AllowedOperators!;

        Assert.Equal(new[] { Operator.Equal, Operator.In }, back);
    }

    [Fact]
    public void An_alias_survives()
    {
        PolicyRule rule = Rule(alias: "reference");

        Assert.Equal(
            "reference", PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).Alias);
    }

    [Fact]
    public void A_transform_survives()
    {
        PolicyRule rule = Rule(transform: new MaskStage(MaskStrategy.Partial, keepEnd: 4));

        MaskStage back = Assert.IsType<MaskStage>(
            PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule)).Transform);

        Assert.Equal(MaskStrategy.Partial, back.Strategy);
        Assert.Equal(4, back.KeepEnd);
    }

    [Fact]
    public void A_rule_carrying_every_carrier_at_once_survives()
    {
        PolicyRule rule = Rule(
            transform: new TruncateStage(8, "..."),
            allowed: new[] { Operator.Equal },
            alias: "ref",
            forced: ForcedPredicate.FromConstant("IsVoid", Operator.Equal, DataType.Boolean, "false"),
            required: new[] { Operator.Equal, Operator.In });

        PolicyRule back = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule));

        Assert.NotNull(back.Transform);
        Assert.NotNull(back.AllowedOperators);
        Assert.NotNull(back.Alias);
        Assert.NotNull(back.Forced);
        Assert.NotNull(back.RequiredOperators);
    }

    // ---------------------------------------------------------------- null is not empty

    [Fact]
    public void An_absent_requirement_stays_absent_and_an_empty_one_stays_empty()
    {
        // null means "this rule demands no filter"; empty means "it demands one that nothing
        // satisfies". Writing an absent property for both reverses the decision on the way back in.
        PolicyRule none = Rule(required: null);
        PolicyRule impossible = Rule(required: Array.Empty<Operator>());

        Assert.Null(PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(none)).RequiredOperators);

        IReadOnlyList<Operator>? back =
            PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(impossible)).RequiredOperators;

        Assert.NotNull(back);
        Assert.Empty(back!);
    }

    [Fact]
    public void An_absent_operator_restriction_stays_absent_and_an_empty_one_stays_empty()
    {
        PolicyRule none = Rule(allowed: null);
        PolicyRule impossible = Rule(allowed: Array.Empty<Operator>());

        Assert.Null(PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(none)).AllowedOperators);

        IReadOnlyList<Operator>? back =
            PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(impossible)).AllowedOperators;

        Assert.NotNull(back);
        Assert.Empty(back!);
    }

    // ---------------------------------------------------------------- enums by name, never number

    [Fact]
    public void Operators_are_written_by_name()
    {
        // Operator.Equal is zero, so a numeric operator read from an absent column is an equality
        // restriction nobody wrote — plausible enough to survive a review of the stored row.
        string json = PolicyRuleDocument.ToJson(Rule(allowed: new[] { Operator.IContains }));

        Assert.Contains("IContains", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_subject_the_effect_and_the_features_are_written_by_name()
    {
        // All three default to their most permissive member. Written as names, an absent column
        // yields an empty string that parses to nothing and is refused.
        string json = PolicyRuleDocument.ToJson(
            new PolicyRule(
                DwSubjectKind.Tenant,
                "acme",
                StaffType,
                "Salary",
                PolicyFeature.Where | PolicyFeature.Order,
                PolicyEffect.Deny));

        Assert.Contains("Tenant", json, StringComparison.Ordinal);
        Assert.Contains("Deny", json, StringComparison.Ordinal);
        Assert.Contains("Where", json, StringComparison.Ordinal);
        Assert.Contains("Order", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("subjectKind")]
    [InlineData("effect")]
    [InlineData("features")]
    public void A_numeric_enumeration_is_refused(string property)
    {
        string json = PolicyRuleDocument.ToJson(Rule());
        string numeric = json.Replace(
            $"\"{property}\":\"{ValueOf(json, property)}\"", $"\"{property}\":0",
            StringComparison.Ordinal);

        Assert.ThrowsAny<ArgumentException>(() => PolicyRuleDocument.ToRule(numeric));
    }

    [Fact]
    public void An_operator_named_by_a_number_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(Document("""
                "detail":{"allowedOperators":[0]}
                """)));
    }

    [Fact]
    public void A_data_type_named_by_a_number_is_refused()
    {
        // DataType.Text is zero as well, and a forced predicate carrying the wrong data type is
        // validated against the wrong CLR type at injection.
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(Document("""
                "detail":{"forced":{"fieldPath":"TenantId","operator":"Equal","dataType":0,
                "contextValue":"TenantId"}}
                """)));
    }

    [Fact]
    public void An_enumeration_name_this_library_does_not_define_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(
                PolicyRuleDocument.ToJson(Rule()).Replace(
                    "\"Deny\"", "\"Obliterate\"", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void A_forced_predicate_naming_both_a_value_and_a_context_value_is_refused()
    {
        // The three shapes are mutually exclusive and the factories enforce that. A reader setting
        // fields directly would resolve this by whichever it happened to check first, which is a
        // predicate the operator did not write filtering rows they did not intend.
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(Document("""
                "detail":{"forced":{"fieldPath":"TenantId","operator":"Equal","dataType":"Number",
                "value":"1","contextValue":"TenantId"}}
                """)));
    }

    [Fact]
    public void A_transformer_cannot_be_named_by_a_document()
    {
        // Inherited from PolicyPayload unchanged: naming a CLR type escalates a store from "can
        // change policy" to "can construct arbitrary types".
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToJson(Rule(transform: new MutateStage(typeof(string)))));

        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(Document("""
                "detail":{"transform":{"kind":"Mutate"}}
                """)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{}")]
    public void A_document_that_cannot_be_read_throws_rather_than_yielding_a_partial_rule(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => PolicyRuleDocument.ToRule(json));
    }

    [Fact]
    public void A_document_still_passes_through_the_rules_own_boundary()
    {
        // The refusals Phase 5 mutation-checked one at a time stay in force: the document reader
        // builds through the constructor rather than around it. A short entity name matches no type
        // and would be a denial that never applies.
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyRuleDocument.ToRule(
                PolicyRuleDocument.ToJson(Rule()).Replace(
                    StaffType, "Staff", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_null_rule_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => PolicyRuleDocument.ToJson(null!));
    }

    // ---------------------------------------------------------------- the detail half, for EF

    [Fact]
    public void The_detail_half_round_trips_every_carrier()
    {
        // The relational store keeps the indexed axes as columns and only this half as JSON, so the
        // two halves are written by one routine and cannot describe a carrier differently.
        PolicyRule rule = Rule(
            transform: new FormatStage("yyyy"),
            allowed: new[] { Operator.Equal },
            alias: "ref",
            forced: ForcedPredicate.FromContext(
                "TenantId", Operator.Equal, DataType.Number, "TenantId"),
            required: Array.Empty<Operator>());

        RuleDetail back = PolicyRuleDocument.ReadDetail(PolicyRuleDocument.DetailToJson(rule));

        Assert.IsType<FormatStage>(back.Transform);
        Assert.Equal(new[] { Operator.Equal }, back.AllowedOperators!);
        Assert.Equal("ref", back.Alias);
        Assert.Equal("TenantId", back.Forced!.ContextValue);
        Assert.Empty(back.RequiredOperators!);
    }

    [Fact]
    public void A_rule_carrying_no_carrier_has_no_detail()
    {
        Assert.Null(PolicyRuleDocument.DetailToJson(Rule()));

        RuleDetail none = PolicyRuleDocument.ReadDetail(null);

        Assert.Null(none.Transform);
        Assert.Null(none.AllowedOperators);
        Assert.Null(none.Alias);
        Assert.Null(none.Forced);
        Assert.Null(none.RequiredOperators);
    }

    [Fact]
    public void A_detail_column_that_cannot_be_read_throws()
    {
        // A row whose detail is unreadable is a rule enforcing less than it says. Skipping it would
        // be the same silence in a different place.
        Assert.ThrowsAny<ArgumentException>(() => PolicyRuleDocument.ReadDetail("not json"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A rule carrying whichever of the five uncolumned carriers a test needs.</summary>
    private static PolicyRule Rule(
        TransformStage? transform = null,
        IReadOnlyList<Operator>? allowed = null,
        string? alias = null,
        ForcedPredicate? forced = null,
        IReadOnlyList<Operator>? required = null) =>
        new(DwSubjectKind.Role,
            "Manager",
            StaffType,
            "Salary",
            PolicyFeature.Select,
            PolicyEffect.Deny,
            transform: transform,
            allowedOperators: allowed,
            alias: alias,
            forced: forced,
            requiredOperators: required);

    /// <summary>Builds a whole document around a hand-written detail, for the malformed cases.</summary>
    private static string Document(string detail) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","subjectKind":"Role","subjectKey":"Manager",
        "entityType":"{{StaffType}}","fieldPath":"Salary","features":"Select","effect":"Deny",
        "priority":0,"enabled":true,{{detail}}}
        """;

    /// <summary>Reads a property's written value back out, so a test can replace it.</summary>
    private static string ValueOf(string json, string property)
    {
        int start = json.IndexOf($"\"{property}\":\"", StringComparison.Ordinal)
            + property.Length + 4;

        return json[start..json.IndexOf('"', start)];
    }
}
