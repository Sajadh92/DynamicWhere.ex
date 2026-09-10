using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Validation;

namespace DynamicWhere.Tests.Policies;

/// <summary>A member whose chain emits text the member cannot hold.</summary>
internal class MaskedDecimal
{
    [DwMask(MaskStrategy.Full)]
    public decimal Salary { get; set; }
}

/// <summary>A member the mask would remove, which a non-nullable type cannot express.</summary>
internal class NulledInt
{
    [DwMask(MaskStrategy.Null)]
    public int Count { get; set; }
}

/// <summary>The nullable form of <see cref="NulledInt"/>, which the scan should accept.</summary>
internal class NulledNullableInt
{
    [DwMask(MaskStrategy.Null)]
    public int? Count { get; set; }
}

/// <summary>
/// A member whose attribute the stage constructors refuse, beside one with an ordinary error.
/// </summary>
/// <remarks>
/// Round with no Step is refused by <c>GeneralizeStage</c> itself. The scan built the chain outside
/// a try, so the exception escaped the routine whose contract is to return a list — naming the
/// parameter rather than the member, and hiding every other problem in the model behind it.
/// </remarks>
internal class MalformedStage
{
    [DwGeneralize(GeneralizeMode.Round)]
    public decimal Salary { get; set; }

    [DwMask(MaskStrategy.Full)]
    public decimal Bonus { get; set; }
}

/// <summary>A member masked to a hash, which is only as good as the salt the host supplies.</summary>
internal class HashedIdentifier
{
    [DwMask(MaskStrategy.Hash)]
    public string NationalId { get; set; } = string.Empty;
}

/// <summary>Two attributes that contradict each other.</summary>
internal class ConflictingDefault
{
    [DwDefault("N/A")]
    [DwMask(MaskStrategy.Full)]
    [DwNoOrder]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>A transformer that is not one.</summary>
internal class BadMutator
{
    [DwMutate(typeof(NotATransformer))]
    [DwNoOrder]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Two members answering to the same public name.</summary>
internal class DuplicateAliases
{
    [DwAlias("name")]
    public string First { get; set; } = string.Empty;

    [DwAlias("NAME")]
    public string Second { get; set; } = string.Empty;
}

/// <summary>A constant that cannot be read as the member's type.</summary>
internal class UnreadableDefault
{
    [DwDefault("not a number")]
    public int Count { get; set; }
}

/// <summary>Transformed and still sortable, which is a warning rather than an error.</summary>
internal class MaskedButOrderable
{
    [DwMask(MaskStrategy.Full)]
    public string NationalId { get; set; } = string.Empty;
}

/// <summary>A model with nothing wrong with it.</summary>
internal class SoundModel
{
    [DwMask(MaskStrategy.Partial, KeepEnd = 4)]
    [DwNoOrder]
    public string NationalId { get; set; } = string.Empty;

    [DwGeneralize(GeneralizeMode.Round, Step = 1000)]
    [DwNoOrder]
    public decimal Salary { get; set; }

    [DwGeneralize(GeneralizeMode.Bucket, Step = 10)]
    [DwNoOrder]
    public string AgeBand { get; set; } = string.Empty;

    [DwDefault]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Covers the startup scan: every rule that turns a misconfiguration into a failed deployment
/// rather than a failed query at three in the morning.
/// </summary>
public class PolicyValidationTests
{
    private static PolicyModelReport Inspect<T>() =>
        PolicyModelValidator.Inspect(new[] { typeof(T) });

