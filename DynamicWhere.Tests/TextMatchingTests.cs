using System.Data.Common;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace DynamicWhere.Tests;

public class TmItem
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Code { get; set; }

    public List<TmTag> Tags { get; set; } = new();
}

public class TmTag
{
    public int Id { get; set; }

    public string? Label { get; set; }

    public int TmItemId { get; set; }
}

/// <summary>A row projected from <see cref="TmItem"/>, which the model does not map and so has no key.</summary>
public class TmRow
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public sealed class TmContext : DbContext
{
    public TmContext(DbContextOptions<TmContext> options) : base(options)
    {
    }

    public DbSet<TmItem> Items => Set<TmItem>();
}

/// <summary>
/// <c>DwText</c> and <see cref="TextMatching.ILike"/>: the six case-insensitive pattern operators compiled to
/// PostgreSQL's <c>ILIKE</c> on request, and nothing else changed (issue #11).
/// </summary>
/// <remarks>
/// The rewrite is exercised inside <c>DwText.Use</c>, never through <c>DwText.Configure</c>, because that
/// choice is process-wide and made once: a test that chose <c>ILike</c> for itself would choose it for every
/// SQLite query in the run. The SQL is read with <c>ToQueryString</c> against Npgsql, which needs no server.
/// </remarks>
public class TextMatchingTests
{
    private static readonly Operator[] Patterns =
    {
        Operator.IContains, Operator.INotContains, Operator.IStartsWith,
        Operator.INotStartsWith, Operator.IEndsWith, Operator.INotEndsWith
    };

    private static DwTextOptions ILike() => new() { CaseInsensitive = TextMatching.ILike };

    private static TmContext Npgsql() =>
        new(new DbContextOptionsBuilder<TmContext>().UseNpgsql("Host=localhost;Database=dw_text_shape").Options);

    private static Condition On(string field, Operator op, params string[] values)
    {
        Condition condition = new() { Field = field, DataType = DataType.Text, Operator = op };

        condition.Values.AddRange(values);

        return condition;
    }

    private static string Sql(IQueryable query) => query.ToQueryString().Replace("\n", " ").Replace("\r", " ");

    private static string Sql(Condition condition, bool iLike)
    {
        using TmContext db = Npgsql();

        if (!iLike)
        {
            return Sql(db.Items.Where(condition));
        }

        using (DwText.Use(ILike()))
        {
            return Sql(db.Items.Where(condition));
        }
    }

    // ---- options and configuration ------------------------------------------------------------------

    [Fact]
    public void Nothing_configured_means_lowering_both_sides()
    {
        Assert.Equal(TextMatching.Lower, DwText.Options.CaseInsensitive);
        Assert.True(DwText.Options.IsFrozen);
        Assert.Null(DwText.Options.ILike);
        Assert.Equal(TextMatching.Lower, new DwTextOptions().CaseInsensitive);
    }

    [Fact]
    public void Choosing_ilike_resolves_npgsqls_function_when_frozen()
    {
        DwTextOptions options = ILike();

        options.Freeze();

        Assert.NotNull(options.ILike);
        Assert.Equal("ILike", options.ILike!.Name);
        Assert.Equal(4, options.ILike.GetParameters().Length);
        Assert.Equal("Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions", options.ILike.DeclaringType!.FullName);
    }

    [Fact]
    public void Options_cannot_change_once_frozen_and_an_undefined_choice_is_refused()
    {
        DwTextOptions options = ILike();

        options.Freeze();

        Assert.True(options.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => options.CaseInsensitive = TextMatching.Lower);
        Assert.Throws<ArgumentException>(() => new DwTextOptions { CaseInsensitive = (TextMatching)7 }.Freeze());
    }

