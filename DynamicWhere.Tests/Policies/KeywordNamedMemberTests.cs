using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using System.Linq.Dynamic.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

public class KwLink
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Three navigations named after the parser's context keywords, and a collection.</summary>
public class KwNode
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int TenantId { get; set; }

    public int? RootId { get; set; }

    public KwLink? Root { get; set; }

    public int? ItId { get; set; }

    public KwLink? It { get; set; }

    public int? ParentId { get; set; }

    public KwLink? Parent { get; set; }

    public List<KwLeaf> Leaves { get; set; } = new();
}

/// <summary>A collection element with a navigation named <c>Root</c> of its own.</summary>
public class KwLeaf
{
    public int Id { get; set; }

    public int KwNodeId { get; set; }

    public int? RootId { get; set; }

    public KwLink? Root { get; set; }
}

/// <summary>A record whose own name is denied, reached through a navigation whose name is not.</summary>
public class KwGuardedNode
{
    public int Id { get; set; }

    [DwDenied]
    public string Name { get; set; } = string.Empty;

    public int? RootId { get; set; }

    public KwLink? Root { get; set; }
}

public class KwTenantLink
{
    public int Id { get; set; }

    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public int TenantId { get; set; }
}

/// <summary>A record scoped through its navigation, not through a member of its own.</summary>
public class KwScopedNode
{
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int? RootId { get; set; }

    public KwTenantLink? Root { get; set; }
}

public sealed class KeywordContext : DbContext
{
    private readonly SqliteConnection _connection;

    public KeywordContext(SqliteConnection connection) => _connection = connection;

    public DbSet<KwNode> Nodes => Set<KwNode>();

    public DbSet<KwLink> Links => Set<KwLink>();

    public DbSet<KwGuardedNode> GuardedNodes => Set<KwGuardedNode>();

    public DbSet<KwScopedNode> ScopedNodes => Set<KwScopedNode>();

    public DbSet<KwTenantLink> TenantLinks => Set<KwTenantLink>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<KwNode>().HasOne(node => node.Root).WithMany().HasForeignKey(node => node.RootId);
        model.Entity<KwNode>().HasOne(node => node.It).WithMany().HasForeignKey(node => node.ItId);
        model.Entity<KwNode>().HasOne(node => node.Parent).WithMany().HasForeignKey(node => node.ParentId);
        model.Entity<KwLeaf>().HasOne(leaf => leaf.Root).WithMany().HasForeignKey(leaf => leaf.RootId);
    }
}

