using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

// ------------------------------------------------------------------------------------ the database

/// <summary>A role, scoped to a tenant or, with none, a platform template.</summary>
public class PrRole
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public int? TenantId { get; set; }
}

/// <summary>A permission, with a value no row built from it may carry out.</summary>
public class PrPermission
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;
}

/// <summary>A permission granted to a role.</summary>
public class PrGrant
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    public PrPermission Permission { get; set; } = null!;
}

/// <summary>An entity with nothing denied at the top: every denial sits beneath a member.</summary>
public class PrShelf
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Owned, so EF Core loads it with every shelf.</summary>
    public PrLocation? Location { get; set; }

    /// <summary>Owned as well, and a collection.</summary>
    public List<PrTag> Tags { get; set; } = new();

    /// <summary>A navigation, loaded only when something includes it.</summary>
    public List<PrBook> Books { get; set; } = new();
}

public class PrLocation
{
    public string Aisle { get; set; } = string.Empty;

    [DwDenied]
    public string? Code { get; set; }
}

public class PrTag
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DwDenied]
    public string? Hidden { get; set; }
}

public class PrBook
{
    public int Id { get; set; }

    public int PrShelfId { get; set; }

    public string Title { get; set; } = string.Empty;

    [DwDenied]
    public string? Isbn { get; set; }
}

/// <summary>An entity with a denied scalar, an owned member with nothing denied beneath it, and a blob.</summary>
public class PrBin
{
    public int Id { get; set; }

    [DwDenied]
    public string? Label { get; set; }

    public PrPlace? Place { get; set; }

    public byte[] Photo { get; set; } = Array.Empty<byte>();
}

public class PrPlace
{
    public string Aisle { get; set; } = string.Empty;

    public int Row { get; set; }
}

/// <summary>An entity whose children's key no caller may see.</summary>
public class PrKeyParent
{
    public int Id { get; set; }

    public List<PrKeyChild> Kids { get; set; } = new();
}

public class PrKeyChild
{
    [DwDenied]
    public int Id { get; set; }

    public int PrKeyParentId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectedRowContext : DbContext
{
    private readonly SqliteConnection _connection;

    public ProjectedRowContext(SqliteConnection connection) => _connection = connection;

    public DbSet<PrRole> Roles => Set<PrRole>();

    public DbSet<PrPermission> Permissions => Set<PrPermission>();

    public DbSet<PrGrant> Grants => Set<PrGrant>();

    public DbSet<PrShelf> Shelves => Set<PrShelf>();

    public DbSet<PrBin> Bins => Set<PrBin>();

    public DbSet<PrKeyParent> KeyParents => Set<PrKeyParent>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PrShelf>().OwnsOne(shelf => shelf.Location);
        model.Entity<PrShelf>().OwnsMany(shelf => shelf.Tags, tag => tag.HasKey(t => t.Id));
        model.Entity<PrBin>().OwnsOne(bin => bin.Place);
    }
}

// ------------------------------------------------------------------------------------ the rows

/// <summary>DCMP's role row: a denied, forced tenant beside a nested object, a list of objects and a list of values.</summary>
[DwEntity(RequirePolicy = true)]
public sealed class PrRoleRow
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    [DwDenied, DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int? TenantId { get; set; }

    [DwNoWhere, DwNoOrder]
    public PrName? Name { get; set; }

    [DwNoWhere, DwNoOrder]
    public List<PrGrantRow> Contents { get; set; } = new();

    [DwNoWhere, DwNoOrder]
    public List<string> Codes { get; set; } = new();
}

public sealed class PrName
{
    public string Ar { get; set; } = string.Empty;

    public string En { get; set; } = string.Empty;
}

public sealed class PrGrantRow
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;
}

/// <summary>A row with nothing denied at the top and a denial inside its list.</summary>
[DwEntity(RequirePolicy = true)]
public sealed class PrOpenRow
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    [DwNoWhere, DwNoOrder]
    public List<PrSecretGrantRow> Contents { get; set; } = new();
}