    [Fact]
    public void Options_bind_from_configuration_and_refuse_what_they_do_not_know()
    {
        static IConfiguration Section(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("DynamicWhere:Text");

        Assert.Equal(
            TextMatching.ILike,
            new DwTextOptions().Bind(Section(new() { ["DynamicWhere:Text:CaseInsensitive"] = "ILike" })).CaseInsensitive);
        Assert.Equal(TextMatching.Lower, new DwTextOptions().Bind(Section(new())).CaseInsensitive);

        Assert.Throws<InvalidOperationException>(
            () => new DwTextOptions().Bind(Section(new() { ["DynamicWhere:Text:CaseInsensitiv"] = "ILike" })));
        Assert.Throws<InvalidOperationException>(
            () => new DwTextOptions().Bind(Section(new() { ["DynamicWhere:Text:CaseInsensitive"] = "ILikee" })));
        Assert.Throws<InvalidOperationException>(
            () => new DwTextOptions().Bind(Section(new() { ["DynamicWhere:Text"] = "ILike" })));

        DwTextOptions frozen = new();

        frozen.Freeze();

        Assert.Throws<InvalidOperationException>(() => frozen.Bind(Section(new())));
    }

    [Fact]
    public void Configure_repeats_harmlessly_and_refuses_a_different_choice()
    {
        // Lower is what an unconfigured process does anyway, so this changes nothing for any other test.
        DwText.Configure(new DwTextOptions());
        DwText.Configure(options => options.CaseInsensitive = TextMatching.Lower);

        Assert.True(DwText.IsConfigured);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => DwText.Configure(options => options.CaseInsensitive = TextMatching.ILike));

        Assert.Contains("already configured as Lower", refused.Message, StringComparison.Ordinal);
        Assert.Equal(TextMatching.Lower, DwText.Options.CaseInsensitive);
        Assert.Throws<ArgumentNullException>(() => DwText.Configure((DwTextOptions)null!));
        Assert.Throws<ArgumentNullException>(() => DwText.Configure((Action<DwTextOptions>)null!));
    }

    // ---- what the SQL says ---------------------------------------------------------------------------

