using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DynamicWhere.ex.Policies.EntityFrameworkCore;

/// <summary>
/// Maps <see cref="DwPolicyRuleRecord"/> onto the <c>DwPolicyRules</c> table.
/// </summary>
/// <remarks>
/// Shipped as an <see cref="IEntityTypeConfiguration{TEntity}"/> rather than only as part of
/// <see cref="DwPolicyDbContext"/>, so a consumer can apply it to the context they already have and
/// let the two tables join the migration history they already run. That is the whole reason this
/// package ships a model and no migrations: a migration is generated per provider, and design
/// section 5.6 claims one package serves SQL Server, PostgreSQL and any other EF Core provider
/// because the store uses no raw SQL. Shipping a SQL Server migration would make that false.
/// </remarks>
public sealed class DwPolicyRuleConfiguration : IEntityTypeConfiguration<DwPolicyRuleRecord>
{
    /// <summary>The table this configuration maps to.</summary>
    public const string Table = "DwPolicyRules";

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public void Configure(EntityTypeBuilder<DwPolicyRuleRecord> builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.ToTable(Table);
        builder.HasKey(r => r.Id);

        // Never generated. The identifier belongs to the rule, and a store that assigned its own
        // would make the same rule two different rules in two different stores.
        builder.Property(r => r.Id).ValueGeneratedNever();

        // The enumerations, as names. Sized to the longest member each can hold, with room: an
        // unbounded string column is nvarchar(max) on SQL Server, which cannot be indexed.
        builder.Property(r => r.SubjectKind).IsRequired().HasMaxLength(32);
        builder.Property(r => r.Effect).IsRequired().HasMaxLength(32);
        builder.Property(r => r.Features).IsRequired().HasMaxLength(128);

        builder.Property(r => r.SubjectKey).HasMaxLength(256);
        builder.Property(r => r.SubjectKeyNormalized).HasMaxLength(256);
        builder.Property(r => r.EntityType).IsRequired().HasMaxLength(512);
        builder.Property(r => r.FieldPath).IsRequired().HasMaxLength(512);
        builder.Property(r => r.Purpose).HasMaxLength(128);
        builder.Property(r => r.CreatedBy).HasMaxLength(256);
        builder.Property(r => r.UpdatedBy).HasMaxLength(256);

        // Deliberately unbounded. A mask specification with a regex pattern has no natural ceiling,
        // and a truncated payload is a transform that reads back as something else.
        builder.Property(r => r.Detail);

        foreach (string instant in new[]
        {
            nameof(DwPolicyRuleRecord.ValidFrom),
            nameof(DwPolicyRuleRecord.ValidTo),
            nameof(DwPolicyRuleRecord.CreatedAt),
            nameof(DwPolicyRuleRecord.UpdatedAt)
        })
        {
            builder.Property<DateTimeOffset?>(instant).HasConversion(Utc);
        }

        // Design section 5.6's two indexes. The first names SubjectKeyNormalized rather than
        // SubjectKey because that is the column the narrow load filters on — indexing the column an
        // operator reads and not the one the query uses would leave the scan in place.
        builder.HasIndex(
            r => new { r.SubjectKind, r.SubjectKeyNormalized, r.EntityType });

        builder.HasIndex(r => new { r.EntityType, r.FieldPath });
    }

    /// <summary>
    /// Stores every instant as UTC.
    /// </summary>
    /// <remarks>
    /// Npgsql refuses a <see cref="DateTimeOffset"/> whose offset is not zero when writing
    /// <c>timestamptz</c>, so a rule valid from midnight in Baghdad would throw on PostgreSQL and
    /// save on SQL Server. Normalizing here keeps one package serving both. Nothing is lost that
    /// this library reads: validity is compared as an instant, and the offset an operator typed in
    /// is not part of the comparison.
    /// </remarks>
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc =
        new(value => value.ToUniversalTime(), value => value);
}

/// <summary>
/// Maps <see cref="DwPolicyVersionRecord"/> onto the single-row <c>DwPolicyVersion</c> table.
/// </summary>
public sealed class DwPolicyVersionConfiguration : IEntityTypeConfiguration<DwPolicyVersionRecord>
{
    /// <summary>The table this configuration maps to.</summary>
    public const string Table = "DwPolicyVersion";

    /// <summary>The key of the one row this table holds.</summary>
    public const int SingleRowId = 1;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public void Configure(EntityTypeBuilder<DwPolicyVersionRecord> builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.ToTable(Table);
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // The token that turns a lost update into an exception. See DwPolicyVersionRecord.Version.
        builder.Property(v => v.Version).IsConcurrencyToken();
    }
}

/// <summary>
/// A context holding only the two policy tables, for a consumer who would rather not put them in
/// their own.
/// </summary>
/// <remarks>
/// Optional. <see cref="EfPolicyStore"/> is written against <see cref="DbContext"/> and reaches its
/// tables through <c>Set&lt;T&gt;</c>, so it serves this context and a consumer's own equally —
/// which is what makes applying <see cref="DwPolicyRuleConfiguration"/> to an existing context a
/// real option rather than a documented one.
/// </remarks>
public class DwPolicyDbContext : DbContext
{
    /// <summary>Initializes the context.</summary>
    /// <param name="options">How to connect.</param>
    public DwPolicyDbContext(DbContextOptions<DwPolicyDbContext> options) : base(options)
    {
    }

    /// <summary>The rules.</summary>
    public DbSet<DwPolicyRuleRecord> PolicyRules => Set<DwPolicyRuleRecord>();

    /// <summary>The single-row version.</summary>
    public DbSet<DwPolicyVersionRecord> PolicyVersion => Set<DwPolicyVersionRecord>();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> is null.</exception>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);

        // The same two configurations a consumer applies to their own context, so there is one
        // schema and not a shipped one and an applied one that drift.
        modelBuilder.ApplyConfiguration(new DwPolicyRuleConfiguration());
        modelBuilder.ApplyConfiguration(new DwPolicyVersionConfiguration());
    }
}
