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
        PolicyException exception = new("FieldDeniedForWhere", "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.IsAssignableFrom<LogicException>(exception);
    }

    [Fact]
    public void Structured_data_survives_onto_the_exception()
    {
        PolicyException exception = new("FieldDeniedForWhere", "Contact.Email", PolicyFeature.Where, DwTier.Convenience)
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
        PolicyException exception = new("FieldDeniedForWhere", "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.Contains("FieldDeniedForWhere", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Salary", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Where", exception.Message, StringComparison.Ordinal);
    }
}