public sealed class PrSecretGrantRow
{
    public string Code { get; set; } = string.Empty;

    [DwDenied]
    public string? Secret { get; set; }
}

/// <summary>A row whose list elements carry a key no caller may see, which the core adds to any narrowing.</summary>
[DwEntity(RequirePolicy = true)]
public sealed class PrKeyedRow
{
    public int Id { get; set; }

    [DwNoWhere, DwNoOrder]
    public List<PrKeyedGrantRow> Contents { get; set; } = new();
}

public sealed class PrKeyedGrantRow
{
    [DwDenied]
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;
}

/// <summary>A row whose list is typed as a collection the core does not unwrap.</summary>
[DwEntity(RequirePolicy = true)]
public sealed class PrReadOnlyRow
{
    public int Id { get; set; }

    [DwNoWhere, DwNoOrder]
    public IReadOnlyList<PrSecretGrantRow> Contents { get; set; } = new List<PrSecretGrantRow>();

    [DwNoWhere, DwNoOrder]
    public IReadOnlyList<PrGrantRow> Clean { get; set; } = new List<PrGrantRow>();
}

/// <summary>
/// A guarded query over rows its source builds, and over an entity whose denials sit beneath its
/// members: what a caller who sends no projection receives, and what one who names a member does.
/// </summary>
/// <remarks>
/// DCMP projects every read into its own row type before guarding it, and a row there carries a
/// select-denied tenant. The projection the library synthesized for that denial kept scalars only, so
/// every nested object and list came back empty. Checking it found that a denial only beneath a member
/// synthesized nothing at all, and the whole row, denied values included, came back.
/// </remarks>
public sealed class ProjectedRowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProjectedRowContext _db;

    public ProjectedRowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new ProjectedRowContext(_connection);
        _db.Database.EnsureCreated();

        PrPermission read = new() { Code = "read", Secret = "s-read" };
        PrPermission write = new() { Code = "write", Secret = "s-write" };
        PrRole ownRole = new() { Code = "R1", NameAr = "دور", NameEn = "Role one", TenantId = 1 };
        PrRole otherRole = new() { Code = "R2", NameAr = "دور", NameEn = "Role two", TenantId = 2 };
        PrRole template = new() { Code = "T0", NameAr = "قالب", NameEn = "Template", TenantId = null };

        _db.AddRange(read, write, ownRole, otherRole, template);
        _db.SaveChanges();

        _db.Grants.AddRange(
            new PrGrant { RoleId = ownRole.Id, PermissionId = read.Id },
            new PrGrant { RoleId = ownRole.Id, PermissionId = write.Id },
            new PrGrant { RoleId = otherRole.Id, PermissionId = read.Id },
            new PrGrant { RoleId = template.Id, PermissionId = read.Id });

        _db.Shelves.Add(new PrShelf
        {
            Name = "S1",
            Location = new PrLocation { Aisle = "A7", Code = "location-secret" },
            Tags = { new PrTag { Name = "t1", Hidden = "tag-secret" } },
            Books = { new PrBook { Title = "B1", Isbn = "isbn-secret" } }
        });

        _db.Bins.Add(new PrBin
        {
            Label = "label-secret", Place = new PrPlace { Aisle = "B2", Row = 3 }, Photo = new byte[] { 1, 2, 3 }
        });

        _db.KeyParents.Add(new PrKeyParent { Kids = { new PrKeyChild { Name = "k1" } } });

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", 1);

    private static DwPolicyOptions Options(DwTier tier, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun };

    private static PolicyResolver Resolver() => new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier, bool dryRun = false) where T : class =>
        source.ApplyPolicy(Caller(), Options(tier, dryRun), Resolver());

    private static Filter Everything() => new();

    private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

    private IQueryable<PrRoleRow> RoleRows() => _db.Roles.AsNoTracking().Select(role => new PrRoleRow
    {
        Id = role.Id,
        Code = role.Code,
        TenantId = role.TenantId,
        Name = new PrName { Ar = role.NameAr, En = role.NameEn },
        Contents = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .OrderBy(grant => grant.Permission.Code)
            .Select(grant => new PrGrantRow { Code = grant.Permission.Code, NameEn = grant.Permission.Code + "!" })
            .ToList(),
        Codes = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .Select(grant => grant.Permission.Code)
            .OrderBy(code => code)
            .ToList()
    });

    private IQueryable<PrOpenRow> OpenRows() => _db.Roles.AsNoTracking().Select(role => new PrOpenRow
    {
        Id = role.Id,
        Code = role.Code,
        Contents = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .OrderBy(grant => grant.Permission.Code)
            .Select(grant => new PrSecretGrantRow { Code = grant.Permission.Code, Secret = grant.Permission.Secret })
            .ToList()
    });

    private IQueryable<PrKeyedRow> KeyedRows() => _db.Roles.AsNoTracking().Select(role => new PrKeyedRow
    {
        Id = role.Id,
        Contents = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .Select(grant => new PrKeyedGrantRow { Id = grant.Permission.Id, Code = grant.Permission.Code })
            .ToList()
    });

    private IQueryable<PrReadOnlyRow> ReadOnlyRows() => _db.Roles.AsNoTracking().Select(role => new PrReadOnlyRow
    {
        Id = role.Id,
        Contents = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .Select(grant => new PrSecretGrantRow { Code = grant.Permission.Code, Secret = grant.Permission.Secret })
            .ToList(),
        Clean = _db.Grants
            .Where(grant => grant.RoleId == role.Id)
            .Select(grant => new PrGrantRow { Code = grant.Permission.Code, NameEn = grant.Permission.Code })
            .ToList()
    });

    private static string Describe(PrRoleRow row) =>
        $"{row.Code}|{row.TenantId?.ToString() ?? "-"}|{row.Name?.En ?? "-"}|"
        + $"{string.Join(",", row.Contents.Select(grant => grant.Code + "/" + grant.NameEn))}|{string.Join(",", row.Codes)}";

    private static string Describe(PrOpenRow row) =>
        $"{row.Code}:{string.Join(",", row.Contents.Select(grant => grant.Code + "/" + (grant.Secret ?? "-")))}";

    // ------------------------------------------------------------------ DW-12: a projected row stays whole

    /// <summary>
    /// DCMP's B1b, B1f and C1. The denied tenant is stripped and every other member arrives as the
    /// source built it: the nested name, the list of grants and the list of codes.
    /// </summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public async Task A_projected_row_keeps_its_nested_objects_and_lists_when_a_scalar_is_denied(DwTier tier)
    {
        string[] expected =
        {
            "R1|-|Role one|read/read!,write/write!|read,write",
            "T0|-|Template|read/read!|read"
        };

        Assert.Equal(expected, Guard(RoleRows(), tier).ToList(Everything()).Data.Select(Describe).OrderBy(x => x));
        Assert.Equal(expected, (await Guard(RoleRows(), tier).ToListAsync(Everything())).Data.Select(Describe).OrderBy(x => x));

        FilterResult<dynamic> dynamicRows = await Guard(RoleRows(), tier).ToListAsyncDynamic(Everything());
        dynamic first = dynamicRows.Data.Single(row => (string)row.Code == "R1");
        Assert.Equal("Role one", (string)first.Name.En);
        Assert.Equal(2, ((IEnumerable<PrGrantRow>)first.Contents).Count());
        Assert.Equal(new[] { "read", "write" }, (IEnumerable<string>)first.Codes);
        Assert.Null(((object)first).GetType().GetProperty("TenantId"));

        SegmentResult<PrRoleRow> segment = await Guard(RoleRows(), tier).ToListAsync(new Segment());
        Assert.Equal(expected, segment.Data!.Select(Describe).OrderBy(x => x));
    }

    /// <summary>The same row, read without the library, for comparison: nothing is lost but the tenant.</summary>
    [Fact]
    public void The_guarded_row_matches_what_plain_EF_Core_returns_less_the_denied_field()
    {
        List<string> plain = RoleRows().Where(row => row.TenantId == 1 || row.TenantId == null).ToList()
            .Select(row => { row.TenantId = null; return Describe(row); })
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(plain, Guard(RoleRows(), DwTier.Strict).ToList(Everything()).Data.Select(Describe).OrderBy(x => x));
    }

    /// <summary>The tenant stays denied everywhere else: a condition on it is refused, and the scope still applies.</summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void The_denied_field_is_still_refused_as_a_condition(DwTier tier)
    {
        Filter byTenant = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = "TenantId", DataType = DataType.Number, Operator = Operator.Equal, Values = { 2 } } }
            }
        };

        PolicyException refused = Assert.Throws<PolicyException>(() => Guard(RoleRows(), tier).ToList(byTenant));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refused.ErrorCode);
    }

    /// <summary>A member the caller names is carried whole when nothing beneath it is denied, as it was.</summary>
    [Fact]
    public void A_member_named_by_the_caller_is_carried_whole()
    {
        PrRoleRow row = Guard(RoleRows(), DwTier.Strict)
            .ToList(Selecting("Code", "Name", "Contents", "Codes")).Data.Single(r => r.Code == "R1");

        Assert.Equal("R1|-|Role one|read/read!,write/write!|read,write", Describe(row));
    }

    // ------------------------------------------------------------------ F1: a denial beneath a member

    /// <summary>
    /// Nothing is denied at the top of this row, so nothing used to be synthesized and the whole list,
    /// its denied values included, came back. The list is now narrowed around the denial.
    /// </summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public async Task A_denial_beneath_a_projected_member_narrows_it(DwTier tier)
    {
        string[] expected = { "R1:read/-,write/-", "R2:read/-", "T0:read/-" };

        PolicyQueryable<PrOpenRow> guarded = Guard(OpenRows(), tier);

        Assert.Equal(expected, guarded.ToList(Everything()).Data.Select(Describe).OrderBy(x => x));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision is { FieldPath: "Contents.Secret", Feature: PolicyFeature.Select, Action: PolicyAction.Dropped });

        Assert.Equal(expected, (await Guard(OpenRows(), tier).ToListAsync(Everything())).Data.Select(Describe).OrderBy(x => x));
        Assert.Equal(expected, (await Guard(OpenRows(), tier).ToListAsync(new Segment())).Data!.Select(Describe).OrderBy(x => x));

        FilterResult<dynamic> dynamicRows = Guard(OpenRows(), tier).ToListDynamic(Everything());
        foreach (dynamic row in dynamicRows.Data)
        {
            foreach (object grant in (System.Collections.IEnumerable)row.Contents)
            {
                Assert.Null(grant.GetType().GetProperty("Secret"));
            }
        }
    }

    /// <summary>A dry run enforces nothing, as it never has: the rows come back whole and the decision is recorded.</summary>
    [Fact]
    public void A_dry_run_records_the_denial_beneath_and_returns_the_row_whole()
    {
        PolicyQueryable<PrOpenRow> guarded = Guard(OpenRows(), DwTier.Strict, dryRun: true);

        Assert.Contains("R1:read/s-read,write/s-write", guarded.ToList(Everything()).Data.Select(Describe));
        Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Contents.Secret");
    }

    /// <summary>
    /// An included navigation of an entity used to come back whole whenever nothing at the top was
    /// denied. It is now left out, as a navigation always was once a projection was needed; the owned
    /// members, which EF Core loads with every shelf, are narrowed and kept.
    /// </summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public async Task An_entity_with_a_denial_beneath_leaves_out_its_navigations_and_narrows_what_it_owns(DwTier tier)
    {
        PrShelf shelf = (await Guard(_db.Shelves.Include(s => s.Books), tier).ToListAsync(Everything())).Data.Single();

        Assert.Equal("S1", shelf.Name);
        Assert.Empty(shelf.Books);
        Assert.Equal(("A7", (string?)null), (shelf.Location!.Aisle, shelf.Location.Code));
        Assert.Equal(("t1", (string?)null), (shelf.Tags.Single().Name, shelf.Tags.Single().Hidden));

        dynamic row = Guard(_db.Shelves.Include(s => s.Books), tier).ToListDynamic(Everything()).Data.Single();
        Assert.Null(((object)row).GetType().GetProperty("Books"));
        Assert.Null(((object)row.Location).GetType().GetProperty("Code"));
    }

    /// <summary>
    /// A top-level denial on an entity keeps what EF Core loads with it: the owned member whole and
    /// the blob, which a projection of scalars alone used to empty.
    /// </summary>
    [Fact]
    public void An_entity_keeps_its_owned_member_and_its_blob_beside_a_denied_scalar()
    {
        PrBin bin = Guard(_db.Bins, DwTier.Strict).ToList(Everything()).Data.Single();

        Assert.Null(bin.Label);
        Assert.Equal(("B2", 3), (bin.Place!.Aisle, bin.Place.Row));
        Assert.Equal(new byte[] { 1, 2, 3 }, bin.Photo);
    }

    /// <summary>
    /// Rows in memory keep a member whole when nothing beneath it is denied. One with a denial beneath
    /// is left out: the core's narrowing of a reference reads it through EF Core.
    /// </summary>
    [Fact]
    public void Rows_in_memory_keep_clean_members_whole_and_leave_out_the_rest()
    {
        PrRoleRow[] roles =
        {
            new() { Id = 1, Code = "R1", TenantId = 1, Name = new PrName { En = "One" }, Contents = { new PrGrantRow { Code = "read" } }, Codes = { "read" } }
        };
        PrOpenRow[] open = { new() { Id = 1, Code = "R1", Contents = { new PrSecretGrantRow { Code = "read", Secret = "s-read" } } } };

        PrRoleRow role = roles.AsQueryable().ApplyPolicy(Caller(), Options(DwTier.Strict), Resolver()).ToList(Everything()).Data.Single();
        PolicyQueryable<PrOpenRow> guarded = open.AsQueryable().ApplyPolicy(Caller(), Options(DwTier.Strict), Resolver());
        PrOpenRow row = guarded.ToList(Everything()).Data.Single();

        Assert.Equal("R1|-|One|read/|read", Describe(role));
        Assert.Empty(row.Contents);
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Contents" && decision.Reason!.StartsWith("left out whole", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ F2: the key the builder adds

    /// <summary>A synthesized projection cannot narrow a list whose key is denied, so it leaves the list out.</summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_member_whose_key_is_denied_is_left_out_whole(DwTier tier)
    {
        PolicyQueryable<PrKeyedRow> guarded = Guard(KeyedRows(), tier);

        Assert.All(guarded.ToList(Everything()).Data, row => Assert.Empty(row.Contents));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Contents" && decision.Reason == "left out whole: the projection would add its key 'Contents.Id', which is denied");
    }

    /// <summary>
    /// Naming a navigation whose key is denied used to drop the key from the list in the convenience
    /// tier, and the builder added it straight back. It is refused in both tiers, as naming a sibling
    /// of the key already was.
    /// </summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void Naming_a_navigation_whose_key_is_denied_is_refused(DwTier tier)
    {
        PolicyException projected = Assert.Throws<PolicyException>(
            () => Guard(KeyedRows(), tier).ToList(Selecting("Id", "Contents")));
        PolicyException entity = Assert.Throws<PolicyException>(
            () => Guard(_db.KeyParents, tier).ToList(Selecting("Id", "Kids")));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, projected.ErrorCode);
        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, entity.ErrorCode);
        Assert.Equal(tier == DwTier.Strict ? "*" : "Contents.Id", projected.FieldPath);
        Assert.Equal(tier == DwTier.Strict ? "*" : "Kids.Id", entity.FieldPath);
    }

    // ------------------------------------------------------------------ F3: a list typed IReadOnlyList<T>

    /// <summary>
    /// The projection gate read collections through a narrower list than the policy, so a list typed
    /// <c>IReadOnlyList&lt;T&gt;</c> hid its denied field: naming it carried the value out in both tiers.
    /// </summary>
    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void Naming_a_read_only_list_with_a_denial_beneath_is_refused(DwTier tier)
    {
        PolicyException typed = Assert.Throws<PolicyException>(
            () => Guard(ReadOnlyRows(), tier).ToList(Selecting("Id", "Contents")));
        PolicyException dynamicRows = Assert.Throws<PolicyException>(
            () => Guard(ReadOnlyRows(), tier).ToListDynamic(Selecting("Id", "Contents")));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, typed.ErrorCode);
        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, dynamicRows.ErrorCode);
        Assert.Equal(tier == DwTier.Strict ? "*" : "Contents.Secret", typed.FieldPath);
    }

    /// <summary>
    /// With no projection sent, the read-only list with a denial beneath is left out, since the core
    /// cannot project a path through it; the clean one beside it is carried whole.
    /// </summary>
    [Fact]
    public void A_read_only_list_is_kept_whole_when_clean_and_left_out_when_it_cannot_be_narrowed()
    {
        PolicyQueryable<PrReadOnlyRow> guarded = Guard(ReadOnlyRows(), DwTier.Strict);
        PrReadOnlyRow row = guarded.ToList(Everything()).Data.Single(r => r.Id == 1);

        Assert.Empty(row.Contents);
        Assert.Equal(new[] { "read", "write" }, row.Clean.Select(grant => grant.Code).OrderBy(x => x));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Contents" && decision.Reason == "left out whole: the core cannot project 'Contents.Code'");
        Assert.Equal(
            new[] { "read", "write" },
            Guard(ReadOnlyRows(), DwTier.Strict).ToList(Selecting("Id", "Clean")).Data
                .Single(r => r.Id == 1).Clean.Select(grant => grant.Code).OrderBy(x => x));
    }

    // ------------------------------------------------------------------ the gate, without a database

    /// <summary>A type with nothing denied anywhere keeps its null projection, on every source.</summary>
    [Fact]
    public void Nothing_is_synthesized_when_nothing_is_denied_at_any_depth()
    {
        foreach (RowShape rows in new[] { RowShape.Entity, RowShape.Projected, RowShape.InMemory })
        {
            Filter sanitized = FilterSanitizer.Sanitize<PrShelfWithoutDenials>(
                new Filter(), Resolver(), Caller(), Options(DwTier.Strict), new PolicyTrace(DwTier.Strict, dryRun: false), rows: rows);

            Assert.Null(sanitized.Selects);
        }
    }

    /// <summary>What each source keeps of the same type, with one denial beneath a list.</summary>
    [Fact]
    public void Each_source_keeps_what_it_carries()
    {
        List<string>? Synthesized(RowShape rows) => FilterSanitizer.Sanitize<PrOpenRow>(
            new Filter(), Resolver(), Caller(), Options(DwTier.Strict), new PolicyTrace(DwTier.Strict, dryRun: false), rows: rows).Selects;

        Assert.Equal(new[] { "Code", "Id" }, Synthesized(RowShape.Entity)!.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "Code", "Contents.Code", "Id" }, Synthesized(RowShape.Projected)!.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "Code", "Id" }, Synthesized(RowShape.InMemory)!.OrderBy(x => x, StringComparer.Ordinal));
    }
}

/// <summary>The shelf's shape with no attribute anywhere.</summary>
public class PrShelfWithoutDenials
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PrPlace? Place { get; set; }

    public List<PrGrantRow> Contents { get; set; } = new();
}
