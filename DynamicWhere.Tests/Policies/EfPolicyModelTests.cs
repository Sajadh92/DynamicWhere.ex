using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The relational mapping: what a rule looks like as a row, and what a row must not be able to
/// become.
/// </summary>
/// <remarks>
/// The row is a <see cref="DwPolicyRuleRecord"/> and never a <see cref="PolicyRule"/>, because
/// Entity Framework materializes by setting properties on a mutable object — which would walk
/// straight past every refusal in the rule's constructor, the ones Phase 5 mutation-checked one at
/// a time. Converting the record runs that constructor on every row of every load.
/// </remarks>
public class EfPolicyModelTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    // ---------------------------------------------------------------- the round trip

    [Fact]
    public void A_rule_survives_the_trip_through_a_row()
    {
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
            validTo: from.AddYears(1),
            purpose: "billing",
            transform: new MaskStage(MaskStrategy.Partial, keepEnd: 4),
            allowedOperators: new[] { Operator.Equal },
            alias: "pay",
            forced: ForcedPredicate.FromContext("TenantId", Operator.Equal, DataType.Number, "T"),
            requiredOperators: Array.Empty<Operator>(),
            createdBy: "sajjad",
            createdAt: from);

        PolicyRule back = DwPolicyRuleRecord.FromRule(rule).ToRule();

        Assert.Equal(rule.Id, back.Id);
        Assert.Equal(DwSubjectKind.Role, back.SubjectKind);
        Assert.Equal("Manager", back.SubjectKey);
        Assert.Equal(StaffType, back.EntityType);
        Assert.Equal("Salary", back.FieldPath);
        Assert.Equal(PolicyFeature.Where | PolicyFeature.Select, back.Features);
        Assert.Equal(PolicyEffect.Mask, back.Effect);
        Assert.Equal(10, back.Priority);
        Assert.False(back.Enabled);
        Assert.Equal(from, back.ValidFrom);
        Assert.Equal("billing", back.Purpose);
        Assert.Equal("sajjad", back.CreatedBy);

        // The four carriers design section 5.1 has no column for, through the detail column.
        Assert.IsType<MaskStage>(back.Transform);
        Assert.Equal(new[] { Operator.Equal }, back.AllowedOperators!);
        Assert.Equal("pay", back.Alias);
        Assert.Equal("T", back.Forced!.ContextValue);
        Assert.Empty(back.RequiredOperators!);
    }

    [Fact]
    public void A_global_rule_carries_no_subject_key_and_no_normalized_key()
    {
        DwPolicyRuleRecord row = DwPolicyRuleRecord.FromRule(
            new PolicyRule(
                DwSubjectKind.Global, null, StaffType, "*", PolicyFeature.Select, PolicyEffect.Deny));

        Assert.Null(row.SubjectKey);
        Assert.Null(row.SubjectKeyNormalized);
        Assert.Null(row.ToRule().SubjectKey);
    }

    // ---------------------------------------------------------------- names, never numbers

    [Fact]
    public void The_enumerations_are_stored_as_names()
    {
        // A column holding an integer hands the all-zero row to anything that can write a zero,
        // including a migration adding the column with NOT NULL DEFAULT 0. Allow, Global and Text
        // are all zero, so that row reads as "grant everyone everything".
        DwPolicyRuleRecord row = DwPolicyRuleRecord.FromRule(
            new PolicyRule(
                DwSubjectKind.Tenant,
                "acme",
                StaffType,
                "Salary",
                PolicyFeature.Where | PolicyFeature.Order,
                PolicyEffect.Deny));

        Assert.Equal("Tenant", row.SubjectKind);
        Assert.Equal("Deny", row.Effect);
        Assert.Contains("Where", row.Features, StringComparison.Ordinal);
        Assert.Contains("Order", row.Features, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("Sideways")]
    public void A_subject_kind_that_is_not_a_name_this_library_defines_is_refused(string kind)
    {
        DwPolicyRuleRecord row = Row();
        row.SubjectKind = kind;

        Assert.ThrowsAny<ArgumentException>(() => row.ToRule());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Permit")]
    public void An_effect_that_is_not_a_name_this_library_defines_is_refused(string effect)
    {
        DwPolicyRuleRecord row = Row();
        row.Effect = effect;

        Assert.ThrowsAny<ArgumentException>(() => row.ToRule());
    }

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("Sideways")]
    [InlineData("None")]
    public void A_feature_set_that_is_not_a_name_this_library_defines_is_refused(string features)
    {
        DwPolicyRuleRecord row = Row();
        row.Features = features;

        Assert.ThrowsAny<ArgumentException>(() => row.ToRule());
    }

    // ---------------------------------------------------------------- the collation trap

    [Fact]
    public void The_subject_key_is_normalized_on_the_way_in()
    {
        // PolicyRule matches subjects with OrdinalIgnoreCase because identities come from token
        // claims. A relational '=' honours the column's collation instead: case-insensitive on
        // SQL Server's default, case-sensitive on PostgreSQL. The same rule and the same query
        // would then apply on a developer's SQL Server and not on the production Postgres, and a
        // user-level denial that does not apply is a denial that does nothing.
        DwPolicyRuleRecord row = DwPolicyRuleRecord.FromRule(
            new PolicyRule(
                DwSubjectKind.User, "Alice", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny));

        Assert.Equal("Alice", row.SubjectKey);
        Assert.Equal("alice", row.SubjectKeyNormalized);

        // The key an operator typed survives unchanged, because Phase 7's /explain shows it back.
        Assert.Equal("Alice", row.ToRule().SubjectKey);
    }

    [Fact]
    public void Normalization_does_not_depend_on_the_current_culture()
    {
        // Turkish dotless i: "I".ToLower() is "ı" under tr-TR, so a culture-sensitive normalizer
        // stores a key that a caller on another host never matches.
        DwPolicyRuleRecord row = DwPolicyRuleRecord.FromRule(
            new PolicyRule(
                DwSubjectKind.User, "ALICE", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny));

        Assert.Equal("alice", row.SubjectKeyNormalized);
        Assert.Equal(DwPolicyRuleRecord.Normalize("ALICE"), row.SubjectKeyNormalized);
    }

    // ---------------------------------------------------------------- a row that cannot be read

    [Fact]
    public void A_detail_column_that_cannot_be_read_fails_the_row_rather_than_being_skipped()
    {
        // Skipping it would load a rule enforcing less than it says, with the snapshot counting it
        // and the instance healthy. Throwing routes into machinery that already exists: fatal at
        // startup, and a refresh failure afterwards.
        DwPolicyRuleRecord row = Row();
        row.Detail = "{ not json";

        Assert.ThrowsAny<ArgumentException>(() => row.ToRule());
    }

    [Fact]
    public void A_row_still_passes_through_the_rules_own_boundary()
    {
        DwPolicyRuleRecord row = Row();
        row.EntityType = "Staff";

        Assert.ThrowsAny<ArgumentException>(() => row.ToRule());
    }

    // ---------------------------------------------------------------- the schema

    [Fact]
    public void The_schema_carries_the_two_indexes_the_design_asks_for()
    {
        using DwPolicyDbContext context = Context();

        IEntityType entity = context.Model.FindEntityType(typeof(DwPolicyRuleRecord))!;

        string[][] indexes = entity.GetIndexes()
            .Select(i => i.Properties.Select(p => p.Name).ToArray())
            .ToArray();

        Assert.Contains(
            indexes,
            i => i.SequenceEqual(new[]
            {
                nameof(DwPolicyRuleRecord.SubjectKind),
                nameof(DwPolicyRuleRecord.SubjectKeyNormalized),
                nameof(DwPolicyRuleRecord.EntityType)
            }));

        Assert.Contains(
            indexes,
            i => i.SequenceEqual(new[]
            {
                nameof(DwPolicyRuleRecord.EntityType), nameof(DwPolicyRuleRecord.FieldPath)
            }));
    }

    [Fact]
    public void The_tables_are_named_as_the_design_specifies()
    {
        using DwPolicyDbContext context = Context();

        Assert.Equal(
            "DwPolicyRules", context.Model.FindEntityType(typeof(DwPolicyRuleRecord))!.GetTableName());

        Assert.Equal(
            "DwPolicyVersion",
            context.Model.FindEntityType(typeof(DwPolicyVersionRecord))!.GetTableName());
    }

    [Fact]
    public void The_version_is_a_concurrency_token()
    {
        // Two writers reading 41 and both writing 42 is a version that did not move for one of
        // them, so an instance polling for a change sees none and keeps serving withdrawn rules.
        // The token turns the lost update into an exception the store retries.
        using DwPolicyDbContext context = Context();

        IProperty version = context.Model
            .FindEntityType(typeof(DwPolicyVersionRecord))!
            .FindProperty(nameof(DwPolicyVersionRecord.Version))!;

        Assert.True(version.IsConcurrencyToken);
    }

    [Fact]
    public void The_configurations_can_be_applied_to_a_consumers_own_context()
    {
        // The package ships the model and not the migrations, so the two tables can join a
        // migration history the consumer already has. That only works if the configurations are
        // usable outside DwPolicyDbContext.
        using ForeignContext context = new();

        Assert.NotNull(context.Model.FindEntityType(typeof(DwPolicyRuleRecord)));
        Assert.NotNull(context.Model.FindEntityType(typeof(DwPolicyVersionRecord)));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A valid row, for a test that then breaks exactly one column.</summary>
    private static DwPolicyRuleRecord Row() =>
        DwPolicyRuleRecord.FromRule(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny));

    /// <summary>A context over a throwaway in-memory SQLite database.</summary>
    private static DwPolicyDbContext Context()
    {
        SqliteConnection connection = new("DataSource=:memory:");

        connection.Open();

        return new DwPolicyDbContext(
            new DbContextOptionsBuilder<DwPolicyDbContext>().UseSqlite(connection).Options);
    }

    /// <summary>A context that is not the shipped one, applying the shipped configurations.</summary>
    private sealed class ForeignContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite("DataSource=:memory:");

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfiguration(new DwPolicyRuleConfiguration());
            builder.ApplyConfiguration(new DwPolicyVersionConfiguration());
        }
    }
}
