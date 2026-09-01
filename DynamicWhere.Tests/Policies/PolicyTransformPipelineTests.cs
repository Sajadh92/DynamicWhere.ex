using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;

namespace DynamicWhere.Tests.Policies;

/// <summary>A transformer that records what it was given and bands a salary.</summary>
internal sealed class SalaryBandTransformer : IValueTransformer
{
    /// <inheritdoc />
    public object? Transform(object? value, DwTransformContext context) =>
        value is decimal amount && amount >= 100000m ? 999m : 1m;
}

/// <summary>A transformer that reads a sibling property, proving the whole entity is reachable.</summary>
internal sealed class DepartmentAwareTransformer : IValueTransformer
{
    /// <inheritdoc />
    public object? Transform(object? value, DwTransformContext context) =>
        context.Entity is SecuredEmployee { Name: "Ada" } ? 0m : value;
}

/// <summary>A transformer that fails, to prove a failure is not swallowed.</summary>
internal sealed class ExplodingTransformer : IValueTransformer
{
    /// <inheritdoc />
    public object? Transform(object? value, DwTransformContext context) =>
        throw new InvalidOperationException("boom");
}

/// <summary>Does not implement the interface, so it cannot be resolved.</summary>
internal sealed class NotATransformer
{
}

/// <summary>
/// Covers the chain: the order its stages run in, the short-circuit, and the rule that decides
/// which chains a member can carry at all.
/// </summary>
public class PolicyTransformPipelineTests
{
    private static DwPolicyOptions Options() => new() { HashSalt = "pepper" };

    private static DwTransformContext Context(object? entity = null) =>
        new(entity ?? new SecuredEmployee(), "Salary", new DwPolicyContext());

    private static object? Apply(ValueTransform chain, object? value, Type memberType, object? entity = null) =>
        TransformPipeline.Apply(chain, value, memberType, Context(entity), Options());

    // -------------------------------------------------------------------------------- order

    [Fact]
    public void Generalize_runs_before_format_and_format_before_mask()
    {
        // A date reduced to its year, rendered, then partly hidden. Reversing any pair changes the
        // answer, which is why the order is fixed rather than left to declaration.
        ValueTransform chain = new(
            generalize: new GeneralizeStage(GeneralizeMode.DatePart, part: DatePart.Year),
            format: new FormatStage("yyyy-MM-dd"),
            mask: new MaskStage(MaskStrategy.Partial, keepStart: 4));

        Assert.Equal("1987******", Apply(chain, new DateTime(1987, 6, 15), typeof(string)));
    }

    [Fact]
    public void Truncate_runs_last_so_the_length_cap_is_the_final_word()
    {
        ValueTransform chain = new(
            mask: new MaskStage(MaskStrategy.Full),
            truncate: new TruncateStage(3));

        Assert.Equal("***", Apply(chain, "a much longer value", typeof(string)));
    }

    [Fact]
    public void A_transformer_runs_first_and_sees_the_real_value()
    {
        ValueTransform chain = new(mutate: new MutateStage(typeof(SalaryBandTransformer)));

        Assert.Equal(999m, Apply(chain, 120000m, typeof(decimal)));
        Assert.Equal(1m, Apply(chain, 50000m, typeof(decimal)));
    }

    [Fact]
    public void A_transformer_can_read_the_rest_of_the_entity()
    {
        ValueTransform chain = new(mutate: new MutateStage(typeof(DepartmentAwareTransformer)));

        SecuredEmployee named = new() { Name = "Ada" };
        SecuredEmployee other = new() { Name = "Bo" };

        Assert.Equal(0m, Apply(chain, 500m, typeof(decimal), named));
        Assert.Equal(500m, Apply(chain, 500m, typeof(decimal), other));
    }

    [Fact]
    public void A_failing_transformer_fails_the_query_rather_than_returning_the_real_value()
    {
        // The one outcome a transformer must never produce. Catching this into "return what you were
        // given" would hand the caller exactly the value the transform exists to hide.
        ValueTransform chain = new(mutate: new MutateStage(typeof(ExplodingTransformer)));

        Assert.Throws<InvalidOperationException>(() => Apply(chain, 1m, typeof(decimal)));
    }

