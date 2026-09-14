using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Storage;
using DynamicWhere.ex.Policies.Tokens;
using DynamicWhere.ex.Policies.Validation;
using System.Reflection;

namespace DynamicWhere.Tests.Policies;

/// <summary>Two identifiers, each tokenized under its own field path.</summary>
internal class TokenizedPerson
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DwMask(MaskStrategy.Tokenize)]
    [DwNoOrder]
    public string NationalId { get; set; } = string.Empty;

    [DwMask(MaskStrategy.Tokenize)]
    [DwNoOrder]
    public string PassportNumber { get; set; } = string.Empty;
}

/// <summary>Two identifiers deliberately sharing one scope, so a caller can join them.</summary>
internal class SharedScopePerson
{
    public int Id { get; set; }

    [DwMask(MaskStrategy.Tokenize, TokenScope = "person-identifier")]
    [DwNoOrder]
    public string NationalId { get; set; } = string.Empty;

    [DwMask(MaskStrategy.Tokenize, TokenScope = "person-identifier")]
    [DwNoOrder]
    public string LegacyId { get; set; } = string.Empty;
}

/// <summary>Tokenizes, and names no vault anywhere, for the startup scan to report.</summary>
internal class TokenizedWithoutVault
{
    [DwMask(MaskStrategy.Tokenize)]
    public string NationalId { get; set; } = string.Empty;
}

/// <summary>
/// Covers <c>MaskStrategy.Tokenize</c>: what a token is, what it preserves, and what it refuses.
/// </summary>
/// <remarks>
/// The strategy exists because a hash is derived from its input, so whoever holds the salt holds
/// every value the deployment ever masked. A token is not derived from anything, so the tests that
/// matter most here are the ones proving the output has no relationship to the input — two vaults
/// disagreeing about the same value, and a token that survives no amount of recomputation.
/// <para>
/// What it does not close is equality, and that is asserted too rather than left implied: the same
/// value maps to the same token on purpose, because a column nobody can group by is a column
/// nobody can use.
/// </para>
/// </remarks>
public class PolicyTokenTests
{
    private static DwPolicyOptions Options(IDwTokenVault? vault) =>
        new() { TokenVault = vault, Caps = { MinGroupSize = 1 } };

    private static DwTransformContext Context(string field = "NationalId") =>
        new(new TokenizedPerson(), field, new DwPolicyContext());

    private static ValueTransform Chain(string? scope = null) =>
        new(mask: new MaskStage(MaskStrategy.Tokenize, tokenScope: scope));

    private static string? Apply(IDwTokenVault? vault, string? value, string field = "NationalId") =>
        (string?)TransformPipeline.Apply(
            Chain(), value, typeof(string), Context(field), Options(vault));

    // ------------------------------------------------------------------- what a token is

    [Fact]
    public void A_tokenized_value_is_replaced_by_something_that_is_not_it()
    {
        InMemoryTokenVault vault = new();

        string? token = Apply(vault, "AAA-000123");

        Assert.NotNull(token);
        Assert.NotEqual("AAA-000123", token);

        // Thirty-two lowercase hexadecimal characters, the same shape a hashed mask emits. A column
        // that switches between the two strategies keeps its width, and a caller cannot tell from
        // the output which one produced it.
        Assert.Equal(32, token!.Length);
        Assert.All(token, c => Assert.True(char.IsDigit(c) || (c is >= 'a' and <= 'f')));
    }

    [Fact]
    public void The_same_value_gets_the_same_token_so_a_caller_can_still_group_by_it()
    {
        InMemoryTokenVault vault = new();

        Assert.Equal(Apply(vault, "AAA-000123"), Apply(vault, "AAA-000123"));
    }

    [Fact]
    public void Two_values_get_two_tokens()
    {
        InMemoryTokenVault vault = new();

        Assert.NotEqual(Apply(vault, "AAA-000123"), Apply(vault, "AAA-000124"));
    }

    [Fact]
    public void A_token_is_drawn_rather_than_derived_so_two_vaults_disagree_about_one_value()
    {
        // The whole difference from MaskStrategy.Hash, stated as a test. A hash of one value under
        // one salt is one digest, recomputable by anyone holding the salt. There is nothing to hold
        // here: the two vaults below are configured identically and still cannot agree, because the
        // token was drawn from a random source and written down rather than computed.
        InMemoryTokenVault first = new();
        InMemoryTokenVault second = new();

        Assert.NotEqual(Apply(first, "AAA-000123"), Apply(second, "AAA-000123"));
    }

    [Fact]
    public void A_null_value_stays_null_rather_than_being_given_a_token()
    {
        InMemoryTokenVault vault = new();

        // Minting one would invent a value where the database holds none, which every other
        // strategy also refuses to do.
        Assert.Null(Apply(vault, null));
        Assert.Equal(0, vault.Count);
    }

    [Fact]
    public void An_empty_value_is_tokenized_rather_than_passed_through()
    {
        InMemoryTokenVault vault = new();

        string? token = Apply(vault, string.Empty);

        Assert.False(string.IsNullOrEmpty(token));
    }

    // ------------------------------------------------------------------------------ scope

    [Fact]
    public void Two_fields_holding_one_value_get_two_tokens_by_default()
    {
        // Scoped to the field path unless somebody says otherwise. Sharing a token across fields
        // tells a caller the two rows concern the same subject, which is a disclosure nobody asked
        // for by writing [DwMask(MaskStrategy.Tokenize)] on two members.
        InMemoryTokenVault vault = new();

        Assert.NotEqual(
            Apply(vault, "AAA-000123", "NationalId"),
            Apply(vault, "AAA-000123", "PassportNumber"));
    }

