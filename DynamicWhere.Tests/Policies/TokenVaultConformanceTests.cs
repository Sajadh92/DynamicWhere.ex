using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Tokens;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// What every token vault has to do, whatever it keeps the mapping in.
/// </summary>
/// <remarks>
/// Written against the contract rather than any implementation, so the three vaults this library
/// ships are held to one standard — the same arrangement the policy stores have, and for the same
/// reason: three implementations of an interface that quietly differ are three behaviours a
/// deployment discovers by switching.
/// <para>
/// Every test here is about one of two properties. A token must not be derivable from the value,
/// which is the whole reason the strategy exists; and the same value must map to the same token,
/// which is the whole reason it is usable. A vault that gets either one wrong is not a vault.
/// </para>
/// </remarks>
public abstract class TokenVaultConformanceTests
{
    /// <summary>The scope most of these tests work in.</summary>
    protected const string Scope = "Person.NationalId";

    /// <summary>
    /// Builds a vault over this test's storage.
    /// </summary>
    /// <remarks>
    /// xUnit builds one instance of the class per test, so the storage behind it is fresh per test
    /// and two calls within one test share it.
    /// </remarks>
    protected abstract IDwTokenVault CreateVault();

    [Fact]
    public void A_value_is_replaced_by_something_that_is_not_it()
    {
        IDwTokenVault vault = CreateVault();

        string token = vault.GetOrCreate(Scope, "AAA-000123");

        Assert.NotEqual("AAA-000123", token);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void A_token_is_thirty_two_lowercase_hexadecimal_characters()
    {
        // Asserted in the shared suite rather than per implementation, because the shape is what
        // lets a column switch vault or switch strategy without anything downstream being told.
        string token = CreateVault().GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(32, token.Length);
        Assert.All(token, c => Assert.True(char.IsDigit(c) || (c is >= 'a' and <= 'f')));
    }

    [Fact]
    public void The_same_value_gets_the_same_token()
    {
        IDwTokenVault vault = CreateVault();

        Assert.Equal(
            vault.GetOrCreate(Scope, "AAA-000123"),
            vault.GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void Two_values_get_two_tokens()
    {
        IDwTokenVault vault = CreateVault();

        Assert.NotEqual(
            vault.GetOrCreate(Scope, "AAA-000123"),
            vault.GetOrCreate(Scope, "AAA-000124"));
    }

    [Fact]
    public void One_value_in_two_scopes_gets_two_tokens()
    {
        // The isolation a scope is for. Two columns holding one value must not give each other
        // away merely by both being tokenized.
        IDwTokenVault vault = CreateVault();

        Assert.NotEqual(
            vault.GetOrCreate("Person.NationalId", "AAA-000123"),
            vault.GetOrCreate("Person.PassportNumber", "AAA-000123"));
    }

    [Fact]
    public void An_empty_value_gets_a_token_of_its_own()
    {
        IDwTokenVault vault = CreateVault();

        string empty = vault.GetOrCreate(Scope, string.Empty);

        Assert.False(string.IsNullOrEmpty(empty));
        Assert.NotEqual(empty, vault.GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void A_null_value_is_refused_rather_than_given_a_token()
    {
        // The pipeline never passes one — a null value has nothing to stand in for — so a vault
        // reached with one has been called by something that skipped that check.
        Assert.Throws<ArgumentNullException>(() => CreateVault().GetOrCreate(Scope, null!));
    }

    [Fact]
    public void A_blank_scope_is_refused()
    {
        IDwTokenVault vault = CreateVault();

        Assert.Throws<ArgumentException>(() => vault.GetOrCreate(string.Empty, "AAA"));
        Assert.Throws<ArgumentException>(() => vault.GetOrCreate("   ", "AAA"));
    }

    [Fact]
    public void A_thousand_values_get_a_thousand_tokens()
    {
        // Collision is the failure a 128-bit token is sized against, and it would show up here as
        // two values sharing a stand-in — which reads to a caller as two people being one person.
        IDwTokenVault vault = CreateVault();

        HashSet<string> tokens = new(StringComparer.Ordinal);

        for (int i = 0; i < 1000; i++)
        {
            Assert.True(tokens.Add(vault.GetOrCreate(Scope, $"value-{i}")));
        }
    }
}

/// <summary>
/// What a vault that keeps its mapping outside the process has to do on top of the shared suite.
/// </summary>
/// <remarks>
/// Durability is the whole reason to run one. <see cref="InMemoryTokenVault"/> is correct and
/// useless for a column compared across restarts, so the two tests below are exactly the ones it
/// would fail — which is why they live here rather than in the shared suite.
/// </remarks>
public abstract class DurableTokenVaultConformanceTests : TokenVaultConformanceTests
{
    [Fact]
    public void A_second_vault_over_the_same_store_reads_the_first_ones_tokens()
    {
        // The restart, as near as a test can get to one. A fresh vault with a cold cache has to
        // find what the last one wrote, or yesterday's export cannot be lined up with today's.
        string written = CreateVault().GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(written, CreateVault().GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void Many_callers_racing_for_one_value_all_get_one_token()
    {
        // Two vaults minting for the same value at the same moment is the race the store's own
        // uniqueness has to settle, because both would otherwise write and the second would win
        // for whoever read next — leaving one value with two tokens and a column that no longer
        // groups. The loser reads the winner's row instead of retrying.
        IDwTokenVault[] vaults = Enumerable.Range(0, 8).Select(_ => CreateVault()).ToArray();

        string[] tokens = new string[vaults.Length];

        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "AAA-000123"));

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));
    }
}

/// <summary>The shared suite against the process-scoped vault.</summary>
public sealed class InMemoryTokenVaultConformanceTests : TokenVaultConformanceTests
{
    private readonly InMemoryTokenVault _vault = new();

    /// <inheritdoc />
    /// <remarks>
    /// The same instance every time, because this vault is its own storage. Handing back a new one
    /// would make "two calls within one test share storage" false for this implementation alone.
    /// </remarks>
    protected override IDwTokenVault CreateVault() => _vault;

    [Fact]
    public void A_repeated_value_does_not_grow_the_dictionary()
    {
        for (int i = 0; i < 10; i++)
        {
            _vault.GetOrCreate(Scope, "AAA-000123");
        }

        Assert.Equal(1, _vault.Count);
    }

    [Fact]
    public void Clearing_it_reissues_every_token()
    {
        string before = _vault.GetOrCreate(Scope, "AAA-000123");

        _vault.Clear();

        // Stated as a test because it is the hazard of the method, not a feature of it: a cleared
        // vault does not recover the tokens it handed out, it replaces them.
        Assert.NotEqual(before, _vault.GetOrCreate(Scope, "AAA-000123"));
    }
}

/// <summary>
/// The durable suite against Entity Framework on SQLite.
/// </summary>
/// <remarks>
/// SQLite because it proves the claim the package makes: no raw SQL and no provider-specific API,
/// so the vault runs on any EF relational provider. It also means this leg needs no Docker daemon,
/// unlike the Redis one.
/// </remarks>
public sealed class SqliteTokenVaultConformanceTests : DurableTokenVaultConformanceTests, IDisposable
{
    private readonly SqlitePolicyDatabase _database = new();

    /// <inheritdoc />
    protected override IDwTokenVault CreateVault() => new EfTokenVault(() => _database.Create());

    /// <summary>Closes the database, which is what destroys it.</summary>
    public void Dispose() => _database.Dispose();
}