    [Fact]
    public void The_six_pattern_operators_compile_to_ilike_with_the_value_escaped()
    {
        // The value carries every character a LIKE pattern gives meaning to, and a quote.
        const string value = "Ab%_\\c'd";

        Assert.Contains(""""i."Name" ILIKE '%ab\%\_\\c''d%' ESCAPE '\'"""", Sql(On("Name", Operator.IContains, value), iLike: true));
        Assert.Contains(""""NOT (i."Name" ILIKE '%ab\%\_\\c''d%' ESCAPE '\')"""", Sql(On("Name", Operator.INotContains, value), iLike: true));
        Assert.Contains(""""i."Name" ILIKE 'ab\%\_\\c''d%' ESCAPE '\'"""", Sql(On("Name", Operator.IStartsWith, value), iLike: true));
        Assert.Contains(""""NOT (i."Name" ILIKE 'ab\%\_\\c''d%' ESCAPE '\')"""", Sql(On("Name", Operator.INotStartsWith, value), iLike: true));
        Assert.Contains(""""i."Name" ILIKE '%ab\%\_\\c''d' ESCAPE '\'"""", Sql(On("Name", Operator.IEndsWith, value), iLike: true));
        Assert.Contains(""""NOT (i."Name" ILIKE '%ab\%\_\\c''d' ESCAPE '\')"""", Sql(On("Name", Operator.INotEndsWith, value), iLike: true));

        foreach (Operator op in Patterns)
        {
            string sql = Sql(On("Name", op, value), iLike: true);

            // The null guard stays in front of the match, and nothing is lowered any more.
            Assert.Contains(""""i."Name" IS NOT NULL AND """", sql);
            Assert.DoesNotContain("lower(", sql);
        }
    }

    [Fact]
    public void Lowering_is_unchanged_without_the_choice()
    {
        foreach (Operator op in Patterns)
        {
            string sql = Sql(On("Name", op, "Ab"), iLike: false);

            Assert.Contains(""""lower(i."Name")"""", sql);
            Assert.DoesNotContain("ILIKE", sql);
        }
    }

    [Fact]
    public void Equality_and_lists_stay_lowered()
    {
        foreach (Operator op in new[] { Operator.IEqual, Operator.INotEqual, Operator.IIn, Operator.INotIn })
        {
            string[] values = op is Operator.IIn or Operator.INotIn ? new[] { "Ab", "Cd" } : new[] { "Ab" };
            string sql = Sql(On("Name", op, values), iLike: true);

            Assert.Contains(""""lower(i."Name")"""", sql);
            Assert.DoesNotContain("ILIKE", sql);
        }

        // The case-sensitive operators are not this choice's business either.
        Assert.Contains(""""i."Name" LIKE '%Ab%'"""", Sql(On("Name", Operator.Contains, "Ab"), iLike: true));
    }

    [Fact]
    public void A_group_a_nested_collection_and_a_mixed_group_are_all_rewritten()
    {
        using TmContext db = Npgsql();
        using DwText.Scope scope = DwText.Use(ILike());

        ConditionGroup group = new()
        {
            Connector = Connector.Or,
            Conditions = new List<Condition>
            {
                On("Name", Operator.IContains, "ab"),
                On("Tags.Label", Operator.IStartsWith, "x"),
                On("Code", Operator.IEqual, "K-1")
            }
        };

        for (int i = 0; i < group.Conditions.Count; i++)
        {
            group.Conditions[i].Sort = i + 1;
        }

        string sql = Sql(db.Items.Where(group));

        Assert.Contains(""""i."Name" ILIKE '%ab%' ESCAPE '\'"""", sql);
        Assert.Contains(""""."Label" ILIKE 'x%' ESCAPE '\'"""", sql);
        Assert.Contains(""""lower(i."Code") = 'k-1'"""", sql);
    }

    [Fact]
    public void A_segment_rewrites_every_set_whether_it_combines_by_key_or_by_row()
    {
        using TmContext db = Npgsql();
        using DwText.Scope scope = DwText.Use(ILike());

        Condition first = On("Name", Operator.IContains, "ab");
        Condition second = On("Name", Operator.IEndsWith, "cd");

        first.Sort = 1;
        second.Sort = 1;

        List<ConditionSet> sets = new()
        {
            new() { Sort = 1, ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { first } } },
            new() { Sort = 2, Intersection = Intersection.Union, ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { second } } }
        };

        // Keyed: the sets' own predicates are lifted out of Where(ConditionGroup) and OR-ed on the server.
        string keyed = Sql(SegmentComposer.Compose(db.Items, sets));

        // Keyless: a projected row has no key, so each set stays its own query and the rows are unioned.
        string keyless = Sql(SegmentComposer.Compose(db.Items.Select(item => new TmRow { Id = item.Id, Name = item.Name }), sets));

        foreach (string sql in new[] { keyed, keyless })
        {
            Assert.Contains("ILIKE '%ab%' ESCAPE", sql);
            Assert.Contains("ILIKE '%cd' ESCAPE", sql);
            Assert.DoesNotContain("lower(", sql);
        }

        Assert.Contains("UNION", keyless);
        Assert.DoesNotContain("UNION", keyed);
    }

    [Fact]
    public void A_summary_rewrites_its_row_filter_and_leaves_having_lowered()
    {
        using TmContext db = Npgsql();
        using DwText.Scope scope = DwText.Use(ILike());

        Summary summary = new()
        {
            ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { On("Name", Operator.IContains, "ab") } },
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Code" },
                AggregateBy = new List<AggregateBy> { new() { Field = "Name", Alias = "Top", Aggregator = Aggregator.Maximum } }
            },
            Having = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { On("Top", Operator.IContains, "cd") } }
        };

        summary.ConditionGroup.Conditions[0].Sort = 1;
        summary.Having.Conditions[0].Sort = 1;

        string sql = Sql(db.Items.Summary(summary));

        Assert.Contains(""""i."Name" ILIKE '%ab%' ESCAPE '\'"""", sql);
        Assert.Contains("HAVING", sql);
        Assert.Contains("LIKE '%cd%'", sql);
        Assert.Single(sql.Split("ILIKE")[1..]);
    }

    [Fact]
    public void A_query_in_memory_keeps_lowering()
    {
        List<TmItem> items = new()
        {
            new() { Id = 1, Name = "Alpha" },
            new() { Id = 2, Name = "BETA" },
            new() { Id = 3, Name = null }
        };

        using DwText.Scope scope = DwText.Use(ILike());

        Assert.Equal(new[] { 2 }, items.AsQueryable().Where(On("Name", Operator.IContains, "et")).Select(item => item.Id).ToArray());
        Assert.Equal(new[] { 1 }, items.AsQueryable().Where(On("Name", Operator.INotStartsWith, "b")).Select(item => item.Id).ToArray());
    }

    [Fact]
    public void Every_ef_core_query_is_rewritten_so_another_database_fails_loudly()
    {
        // Documented: the choice is the process's. SQLite has no ILike, and says so rather than matching wrongly.
        using SqliteConnection connection = new("DataSource=:memory:");

        connection.Open();

        using TmContext db = new(new DbContextOptionsBuilder<TmContext>().UseSqlite(connection).Options);

        db.Database.EnsureCreated();

        using DwText.Scope scope = DwText.Use(ILike());

        Assert.Throws<InvalidOperationException>(() => db.Items.Where(On("Name", Operator.IContains, "a")).ToList());
    }

    [Fact]
    public void A_scope_ends_where_it_is_disposed()
    {
        Condition condition = On("Name", Operator.IContains, "ab");

        Assert.Contains("ILIKE", Sql(condition, iLike: true));
        Assert.DoesNotContain("ILIKE", Sql(condition, iLike: false));
    }

    [Fact]
    public void Escaping_leaves_ordinary_text_alone()
    {
        Assert.Equal("plain text", InsensitiveLike.Escape("plain text"));
        Assert.Equal("100\\% \\_x\\\\", InsensitiveLike.Escape("100% _x\\"));
    }
}

/// <summary>A PostgreSQL server with a trigram index on <c>Name</c>, seeded once for the class.</summary>
public sealed class TextMatchingPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _server = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>Every row, as seeded. Plain ASCII, so lowering means the same in .NET and in PostgreSQL.</summary>
    internal static readonly TmItem[] Seed =
    {
        new() { Id = 1, Name = "100% Pure", Code = "K-1" },
        new() { Id = 2, Name = "a_b", Code = "K-2" },
        new() { Id = 3, Name = "aXb", Code = "K-3" },
        new() { Id = 4, Name = "back\\slash", Code = "K-4" },
        new() { Id = 5, Name = "Quote's", Code = "K-5" },
        new() { Id = 6, Name = "MiXeD Case", Code = "K-6" },
        new() { Id = 7, Name = "percent%", Code = "K-7" },
        new() { Id = 8, Name = "plain", Code = "K-8" },
        new() { Id = 9, Name = null, Code = "K-9" },
        new() { Id = 10, Name = "UNDER_SCORE", Code = "K-10" }
    };

    public TmContext Create() =>
        new(new DbContextOptionsBuilder<TmContext>().UseNpgsql(_server.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await _server.StartAsync();

        using TmContext db = Create();

        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS pg_trgm; "
            + "CREATE INDEX ix_tm_items_name_trgm ON \"Items\" USING gin (\"Name\" gin_trgm_ops);");

        db.Items.AddRange(Seed.Select(item => new TmItem { Id = item.Id, Name = item.Name, Code = item.Code }));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _server.DisposeAsync().AsTask();
}

/// <summary>
/// <see cref="TextMatching.ILike"/> against a real PostgreSQL: the same rows as lowering for every pattern
/// operator and every character a pattern gives meaning to, through the plain and the guarded paths, and an
/// index lowering cannot use.
/// </summary>
public sealed class TextMatchingPostgresTests : IClassFixture<TextMatchingPostgresFixture>
{
    private static readonly Operator[] Patterns =
    {
        Operator.IContains, Operator.INotContains, Operator.IStartsWith,
        Operator.INotStartsWith, Operator.IEndsWith, Operator.INotEndsWith
    };

    private static readonly string[] Values = { "%", "_", "\\", "'", "100%", "a_b", "% p", "pure", "MIXED", "e", "slash", "x" };

    private readonly TextMatchingPostgresFixture _fixture;

    public TextMatchingPostgresTests(TextMatchingPostgresFixture fixture) => _fixture = fixture;

    private static DwTextOptions ILike() => new() { CaseInsensitive = TextMatching.ILike };

    private static Condition On(Operator op, string value) =>
        new() { Field = "Name", DataType = DataType.Text, Operator = op, Values = { value } };

    private static int[] Ids(IQueryable<TmItem> query) => query.Select(item => item.Id).OrderBy(id => id).ToArray();

    [Fact]
    public void Ilike_returns_what_lowering_returns_for_every_pattern_operator_and_value()
    {
        using TmContext db = _fixture.Create();

        foreach (Operator op in Patterns)
        {
            foreach (string value in Values)
            {
                Condition condition = On(op, value);

                int[] lowered = Ids(db.Items.Where(condition));
                int[] inMemory = Ids(TextMatchingPostgresFixture.Seed.AsQueryable().Where(condition));
                int[] iLiked;

                using (DwText.Use(ILike()))
                {
                    IQueryable<TmItem> query = db.Items.Where(condition);

                    Assert.Contains("ILIKE", query.ToQueryString());

                    iLiked = Ids(query);
                }

                Assert.True(lowered.SequenceEqual(iLiked), $"{op} '{value}': lowered [{string.Join(",", lowered)}], ILIKE [{string.Join(",", iLiked)}]");
                Assert.True(inMemory.SequenceEqual(iLiked), $"{op} '{value}': in memory [{string.Join(",", inMemory)}], ILIKE [{string.Join(",", iLiked)}]");
            }
        }
    }

    [Fact]
    public async Task A_guarded_filter_and_a_segment_match_by_ilike_too()
    {
        using TmContext db = _fixture.Create();
        using DwText.Scope scope = DwText.Use(ILike());

        // Convenience, because the strict tier refuses to hand back a query string.
        PolicyQueryable<TmItem> Guarded() => db.Items.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Convenience },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        Condition contains = On(Operator.IContains, "% p");

        contains.Sort = 1;

        FilterResult<TmItem> filtered = await Guarded().ToListAsync(
            new Filter { ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { contains } } },
            getQueryString: true);

        Assert.Contains("ILIKE", filtered.QueryString);
        Assert.Equal(new[] { 1 }, filtered.Data.Select(item => item.Id).ToArray());

        Condition endsWith = On(Operator.IEndsWith, "%");

        endsWith.Sort = 1;

        SegmentResult<TmItem> segment = await Guarded().ToListAsync(new Segment
        {
            ConditionSets = new List<ConditionSet>
            {
                new() { Sort = 1, ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { contains } } },
                new() { Sort = 2, Intersection = Intersection.Union, ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new List<Condition> { endsWith } } }
            }
        });

        Assert.Equal(new[] { 1, 7 }, segment.Data.Select(item => item.Id).OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Ilike_can_use_a_trigram_index_where_lowering_cannot()
    {
        using TmContext db = _fixture.Create();

        Condition condition = On(Operator.IContains, "pure");

        string lowered = db.Items.Where(condition).ToQueryString();
        string iLiked;

        using (DwText.Use(ILike()))
        {
            iLiked = db.Items.Where(condition).ToQueryString();
        }

        Assert.Contains("ix_tm_items_name_trgm", Plan(db, iLiked));
        Assert.DoesNotContain("ix_tm_items_name_trgm", Plan(db, lowered));
    }

    /// <summary>The plan PostgreSQL chooses when a sequential scan is the last resort.</summary>
    private static string Plan(TmContext db, string sql)
    {
        DbConnection connection = db.Database.GetDbConnection();

        connection.Open();

        try
        {
            using DbTransaction transaction = connection.BeginTransaction();
            using DbCommand off = connection.CreateCommand();

            off.Transaction = transaction;
            off.CommandText = "SET LOCAL enable_seqscan = off";
            off.ExecuteNonQuery();

            using DbCommand explain = connection.CreateCommand();

            explain.Transaction = transaction;
            explain.CommandText = "EXPLAIN " + sql;

            using DbDataReader reader = explain.ExecuteReader();

            List<string> lines = new();

            while (reader.Read())
            {
                lines.Add(reader.GetString(0));
            }

            return string.Join("\n", lines);
        }
        finally
        {
            connection.Close();
        }
    }
}