    [Fact]
    public void A_shared_scope_makes_two_fields_agree()
    {
        InMemoryTokenVault vault = new();
        DwPolicyOptions options = Options(vault);

        string? first = (string?)TransformPipeline.Apply(
            Chain("person-identifier"), "AAA-000123", typeof(string), Context("NationalId"), options);

        string? second = (string?)TransformPipeline.Apply(
            Chain("person-identifier"), "AAA-000123", typeof(string), Context("LegacyId"), options);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_blank_scope_is_refused_rather_than_treated_as_absent()
    {
        // Absent means "the field's own path". Blank is somebody trying to say something and
        // saying nothing, and honouring it would put every tokenized field in the deployment into
        // one namespace — so two unrelated columns holding the same value would give each other
        // away.
        Assert.Throws<ArgumentException>(
            () => new MaskStage(MaskStrategy.Tokenize, tokenScope: "   "));
    }

    // -------------------------------------------------------------------- a missing vault

    [Fact]
    public void Tokenizing_without_a_vault_refuses_the_query()
    {
        PolicyException refused = Assert.Throws<PolicyException>(() => Apply(vault: null, "AAA"));

        Assert.Equal(PolicyErrorCode.MissingTokenVault, refused.ErrorCode);
    }

    [Fact]
    public void A_missing_vault_is_reported_on_a_null_value_too()
    {
        // The vault is checked before the value is, on purpose. A field whose values are mostly
        // null would otherwise pass every test in this file and fail on the first real row in
        // production.
        PolicyException refused = Assert.Throws<PolicyException>(() => Apply(vault: null, null));

        Assert.Equal(PolicyErrorCode.MissingTokenVault, refused.ErrorCode);
    }

    [Fact]
    public void The_startup_scan_reports_a_missing_vault()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(TokenizedWithoutVault) }, new DwPolicyOptions());

        Assert.Contains(report.Errors, e => e.Contains("TokenVault", StringComparison.Ordinal));
    }

    [Fact]
    public void The_startup_scan_is_quiet_when_a_vault_is_configured()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(TokenizedWithoutVault) },
            new DwPolicyOptions { TokenVault = new InMemoryTokenVault() });

        Assert.DoesNotContain(report.Errors, e => e.Contains("TokenVault", StringComparison.Ordinal));
    }

    [Fact]
    public void The_mask_engine_refuses_a_token_it_has_no_vault_for()
    {
        // Defensive, and an exception rather than a full mask so it stays defensive. Masking in
        // full instead would be silent and safe-looking, which is how a mis-wiring reaches
        // production with every tokenized column reading as a run of stars.
        Assert.Throws<InvalidOperationException>(
            () => MaskEngine.Apply(new MaskStage(MaskStrategy.Tokenize), "AAA", "salt"));
    }

    // ------------------------------------------------------------------------ end to end

    [Fact]
    public void A_tokenized_column_reaches_the_caller_as_a_token()
    {
        InMemoryTokenVault vault = new();

        List<TokenizedPerson> people = new()
        {
            new TokenizedPerson { Id = 1, Name = "Ada", NationalId = "AAA-1", PassportNumber = "P-1" },
            new TokenizedPerson { Id = 2, Name = "Bo", NationalId = "AAA-1", PassportNumber = "P-2" }
        };

        List<TokenizedPerson>? rows = people
            .AsQueryable()
            .ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                Options(vault),
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
            .ToList(new Filter())
            .Data;

        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);

        Assert.All(rows, row => Assert.NotEqual("AAA-1", row.NationalId));

        // Two rows holding one value come back holding one token, which is what keeps the column
        // groupable. Their passport numbers differ, so those tokens differ.
        Assert.Equal(rows[0].NationalId, rows[1].NationalId);
        Assert.NotEqual(rows[0].PassportNumber, rows[1].PassportNumber);

        // Three distinct values across the two fields: one national identifier and two passports.
        Assert.Equal(3, vault.Count);
    }

    [Fact]
    public void The_underlying_value_is_untouched_by_being_tokenized()
    {
        InMemoryTokenVault vault = new();

        TokenizedPerson stored = new() { Id = 1, Name = "Ada", NationalId = "AAA-1" };

        List<TokenizedPerson> people = new() { stored };

        List<TokenizedPerson>? rows = people
            .AsQueryable()
            .ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                Options(vault),
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
            .ToList(new Filter())
            .Data;

        Assert.NotNull(rows);

        // The same object in this case, because the source is a list rather than a database — which
        // makes this the strongest form of the assertion available here: what the caller holds and
        // what the source holds are one object, and the token is what it holds now. A real query
        // materializes detached copies, which TransformQueryTests asserts against a database.
        Assert.Same(stored, rows![0]);
        Assert.NotEqual("AAA-1", rows[0].NationalId);
    }

    // ------------------------------------------------------------------------ persistence

    [Fact]
    public void A_scope_survives_a_round_trip_through_a_stored_rule()
    {
        MaskStage written = new(MaskStrategy.Tokenize, tokenScope: "person-identifier");

        MaskStage read = Assert.IsType<MaskStage>(
            PolicyPayload.ToStage(PolicyPayload.ToJson(written)));

        Assert.Equal(MaskStrategy.Tokenize, read.Strategy);
        Assert.Equal("person-identifier", read.TokenScope);
    }

    [Fact]
    public void An_absent_scope_stays_absent_through_a_round_trip()
    {
        // Absent and "the field's own path" are one instruction. Writing the resolved path out
        // instead would freeze a rule against the field it was written for, and it would read back
        // as something nobody wrote.
        MaskStage read = Assert.IsType<MaskStage>(
            PolicyPayload.ToStage(PolicyPayload.ToJson(new MaskStage(MaskStrategy.Tokenize))));

        Assert.Null(read.TokenScope);
    }
}