/// <summary>
/// Members whose names System.Linq.Dynamic.Core reads as its context keywords.
/// </summary>
/// <remarks>
/// The parser treats <c>it</c>, <c>root</c> and <c>parent</c> as keywords, whatever their case, and
/// the library hands it member paths as written. A navigation named <c>Root</c> or <c>It</c> was read
/// as the row itself, so <c>Root.Name</c> filtered, sorted, grouped and projected the row's own
/// <c>Name</c>. A navigation named <c>Parent</c> threw. Under a policy that is a bypass: the gate
/// decides on the path the caller named, and the query reads a different column.
/// <para>
/// The data is chosen so the wrong column gives a different answer: node 2's own name is the name
/// of node 1's link.
/// </para>
/// </remarks>
public sealed class KeywordNamedMemberTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly KeywordContext _db;

    public KeywordNamedMemberTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new KeywordContext(_connection);
        _db.Database.EnsureCreated();

        KwLink a = new() { Id = 1, Name = "A-LINK" };
        KwLink b = new() { Id = 2, Name = "B-LINK" };
        KwLink c = new() { Id = 3, Name = "C-LINK" };

        _db.Links.AddRange(a, b, c);

        _db.Nodes.AddRange(
            new KwNode
            {
                Id = 1, Name = "C-NODE", TenantId = 5, Root = a, It = a, Parent = a,
                Leaves = { new KwLeaf { Id = 1, Root = c }, new KwLeaf { Id = 2, Root = b } }
            },
            new KwNode
            {
                Id = 2, Name = "A-LINK", TenantId = 9, Root = b, It = b, Parent = b,
                Leaves = { new KwLeaf { Id = 3, Root = a } }
            },
            new KwNode { Id = 3, Name = "B-NODE", TenantId = 5 });

        _db.GuardedNodes.AddRange(
            new KwGuardedNode { Id = 1, Name = "TOP-SECRET", Root = a },
            new KwGuardedNode { Id = 2, Name = "OTHER-SECRET", Root = b });

        KwTenantLink five = new() { Id = 1, TenantId = 5 };
        KwTenantLink nine = new() { Id = 2, TenantId = 9 };

        _db.ScopedNodes.AddRange(
            new KwScopedNode { Id = 1, TenantId = 5, Root = nine },
            new KwScopedNode { Id = 2, TenantId = 9, Root = five });

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    public static IEnumerable<object[]> Navigations() =>
        new[] { new object[] { "Root" }, new object[] { "It" }, new object[] { "Parent" } };

    private static ConditionGroup On(string field, DataType type, Operator op, params object[] values)
    {
        Condition condition = new() { Field = field, DataType = type, Operator = op };

        condition.Values.AddRange(values);

        return new ConditionGroup { Conditions = { condition } };
    }

    /// <summary>A member of a dynamic row, which is a generated class or a dictionary.</summary>
    private static object? Member(object? row, string name) => row switch
    {
        null => null,
        IDictionary<string, object?> columns => columns.TryGetValue(name, out object? value) ? value : null,
        _ => row.GetType().GetProperty(name)?.GetValue(row)
    };

    private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwPolicyContext? caller = null) where T : class =>
        source.ApplyPolicy(
            caller ?? new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Convenience, Caps = { MinGroupSize = 1 } },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    // ---- where -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_condition_through_the_navigation_reads_the_navigation(string navigation)
    {
        List<KwNode> rows = _db.Nodes.AsNoTracking()
            .Where(On($"{navigation}.Name", DataType.Text, Operator.Equal, "A-LINK"))
            .ToList();

        Assert.Equal(new[] { 1 }, rows.Select(node => node.Id));
    }

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_single_condition_through_the_navigation_reads_the_navigation(string navigation)
    {
        Condition condition = new() { Field = $"{navigation}.Name", DataType = DataType.Text, Operator = Operator.Equal };

        condition.Values.Add("A-LINK");

        List<KwNode> rows = _db.Nodes.AsNoTracking().Where(condition).ToList();

        Assert.Equal(new[] { 1 }, rows.Select(node => node.Id));
    }

    // ---- order -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_single_order_through_the_navigation_sorts_by_the_navigation(string navigation)
    {
        List<KwNode> rows = _db.Nodes.AsNoTracking()
            .Order(new OrderBy { Field = $"{navigation}.Name", Direction = Direction.Descending })
            .ToList();

        Assert.Equal(new[] { 2, 1, 3 }, rows.Select(node => node.Id));
    }

    [Theory]
    [MemberData(nameof(Navigations))]
    public void An_order_through_the_navigation_sorts_by_the_navigation(string navigation)
    {
        // Ascending by link name: node 3 has none, then A-LINK (node 1), then B-LINK (node 2).
        List<KwNode> rows = _db.Nodes.AsNoTracking()
            .Order(new List<OrderBy> { new() { Field = $"{navigation}.Name", Direction = Direction.Ascending } })
            .ToList();

        Assert.Equal(new[] { 3, 1, 2 }, rows.Select(node => node.Id));
    }

    [Fact]
    public void An_order_through_a_collection_reads_the_element_navigation_named_Root()
    {
        // The smallest link name among each node's leaves: node 2 A-LINK, node 1 B-LINK, node 3 none.
        List<KwNode> rows = _db.Nodes.AsNoTracking()
            .Order(new List<OrderBy> { new() { Field = "Leaves.Root.Name", Direction = Direction.Ascending } })
            .ToList();

        Assert.Equal(new[] { 3, 2, 1 }, rows.Select(node => node.Id));
    }

    // ---- select ----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_typed_projection_through_the_navigation_carries_the_navigation(string navigation)
    {
        List<KwNode> rows = _db.Nodes.AsNoTracking()
            .Select(new List<string> { "Id", $"{navigation}.Name" })
            .ToList();

        Dictionary<int, string?> names = rows.ToDictionary(
            node => node.Id,
            node => navigation switch { "Root" => node.Root?.Name, "It" => node.It?.Name, _ => node.Parent?.Name });

        Assert.Equal("A-LINK", names[1]);
        Assert.Equal("B-LINK", names[2]);
    }

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_dynamic_projection_through_the_navigation_carries_the_navigation(string navigation)
    {
        FilterResult<dynamic> result = _db.Nodes.AsNoTracking().ToListDynamic(new Filter
        {
            Selects = new List<string> { "Id", $"{navigation}.Name" },
            Orders = new List<OrderBy> { new() { Field = "Id" } }
        });

        List<string?> names = result.Data!
            .Select(row => (string?)Member(Member((object)row, navigation), "Name"))
            .ToList();

        Assert.Equal(new[] { "A-LINK", "B-LINK", null }, names);
    }

    // ---- group, aggregate, having ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Navigations))]
    public void A_group_on_the_navigation_groups_by_the_navigation(string navigation)
    {
        SummaryResult result = _db.Nodes.AsNoTracking().ToList(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { $"{navigation}.Name" },
                AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = "Total" } }
            }
        });

        List<string?> keys = result.Data!
            .Select(row => (string?)Member((object)row, $"{navigation}Name"))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { null, "A-LINK", "B-LINK" }, keys);
    }

    [Theory]
    [MemberData(nameof(Navigations))]
    public void An_aggregate_over_the_navigation_reads_the_navigation(string navigation)
    {
        SummaryResult result = _db.Nodes.AsNoTracking().ToList(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "TenantId" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = $"{navigation}.Name", Aggregator = Aggregator.Maximum, Alias = "Latest" }
                }
            },
            Orders = new List<OrderBy> { new() { Field = "TenantId" } }
        });

        List<string?> latest = result.Data!
            .Select(row => (string?)Member((object)row, "Latest"))
            .ToList();

        // Tenant 5 is node 1 (A-LINK) and node 3 (none); tenant 9 is node 2 (B-LINK).
        Assert.Equal(new[] { "A-LINK", "B-LINK" }, latest);
    }

    [Theory]
    [MemberData(nameof(Navigations))]
    public void The_first_and_last_aggregates_over_the_navigation_read_the_navigation(string navigation)
    {
        // Ordered with the current element, which the library writes as $ now that "it" names a member.
        SummaryResult result = _db.Nodes.AsNoTracking().ToList(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "TenantId" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = $"{navigation}.Name", Aggregator = Aggregator.FirstOrDefault, Alias = "First" },
                    new() { Field = $"{navigation}.Name", Aggregator = Aggregator.LastOrDefault, Alias = "Last" }
                }
            },
            Orders = new List<OrderBy> { new() { Field = "TenantId" } }
        });

        // Tenant 5 holds A-LINK and a node with no link; tenant 9 holds B-LINK.
        Assert.Equal("A-LINK", (string?)Member((object)result.Data![0], "Last"));
        Assert.Equal(new[] { "B-LINK", "B-LINK" },
            new[] { (string?)Member((object)result.Data![1], "First"), (string?)Member((object)result.Data![1], "Last") });
    }

    private static Summary CountedAs(string alias) => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { "TenantId" },
            AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = alias } }
        },
        Having = On(alias, DataType.Number, Operator.GreaterThan, 1),
        Orders = new List<OrderBy> { new() { Field = alias, Direction = Direction.Descending } }
    };

    [Theory]
    [InlineData("root")]
    [InlineData("it")]
    [InlineData("parent")]
    public async Task An_alias_named_like_a_keyword_works_on_every_summary_entry_point(string alias)
    {
        List<object> composed = _db.Nodes.AsNoTracking().Summary(CountedAs(alias)).ToDynamicList().Cast<object>().ToList();
        SummaryResult awaited = await _db.Nodes.AsNoTracking().ToListAsync(CountedAs(alias));

        Assert.Equal(5, (int)Member(composed.Single(), "TenantId")!);
        Assert.Equal(2, Convert.ToInt32(Member(composed.Single(), alias)));
        Assert.Equal(5, (int)Member((object)awaited.Data!.Single(), "TenantId")!);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("it")]
    [InlineData("parent")]
    public void A_guarded_composable_summary_projects_an_alias_named_like_a_keyword(string alias)
    {
        // With a floor the library adds a group-size column and projects it back out by name, which is
        // where an alias called root was read as the row.
        PolicyQueryable<KwNode> guarded = _db.Nodes.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Convenience, Caps = { MinGroupSize = 2 } },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        List<object> rows = guarded.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "TenantId" },
                AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = alias } }
            }
        }).ToDynamicList().Cast<object>().ToList();

        // Tenant 9's single node is under the floor; tenant 5 keeps both.
        Assert.Equal(2, Convert.ToInt32(Member(rows.Single(), alias)));
        Assert.Equal(5, (int)Member(rows.Single(), "TenantId")!);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("it")]
    [InlineData("parent")]
    public void An_alias_named_like_a_keyword_can_be_filtered_and_ordered(string alias)
    {
        SummaryResult result = _db.Nodes.AsNoTracking().ToList(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "TenantId" },
                AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = alias } }
            },
            Having = On(alias, DataType.Number, Operator.GreaterThan, 1),
            Orders = new List<OrderBy> { new() { Field = alias, Direction = Direction.Descending } }
        });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(5, (int)Member((object)result.Data![0], "TenantId")!);
    }

    // ---- the policy bypass -----------------------------------------------------------------------

    [Fact]
    public void A_guarded_filter_on_the_navigation_cannot_reach_the_denied_column()
    {
        // The row's own Name is denied for every feature, and asking for it directly is refused.
        Assert.Throws<PolicyException>(() => Guard(_db.GuardedNodes)
            .ToList(new Filter { ConditionGroup = On("Name", DataType.Text, Operator.Equal, "TOP-SECRET") }));

        // No link is called TOP-SECRET, so this matches nothing. Read as the row's own Name, it matched
        // the row whose denied value it is, and told the caller so.
        FilterResult<KwGuardedNode> result = Guard(_db.GuardedNodes)
            .ToList(new Filter { ConditionGroup = On("Root.Name", DataType.Text, Operator.Equal, "TOP-SECRET") });

        Assert.Empty(result.Data!);
    }

    [Fact]
    public void A_guarded_projection_through_the_navigation_does_not_return_the_denied_column()
    {
        FilterResult<dynamic> result = Guard(_db.GuardedNodes).ToListDynamic(new Filter
        {
            Selects = new List<string> { "Id", "Root.Name" },
            Orders = new List<OrderBy> { new() { Field = "Id" } }
        });

        List<string?> names = result.Data!
            .Select(row => (string?)Member(Member((object)row, "Root"), "Name"))
            .ToList();

        Assert.Equal(new[] { "A-LINK", "B-LINK" }, names);
        Assert.DoesNotContain("TOP-SECRET", names);
    }

    [Fact]
    public void A_scope_forced_through_the_navigation_scopes_the_navigation()
    {
        // The link carries the tenant scope, so a tenant-5 caller sees the node whose link is tenant 5's
        // (node 2). Read as the row's own TenantId, the scope admitted node 1, whose link is tenant 9's.
        DwPolicyContext caller = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", 5);

        FilterResult<KwScopedNode> result = Guard(_db.ScopedNodes, caller).ToList(new Filter());

        Assert.Equal(new[] { 2 }, result.Data!.Select(node => node.Id));
    }
}