    [Fact]
    public void A_type_that_is_not_a_transformer_is_refused()
    {
        ValueTransform chain = new(mutate: new MutateStage(typeof(NotATransformer)));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => Apply(chain, 1m, typeof(decimal)));

        Assert.Contains("IValueTransformer", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------- short circuit

    [Fact]
    public void A_replacement_discards_every_other_stage()
    {
        // Masking a value already replaced by a constant obscures nothing, and composing them would
        // make the emitted value depend on an ordering rule invisible from the entity.
        ValueTransform chain = new(
            mask: new MaskStage(MaskStrategy.Full),
            truncate: new TruncateStage(2),
            @default: new DefaultStage("N/A", hasValue: true));

        Assert.Equal("N/A", Apply(chain, "secret", typeof(string)));
    }

    [Fact]
    public void A_replacement_with_no_constant_uses_the_member_types_own_default()
    {
        ValueTransform chain = new(@default: new DefaultStage(null, hasValue: false));

        Assert.Equal(0m, Apply(chain, 120000m, typeof(decimal)));
        Assert.Equal(0, Apply(chain, 42, typeof(int)));
        Assert.Null(Apply(chain, "text", typeof(string)));
    }

    [Fact]
    public void A_chain_reports_the_stages_it_will_run_in_the_order_it_runs_them()
    {
        ValueTransform chain = new(
            generalize: new GeneralizeStage(GeneralizeMode.Round, step: 10),
            mask: new MaskStage(MaskStrategy.Full),
            truncate: new TruncateStage(3));

        Assert.Equal(
            new[] { TransformKind.Generalize, TransformKind.Mask, TransformKind.Truncate },
            chain.Stages.Select(s => s.Kind));
    }

    [Fact]
    public void A_replacement_alongside_another_stage_is_a_conflict_the_chain_can_report()
    {
        ValueTransform conflicted = new(
            mask: new MaskStage(MaskStrategy.Full), @default: new DefaultStage(null, false));
        ValueTransform alone = new(@default: new DefaultStage(null, false));

        Assert.True(conflicted.HasConflictingDefault);
        Assert.False(alone.HasConflictingDefault);
    }

    // -------------------------------------------------------------------- the assignability rule

    [Fact]
    public void A_mask_on_a_numeric_member_is_refused_rather_than_silently_skipped()
    {
        // The whole applicability question reduces to this one check. A mask emits text and text is
        // not assignable to a decimal, so the pairing fails the query instead of handing back the
        // number the mask was meant to hide.
        ValueTransform chain = new(mask: new MaskStage(MaskStrategy.Full));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => Apply(chain, 120000m, typeof(decimal)));

        Assert.Contains("cannot be assigned to Decimal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_strategy_on_a_non_nullable_member_is_refused()
    {
        // Emitting default(int) instead would turn a removed value into a real-looking zero.
        ValueTransform chain = new(mask: new MaskStage(MaskStrategy.Null));

        Assert.Throws<InvalidOperationException>(() => Apply(chain, 42, typeof(int)));
    }

    [Fact]
    public void A_null_strategy_on_a_nullable_member_is_allowed()
    {
        ValueTransform chain = new(mask: new MaskStage(MaskStrategy.Null));

        Assert.Null(Apply(chain, 42, typeof(int?)));
        Assert.Null(Apply(chain, "text", typeof(string)));
    }

    [Fact]
    public void Generalization_keeps_the_member_type_so_a_numeric_field_can_hold_it()
    {
        ValueTransform chain = new(generalize: new GeneralizeStage(GeneralizeMode.Round, step: 10000));

        Assert.Equal(120000m, Apply(chain, 118500m, typeof(decimal)));
    }

    // ------------------------------------------------------------------------- generalization

    [Theory]
    [InlineData(118500, 10000, 120000)]
    [InlineData(23, 10, 20)]
    [InlineData(-14, 10, -10)]
    public void Rounding_snaps_to_the_nearest_step(int value, int step, int expected)
    {
        ValueTransform chain = new(generalize: new GeneralizeStage(GeneralizeMode.Round, step: step));

        Assert.Equal(expected, Apply(chain, value, typeof(int)));
    }

    [Theory]
    [InlineData(27, 10, "20-29")]
    [InlineData(30, 10, "30-39")]
    [InlineData(-1, 10, "-10--1")]
    public void Bucketing_labels_the_band_the_value_falls_in(int value, int step, string expected)
    {
        ValueTransform chain = new(generalize: new GeneralizeStage(GeneralizeMode.Bucket, step: step));

        Assert.Equal(expected, Apply(chain, value, typeof(string)));
    }

    [Fact]
    public void Truncating_decimals_discards_rather_than_rounds()
    {
        ValueTransform chain = new(generalize: new GeneralizeStage(GeneralizeMode.Truncate, decimals: 2));

        Assert.Equal(33.19m, Apply(chain, 33.199999m, typeof(decimal)));
    }

    [Theory]
    [InlineData(DatePart.Year, "1987-01-01")]
    [InlineData(DatePart.Quarter, "1987-04-01")]
    [InlineData(DatePart.Month, "1987-06-01")]
    [InlineData(DatePart.Day, "1987-06-15")]
    public void A_date_part_reduces_to_the_first_instant_of_its_period(DatePart part, string expected)
    {
        // Every part yields a date rather than a number, so the member keeps its type and a caller
        // can still sort by it.
        ValueTransform chain = new(generalize: new GeneralizeStage(GeneralizeMode.DatePart, part: part));

        object? reduced = Apply(chain, new DateTime(1987, 6, 15, 13, 45, 0), typeof(DateTime));

        Assert.Equal(DateTime.Parse(expected), reduced);
    }

    [Fact]
    public void A_rounding_step_of_zero_is_refused_at_construction()
    {
        // It would divide by zero at the moment of masking, on a caller's query.
        Assert.Throws<ArgumentException>(() => new GeneralizeStage(GeneralizeMode.Round, step: 0));
        Assert.Throws<ArgumentException>(() => new GeneralizeStage(GeneralizeMode.Bucket, step: 0));
    }

    // ------------------------------------------------------------------------------ coercion

    [Theory]
    [InlineData("N/A", typeof(string), "N/A")]
    [InlineData("12.5", typeof(decimal), 12.5)]
    [InlineData("42", typeof(int), 42)]
    [InlineData("true", typeof(bool), true)]
    public void A_constant_is_converted_to_the_member_type(string value, Type target, object expected)
    {
        object? coerced = TransformPipeline.Coerce(value, target);

        Assert.Equal(Convert.ChangeType(expected, Nullable.GetUnderlyingType(target) ?? target), coerced);
    }

    [Fact]
    public void A_constant_reaches_the_types_C_sharp_forbids_as_attribute_arguments()
    {
        // The reason constants are written as text at all: C# will not accept a decimal or a
        // DateTime as an attribute argument.
        Assert.Equal(12.34m, TransformPipeline.Coerce("12.34", typeof(decimal)));
        Assert.Equal(new DateTime(2026, 1, 31), TransformPipeline.Coerce("2026-01-31", typeof(DateTime)));
        Assert.Equal(
            Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            TransformPipeline.Coerce("0f8fad5b-d9cb-469f-a165-70867728950e", typeof(Guid)));
    }

    [Fact]
    public void A_null_constant_becomes_the_member_types_default()
    {
        Assert.Equal(0, TransformPipeline.Coerce(null, typeof(int)));
        Assert.Null(TransformPipeline.Coerce(null, typeof(int?)));
        Assert.Null(TransformPipeline.Coerce(null, typeof(string)));
    }

    [Fact]
    public void An_empty_chain_leaves_the_value_alone()
    {
        Assert.Equal("untouched", Apply(new ValueTransform(), "untouched", typeof(string)));
        Assert.True(new ValueTransform().IsEmpty);
    }
}
