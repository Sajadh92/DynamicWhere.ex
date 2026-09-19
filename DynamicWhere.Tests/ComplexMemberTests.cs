using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

/// <summary>An entity with a denied scalar and a complex property with nothing denied beneath it.</summary>
public class CxCrate
{
    public int Id { get; set; }

    [DwDenied]
    public string? Label { get; set; }

    public CxSize Size { get; set; } = new();
}

public class CxSize
{
    public int Width { get; set; }

    public int Height { get; set; }
}

public class CxSecretSize
{
    public int Width { get; set; }

    [DwDenied]
    public int Height { get; set; }
}

/// <summary>An entity whose only denial sits inside its complex property.</summary>
public class CxSealedBox
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CxSecretSize Inner { get; set; } = new();
}

public sealed class ComplexMemberContext : DbContext
{
    private readonly SqliteConnection _connection;

    public ComplexMemberContext(SqliteConnection connection) => _connection = connection;

    public DbSet<CxCrate> Crates => Set<CxCrate>();

    public DbSet<CxSealedBox> Boxes => Set<CxSealedBox>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CxCrate>().ComplexProperty(crate => crate.Size);
        model.Entity<CxSealedBox>().ComplexProperty(box => box.Inner);
    }
}

/// <summary>
/// An EF Core complex property, which EF Core 8 loads with the entity on every query. A synthesized
/// projection keeps it whole, and leaves it out whole when something beneath it is denied: the core
/// narrows a nested object behind a comparison with null, which EF Core refuses for a complex type.
/// </summary>
/// <remarks>Not on the EF Core 6 leg, which has no complex properties.</remarks>
public sealed class ComplexMemberTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ComplexMemberContext _db;

    public ComplexMemberTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new ComplexMemberContext(_connection);
        _db.Database.EnsureCreated();

        _db.Crates.Add(new CxCrate { Label = "label-secret", Size = new CxSize { Width = 4, Height = 5 } });
        _db.Boxes.Add(new CxSealedBox { Name = "B1", Inner = new CxSecretSize { Width = 2, Height = 9 } });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
        source.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Strict },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    [Fact]
    public void A_complex_property_with_nothing_denied_beneath_is_kept_whole()
    {
        CxCrate crate = Guard(_db.Crates).ToList(new Filter()).Data.Single();

        Assert.Null(crate.Label);
        Assert.Equal((4, 5), (crate.Size.Width, crate.Size.Height));
    }

    [Fact]
    public void A_complex_property_with_a_denial_beneath_is_left_out_whole()
    {
        PolicyQueryable<CxSealedBox> guarded = Guard(_db.Boxes);

        CxSealedBox box = guarded.ToList(new Filter()).Data.Single();

        Assert.Equal("B1", box.Name);
        Assert.Equal((0, 0), (box.Inner.Width, box.Inner.Height));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Inner"
                        && decision.Reason == "left out whole: it is a complex property, which EF Core cannot narrow");
    }
}
