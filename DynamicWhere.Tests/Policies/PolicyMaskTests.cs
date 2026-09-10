using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the eight strategies against every shape of input they can be handed.
/// </summary>
/// <remarks>
/// The cases that matter most are the ones where the input does not fit the strategy: an address
/// that is not an address, a partial mask whose kept ends cover the whole value. Each of those is a
/// place a strategy could quietly return what it was given, which is the one thing a mask must never
/// do.
/// </remarks>
public class PolicyMaskTests
{
    private const string Salt = "pepper";

    private static string? Apply(MaskStage stage, string? value) =>
        MaskEngine.Apply(stage, value, Salt);

    // ------------------------------------------------------------------------------- full

    [Fact]
    public void A_full_mask_hides_every_character_and_keeps_the_length()
    {
        Assert.Equal("*****", Apply(new MaskStage(MaskStrategy.Full), "Ada B"));
    }

    [Fact]
    public void A_full_mask_that_does_not_preserve_length_says_nothing_about_it()
    {
        // Length is itself a disclosure: a preserved-length mask over a national identifier tells a
        // caller how many digits it has, which is most of the format.
        string? masked = Apply(new MaskStage(MaskStrategy.Full, preserveLength: false), "a");

        Assert.Equal("********", masked);
        Assert.Equal(masked, Apply(new MaskStage(MaskStrategy.Full, preserveLength: false), "a much longer value"));
    }

    [Fact]
    public void A_full_mask_honours_the_configured_character()
    {
        Assert.Equal("###", Apply(new MaskStage(MaskStrategy.Full, maskChar: '#'), "abc"));
    }

    // ---------------------------------------------------------------------------- partial

    [Fact]
    public void A_partial_mask_keeps_the_ends_it_was_told_to()
    {
        Assert.Equal("ab***yz", Apply(new MaskStage(MaskStrategy.Partial, keepStart: 2, keepEnd: 2), "abcdeyz"));
    }

    [Fact]
    public void A_partial_mask_keeping_only_the_end_hides_the_rest()
    {
        Assert.Equal("********1234", Apply(new MaskStage(MaskStrategy.Partial, keepEnd: 4), "999999991234"));
    }

    [Fact]
    public void A_partial_mask_whose_kept_ends_cover_the_value_hides_all_of_it()
    {
        // KeepEnd = 4 on a four-character value would otherwise disclose the whole thing under a
        // configuration that reads as though it hides something.
        Assert.Equal("****", Apply(new MaskStage(MaskStrategy.Partial, keepEnd: 4), "1234"));
        Assert.Equal("***", Apply(new MaskStage(MaskStrategy.Partial, keepStart: 2, keepEnd: 2), "abc"));
    }

    // ------------------------------------------------------------------------------ email

    [Fact]
    public void An_email_mask_keeps_the_shape_and_the_first_characters()
    {
        Assert.Equal("a**@e******.com", Apply(new MaskStage(MaskStrategy.Email), "ada@example.com"));
    }

    [Fact]
    public void An_email_mask_that_does_not_preserve_length_says_nothing_about_it()
    {
        // A preserved-length address mask gives away how long the mailbox and the domain are, which
        // narrows a guess considerably.
        MaskStage stage = new(MaskStrategy.Email, preserveLength: false);

        Assert.Equal("a***@e***.com", Apply(stage, "ada@example.com"));
        Assert.Equal("j***@d***.com", Apply(stage, "joe@dept.com"));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@example.com")]
    [InlineData("ada@")]
    [InlineData("ada@example")]
    public void An_email_mask_hides_anything_that_is_not_an_address(string value)
    {
        // A value that reached an email mask and is not an address is a surprise in the data or a
        // misconfiguration, and returning it intact discloses whatever it actually is.
        Assert.Equal(new string('*', value.Length), Apply(new MaskStage(MaskStrategy.Email), value));
    }

    // ------------------------------------------------------------------------------ phone

    [Fact]
    public void A_phone_mask_keeps_the_last_digits_and_the_punctuation()
    {
        Assert.Equal("+*** *** ** 1234", Apply(new MaskStage(MaskStrategy.Phone), "+964 770 12 1234"));
    }

    [Fact]
    public void A_phone_mask_hides_a_number_too_short_to_keep_anything_from()
    {
        Assert.Equal("***", Apply(new MaskStage(MaskStrategy.Phone), "123"));
    }

    // ------------------------------------------------------------- regex, fixed, hash, null

    [Fact]
    public void A_regex_mask_replaces_every_match()
    {
        MaskStage stage = new(MaskStrategy.Regex, pattern: @"\d", replacement: "#");

        Assert.Equal("A#-##", Apply(stage, "A1-23"));
    }

    [Fact]
    public void A_fixed_mask_replaces_the_whole_value()
    {
        Assert.Equal("[REDACTED]", Apply(new MaskStage(MaskStrategy.Fixed, text: "[REDACTED]"), "anything"));
    }

    [Fact]
    public void A_fixed_mask_replaces_a_null_too()
    {
        // The constant is the value, so there is nothing to read from the input. A null here would
        // otherwise leak the fact that the row holds nothing.
        Assert.Equal("[REDACTED]", Apply(new MaskStage(MaskStrategy.Fixed, text: "[REDACTED]"), null));
    }

    [Fact]
    public void A_hash_is_stable_for_the_same_value()
    {
        string? first = Apply(new MaskStage(MaskStrategy.Hash), "AAA-111");
        string? second = Apply(new MaskStage(MaskStrategy.Hash), "AAA-111");

        Assert.Equal(first, second);
        Assert.NotEqual("AAA-111", first);
    }

    [Fact]
    public void A_hash_differs_under_a_different_salt()
    {
        // The salt is what stops the output being reversed by hashing a dictionary of candidates
        // and comparing, which an unsalted hash of a national identifier makes trivial.
        string? salted = MaskEngine.Apply(new MaskStage(MaskStrategy.Hash), "AAA-111", "one");
        string? differently = MaskEngine.Apply(new MaskStage(MaskStrategy.Hash), "AAA-111", "two");

        Assert.NotEqual(salted, differently);
    }

    [Fact]
    public void A_null_strategy_removes_the_value()
    {
        Assert.Null(Apply(new MaskStage(MaskStrategy.Null), "anything"));
    }

    [Fact]
    public void A_null_value_stays_null_rather_than_becoming_a_run_of_stars()
    {
        // Masking a null would invent a value where the database holds none.
        Assert.Null(Apply(new MaskStage(MaskStrategy.Full), null));
        Assert.Null(Apply(new MaskStage(MaskStrategy.Partial, keepEnd: 2), null));
    }

    // ------------------------------------------------------------- configuration is refused early

    [Fact]
    public void A_regex_mask_without_a_pattern_is_refused_at_construction()
    {
        // Without a pattern it matches nothing and the value passes through unmasked, so the
        // misconfiguration is refused where it is made rather than where it fails to bite.
        Assert.Throws<ArgumentException>(() => new MaskStage(MaskStrategy.Regex));
    }

    [Fact]
    public void A_fixed_mask_without_text_is_refused_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new MaskStage(MaskStrategy.Fixed));
    }

    [Fact]
    public void A_partial_mask_cannot_keep_a_negative_number_of_characters()
    {
        Assert.Throws<ArgumentException>(() => new MaskStage(MaskStrategy.Partial, keepEnd: -1));
    }
}