    [Fact]
    public void A_sound_model_reports_nothing()
    {
        PolicyModelReport report = Inspect<SoundModel>();

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void A_mask_on_a_numeric_member_is_an_error()
    {
        // The whole applicability question. Masking emits text and a decimal cannot hold text, so
        // the pairing fails every query that touches the member.
        PolicyModelReport report = Inspect<MaskedDecimal>();

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("emits text", StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_a_value_from_a_non_nullable_member_is_an_error()
    {
        PolicyModelReport report = Inspect<NulledInt>();

        Assert.Contains(report.Errors, e => e.Contains("MaskStrategy.Null", StringComparison.Ordinal));
    }

    [Fact]
    public void A_replacement_beside_another_transform_is_an_error()
    {
        // The short-circuit makes the other attribute dead. A member that reads as masked and emits
        // a constant is worse than either alone.
        Assert.Contains(
            Inspect<ConflictingDefault>().Errors,
            e => e.Contains("short-circuits", StringComparison.Ordinal));
    }

    [Fact]
    public void A_transformer_that_does_not_implement_the_interface_is_an_error()
    {
        Assert.Contains(
            Inspect<BadMutator>().Errors,
            e => e.Contains("IValueTransformer", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_members_sharing_a_public_name_is_an_error()
    {
        // Case-insensitively, because that is how a caller's name is matched. Left unreported, the
        // name is simply refused at query time and neither member can be filtered on by it.
        Assert.Contains(
            Inspect<DuplicateAliases>().Errors,
            e => e.Contains("already used by", StringComparison.Ordinal));
    }

    [Fact]
    public void A_constant_that_cannot_be_read_as_the_member_type_is_an_error()
    {
        Assert.Contains(
            Inspect<UnreadableDefault>().Errors,
            e => e.Contains("cannot be read as Int32", StringComparison.Ordinal));
    }

    [Fact]
    public void Transformed_but_still_sortable_is_a_warning_rather_than_an_error()
    {
        // Design section 7.4: sorting runs against the real value, so paging through a masked column
        // ranks the true order. A warning because the engine does not get to decide -- there are
        // models where the ordering is the point.
        PolicyModelReport report = Inspect<MaskedButOrderable>();

        Assert.True(report.IsValid);
        Assert.Contains(report.Warnings, w => w.Contains("ranks the true order", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_problem_is_reported_at_once_rather_than_one_per_run()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(MaskedDecimal), typeof(NulledInt), typeof(BadMutator) });

        Assert.Equal(3, report.Errors.Count);
    }

    [Fact]
    public void Validating_a_broken_model_throws_and_names_everything_wrong()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ex.Policies.Config.DwPolicy.ValidateModel(typeof(MaskedDecimal), typeof(NulledInt)));

        Assert.Contains("Salary", error.Message, StringComparison.Ordinal);
        Assert.Contains("Count", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validating_a_sound_model_returns_its_warnings_rather_than_throwing()
    {
        PolicyModelReport report = ex.Policies.Config.DwPolicy.ValidateModel(typeof(SoundModel));

        Assert.True(report.IsValid);
    }

    [Fact]
    public void The_shipped_fixture_model_is_sound()
    {
        // The suite's own entities are held to the rule they document. A fixture that could not pass
        // its own validator would make every other assertion in this file academic.
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(Person), typeof(Badge), typeof(Staff), typeof(Invoice), typeof(Journal) });

        Assert.True(report.IsValid, string.Join("; ", report.Errors));
        Assert.Empty(report.Warnings);
    }
    /// <summary>
    /// MaskStrategy.Null removes the value rather than describing it, so it emits null and not
    /// text. Counting it as text refused the very configuration the non-nullable check recommends.
    /// </summary>
    [Fact]
    public void A_nullable_member_may_be_masked_to_null()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(NulledNullableInt) });

        Assert.Empty(report.Errors);
    }

    /// <summary>
    /// The non-nullable form is still refused, so the fix above did not open the case it was
    /// written beside.
    /// </summary>
    [Fact]
    public void A_non_nullable_member_still_may_not_be()
    {
        Assert.NotEmpty(PolicyModelValidator.Inspect(new[] { typeof(NulledInt) }).Errors);
    }

    /// <summary>
    /// An attribute the stage constructors refuse is reported as an error naming its member, and
    /// does not stop the scan finding the rest.
    /// </summary>
    [Fact]
    public void A_malformed_attribute_is_reported_rather_than_thrown()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(MalformedStage) });

        Assert.Contains(report.Errors, e => e.Contains("MalformedStage.Salary", StringComparison.Ordinal));
        Assert.Contains(report.Errors, e => e.Contains("MalformedStage.Bonus", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one check that needs both halves: no attribute can carry a salt, so a scan seeing only
    /// the types cannot know whether the hash it found will work.
    /// </summary>
    [Fact]
    public void A_hash_with_no_salt_is_reported_when_the_posture_is_supplied()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(HashedIdentifier) }, new DwPolicyOptions());

        Assert.Contains(report.Errors, e => e.Contains("HashSalt", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hash_with_a_salt_is_not_reported()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(HashedIdentifier) }, new DwPolicyOptions { HashSalt = "pepper-and-more-pepper" });

        Assert.Empty(report.Errors);
    }

    /// <summary>
    /// The types-only overload cannot see a posture and does not guess at one. A host that validates
    /// before it configures would otherwise be told about a salt it is one line from setting.
    /// </summary>
    [Fact]
    public void The_scan_without_a_posture_says_nothing_about_the_salt()
    {
        Assert.Empty(PolicyModelValidator.Inspect(new[] { typeof(HashedIdentifier) }).Errors);
    }

}
