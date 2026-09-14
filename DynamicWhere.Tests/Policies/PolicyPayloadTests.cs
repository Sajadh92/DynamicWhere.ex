using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the one routine that turns a persisted payload into a transform stage.
/// </summary>
/// <remarks>
/// Phase 4 moved transform detail off <c>Payload</c> and onto typed carriers, and
/// <c>PolicyFragment.AllowedOperators</c> records why: a failed cast yields null, and null there
/// means "no restriction". The same failure on a mask means the field ships unmasked. JSON makes
/// that more likely rather than less, so nothing in here may return null on a bad document — every
/// failure throws.
/// </remarks>
public class PolicyPayloadTests
{
    [Theory]
    [InlineData("""{"kind":"Mask","strategy":"Full"}""")]
    [InlineData("""{"kind":"Mask","strategy":"Partial","keepEnd":4}""")]
    [InlineData("""{"kind":"Generalize","mode":"Bucket","step":1000}""")]
    [InlineData("""{"kind":"Generalize","mode":"DatePart","part":"Month"}""")]
    [InlineData("""{"kind":"Format","format":"yyyy-MM"}""")]
    [InlineData("""{"kind":"Truncate","length":40,"ellipsis":"..."}""")]
    [InlineData("""{"kind":"Default","value":"N/A"}""")]
    [InlineData("""{"kind":"Default"}""")]
    public void Every_shipping_stage_round_trips(string json)
    {
        TransformStage parsed = PolicyPayload.ToStage(json);

        TransformStage again = PolicyPayload.ToStage(PolicyPayload.ToJson(parsed));

        Assert.Equal(parsed.Kind, again.Kind);
        Assert.Equal(PolicyPayload.ToJson(parsed), PolicyPayload.ToJson(again));
    }

    [Fact]
    public void A_mask_keeps_every_parameter_it_was_given()
    {
        MaskStage stage = Assert.IsType<MaskStage>(PolicyPayload.ToStage(
            """
            {"kind":"Mask","strategy":"Partial","keepStart":2,"keepEnd":4,"maskChar":"#",
             "preserveLength":false}
            """));

        Assert.Equal(MaskStrategy.Partial, stage.Strategy);
        Assert.Equal(2, stage.KeepStart);
        Assert.Equal(4, stage.KeepEnd);
        Assert.Equal('#', stage.MaskChar);
        Assert.False(stage.PreserveLength);
    }

    [Fact]
    public void A_replacement_distinguishes_an_absent_value_from_a_null_one()
    {
        // [DwDefault] and [DwDefault(null)] are different instructions, and one JSON document has
        // to be able to say either.
        DefaultStage absent = Assert.IsType<DefaultStage>(
            PolicyPayload.ToStage("""{"kind":"Default"}"""));

        DefaultStage supplied = Assert.IsType<DefaultStage>(
            PolicyPayload.ToStage("""{"kind":"Default","value":null}"""));

        Assert.False(absent.HasValue);
        Assert.True(supplied.HasValue);
        Assert.Null(supplied.Value);
    }

    // ---------------------------------------------------------------- everything that must throw

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"strategy":"Full"}""")]
    [InlineData("""{"kind":""}""")]
    [InlineData("""{"kind":"Tokenize"}""")]
    [InlineData("""{"kind":"NotAStage"}""")]
    [InlineData("""{"kind":5}""")]
    public void A_payload_that_cannot_be_read_is_refused_rather_than_ignored(string json)
    {
        // The whole point. Returning null for any of these would leave the field untransformed,
        // which is the value the database holds.
        Assert.ThrowsAny<ArgumentException>(() => PolicyPayload.ToStage(json));
    }

    [Theory]
    [InlineData("""{"kind":"Mask"}""")]
    [InlineData("""{"kind":"Mask","strategy":"NotAStrategy"}""")]
    [InlineData("""{"kind":"Mask","strategy":99}""")]
    [InlineData("""{"kind":"Generalize"}""")]
    [InlineData("""{"kind":"Generalize","mode":"Bucket"}""")]
    [InlineData("""{"kind":"Generalize","mode":"Round","step":0}""")]
    [InlineData("""{"kind":"Format"}""")]
    [InlineData("""{"kind":"Format","format":"  "}""")]
    [InlineData("""{"kind":"Truncate"}""")]
    [InlineData("""{"kind":"Truncate","length":-1}""")]
    [InlineData("""{"kind":"Mask","strategy":"Regex"}""")]
    [InlineData("""{"kind":"Mask","strategy":"Fixed"}""")]
    public void A_stage_missing_what_it_cannot_work_without_is_refused(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => PolicyPayload.ToStage(json));
    }

    [Fact]
    public void An_enum_is_read_by_name_case_insensitively_and_never_by_its_number()
    {
        // Names, because a number is what an absent column produces and MaskStrategy has a member
        // at zero. Case-insensitively, because the document is written by hand.
        Assert.Equal(
            MaskStrategy.Partial,
            Assert.IsType<MaskStage>(
                PolicyPayload.ToStage("""{"kind":"Mask","strategy":"partial","keepEnd":1}"""))
                .Strategy);

        Assert.ThrowsAny<ArgumentException>(
            () => PolicyPayload.ToStage("""{"kind":"Mask","strategy":0}"""));
    }

    // ---------------------------------------------------------------- the mutate escalation

    [Fact]
    public void A_stored_payload_cannot_name_a_transformer_type()
    {
        // A store that can name a CLR type to construct has escalated from "can change policy" to
        // "can construct arbitrary types", which is a different thing from the disclosure the
        // sealed ceiling is designed to bound. [DwMutate] is source code and stays available; a
        // row in a table is not.
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(
            () => PolicyPayload.ToStage(
                """{"kind":"Mutate","transformer":"System.Object, System.Private.CoreLib"}"""));

        Assert.Contains("attribute", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mutate_stage_cannot_be_written_either()
    {
        // Refused in both directions, so the asymmetry cannot be found only at read time by an
        // operator whose rule saved and then would not load.
        Assert.ThrowsAny<ArgumentException>(
            () => PolicyPayload.ToJson(new MutateStage(typeof(NoopTransformer))));
    }

    /// <summary>A transformer that exists only so a mutate stage can be constructed here.</summary>
    private sealed class NoopTransformer : IValueTransformer
    {
        public object? Transform(object? value, DwTransformContext context) => value;
    }
}
