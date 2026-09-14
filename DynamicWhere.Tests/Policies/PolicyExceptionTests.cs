using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyException"/>, which carries structured data alongside the message so a
/// caller can react to a refusal without parsing text.
/// </summary>
public class PolicyExceptionTests
{
    [Fact]
    public void A_policy_exception_is_catchable_as_a_logic_exception()
    {
        PolicyException exception = new(PolicyErrorCode.FieldDeniedForWhere, "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.IsAssignableFrom<LogicException>(exception);
    }

    [Fact]
    public void Structured_data_survives_onto_the_exception()
    {
        PolicyException exception = new(PolicyErrorCode.FieldDeniedForWhere, "Contact.Email", PolicyFeature.Where, DwTier.Convenience)
        {
            RuleId = "a3f2",
            SourceOrigin = "DwNoWhereAttribute"
        };

        Assert.Equal("Contact.Email", exception.FieldPath);
        Assert.Equal(PolicyFeature.Where, exception.Feature);
        Assert.Equal(DwTier.Convenience, exception.Tier);
        Assert.Equal("a3f2", exception.RuleId);
        Assert.Equal("DwNoWhereAttribute", exception.SourceOrigin);
    }

    [Fact]
    public void The_message_names_the_code_the_field_and_the_feature()
    {
        PolicyException exception = new(PolicyErrorCode.FieldDeniedForWhere, "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.Contains("FieldDeniedForWhere", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Salary", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Where", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_exception_carries_a_typed_code_a_caller_can_switch_on()
    {
        PolicyException exception = new(PolicyErrorCode.FieldDeniedForWhere, "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, exception.ErrorCode);
        Assert.Equal("FieldDeniedForWhere", exception.Code);
    }

    [Fact]
    public void Every_error_code_has_a_distinct_name_usable_as_the_string_code()
    {
        PolicyErrorCode[] codes = Enum.GetValues<PolicyErrorCode>();

        Assert.Equal(codes.Length, codes.Select(c => c.ToString()).Distinct().Count());
    }

    [Theory]
    [InlineData(PolicyErrorCode.RequiredFilterMissing, 11)]
    [InlineData(PolicyErrorCode.MissingContextValue, 12)]
    [InlineData(PolicyErrorCode.AmbiguousFieldName, 13)]
    public void The_injection_codes_are_appended_never_renumbered(PolicyErrorCode code, int expected)
    {
        // The numeric values are contract the moment a caller serializes one. Appending is the only
        // safe edit, and this pins the three this phase adds against a later reshuffle.
        Assert.Equal(expected, (int)code);
    }

    [Fact]
    public void Every_existing_code_keeps_the_value_it_shipped_with()
    {
        Assert.Equal(1, (int)PolicyErrorCode.FieldDeniedForWhere);
        Assert.Equal(10, (int)PolicyErrorCode.PolicyRequired);
    }
}
