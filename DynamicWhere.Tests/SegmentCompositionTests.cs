using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

public class ComposedWriter
{
    public int Id { get; set; }

    public string? Nick { get; set; }

    public int? Score { get; set; }

    public List<ComposedBook> Books { get; set; } = new();

    public ComposedAddress Address { get; set; } = new();
}

[Owned]
public class ComposedAddress
{
    public string? City { get; set; }
}

public class ComposedBook
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int ComposedWriterId { get; set; }
}

/// <summary>Keyed on two columns.</summary>
public class ComposedPair
{
    public int A { get; set; }

    public int B { get; set; }

    public string? Label { get; set; }
}

/// <summary>A struct key with no <c>==</c>, the usual shape of a strongly typed identifier.</summary>
public readonly struct ComposedPlainKey
{
    public ComposedPlainKey(int value) => Value = value;

    public int Value { get; }
}

/// <summary>A record struct key, which does define <c>==</c>.</summary>
public readonly record struct ComposedRecordKey(int Value);

public class ComposedPlain
{
    public ComposedPlainKey Id { get; set; }

    public string? Name { get; set; }
}

public class ComposedRecord
{
    public ComposedRecordKey Id { get; set; }

    public string? Name { get; set; }
}

/// <summary>Mapped with no key, over a table that holds a duplicate row.</summary>
public class ComposedRow
{
    public int Code { get; set; }

    public string? Label { get; set; }
}

public class ComposedAnimal
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public class ComposedDog : ComposedAnimal
{
    public string? Breed { get; set; }
}

public sealed class CompositionContext : DbContext
{
    private readonly SqliteConnection _connection;

    public CompositionContext(SqliteConnection connection) => _connection = connection;

    public DbSet<ComposedWriter> Writers => Set<ComposedWriter>();

    public DbSet<ComposedPair> Pairs => Set<ComposedPair>();

    public DbSet<ComposedPlain> Plains => Set<ComposedPlain>();

    public DbSet<ComposedRecord> Records => Set<ComposedRecord>();

    public DbSet<ComposedRow> Rows => Set<ComposedRow>();

    public DbSet<ComposedAnimal> Animals => Set<ComposedAnimal>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ComposedPair>().HasKey(pair => new { pair.A, pair.B });
        model.Entity<ComposedPlain>().Property(plain => plain.Id)
            .HasConversion(key => key.Value, value => new ComposedPlainKey(value)).ValueGeneratedNever();
        model.Entity<ComposedRecord>().Property(record => record.Id)
            .HasConversion(key => key.Value, value => new ComposedRecordKey(value)).ValueGeneratedNever();
        model.Entity<ComposedRow>().HasNoKey().ToView("ComposedRows");
        model.Entity<ComposedDog>();
    }
}

/// <summary>
/// How condition sets combine once the database answers them, across the shapes a key can take.
/// </summary>
/// <remarks>
/// The oracle is the definition of a segment: load each set's keys with a plain filter, then combine
/// the key lists with LINQ, which compares keys by value. Every case below is a segment whose answer
/// has to agree with it.
/// <para>
/// Nullable members carry most of the weight. A row whose member is null is outside every set on that
/// member, negated operators included, and it has to stay outside when the sets combine: kept by an
/// Except of such a set, and admitted by neither side of a Union or an Intersect.
/// </para>
/// </remarks>
public sealed class SegmentCompositionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CompositionContext _db;

    public SegmentCompositionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new CompositionContext(_connection);
        _db.Database.EnsureCreated();

        string?[] nicks = { "ann", "bob", null, "nan", "Anna", null, "zed", "" };
        int?[] scores = { 1, 5, null, 7, 3, null, 9, 5 };
        string?[] cities = { "Odd", "Even", null, "Even", "Odd", "Even", null, "Odd" };

        for (int id = 1; id <= 8; id++)
        {
            ComposedWriter writer = new()
            {
                Id = id,
                Nick = nicks[id - 1],
                Score = scores[id - 1],
                Address = new ComposedAddress { City = cities[id - 1] }
            };

            for (int book = 1; book <= id % 4; book++)
            {
                writer.Books.Add(new ComposedBook { Title = book == 2 && id % 2 == 1 ? null : $"T{id}-{book}" });
            }

            _db.Writers.Add(writer);
        }

        _db.Pairs.AddRange(
            new ComposedPair { A = 1, B = 1, Label = "x" },
            new ComposedPair { A = 1, B = 2, Label = null },
            new ComposedPair { A = 2, B = 1, Label = "y" },
            new ComposedPair { A = 2, B = 2, Label = "xx" });

        _db.Plains.AddRange(
            new ComposedPlain { Id = new ComposedPlainKey(1), Name = "a" },
            new ComposedPlain { Id = new ComposedPlainKey(2), Name = "a" },
            new ComposedPlain { Id = new ComposedPlainKey(3), Name = "b" },
            new ComposedPlain { Id = new ComposedPlainKey(4), Name = null });

        _db.Records.AddRange(
            new ComposedRecord { Id = new ComposedRecordKey(1), Name = "a" },
            new ComposedRecord { Id = new ComposedRecordKey(2), Name = "a" },
            new ComposedRecord { Id = new ComposedRecordKey(3), Name = "b" },
            new ComposedRecord { Id = new ComposedRecordKey(4), Name = null });

        _db.Animals.AddRange(
            new ComposedAnimal { Id = 1, Name = "Tom" },
            new ComposedDog { Id = 2, Name = "Rex", Breed = "lab" },
            new ComposedDog { Id = 3, Name = "Ace", Breed = null });

        _db.SaveChanges();

        _db.Database.ExecuteSqlRaw("CREATE TABLE ComposedRows (Code INTEGER NOT NULL, Label TEXT NULL)");
        _db.Database.ExecuteSqlRaw("INSERT INTO ComposedRows VALUES (1, 'x'), (1, 'x'), (2, 'y'), (3, NULL)");

        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static readonly Intersection[] Operations = { Intersection.Union, Intersection.Intersect, Intersection.Except };

    private static ConditionGroup On(string field, DataType type, Operator op, params object[] values)
    {
        Condition condition = new() { Field = field, DataType = type, Operator = op };

        condition.Values.AddRange(values);

        return new ConditionGroup { Conditions = { condition } };
    }

    private static Segment Combine(params (Intersection? Operation, ConditionGroup Group)[] sets) => new()
    {
        ConditionSets = sets
            .Select((set, index) => new ConditionSet { Sort = index + 1, Intersection = set.Operation, ConditionGroup = set.Group })
            .ToList()
    };

    private static List<TKey> Oracle<TKey>(params (Intersection? Operation, List<TKey> Keys)[] sets)
    {
        List<TKey> combined = sets[0].Keys;

        foreach ((Intersection? operation, List<TKey> keys) in sets.Skip(1))
        {
            combined = operation switch
            {
                Intersection.Union => combined.Union(keys).ToList(),
                Intersection.Intersect => combined.Intersect(keys).ToList(),
                _ => combined.Except(keys).ToList()
            };
        }

        return combined;
    }

    private static List<(string Name, ConditionGroup Group)> WriterGroups() => new()
    {
        ("Nick = ann", On("Nick", DataType.Text, Operator.Equal, "ann")),
        ("Nick != ann", On("Nick", DataType.Text, Operator.NotEqual, "ann")),
        ("Nick contains n", On("Nick", DataType.Text, Operator.Contains, "n")),
        ("Nick not contains n", On("Nick", DataType.Text, Operator.NotContains, "n")),
        ("Nick not icontains N", On("Nick", DataType.Text, Operator.INotContains, "N")),
        ("Nick starts with a", On("Nick", DataType.Text, Operator.StartsWith, "a")),
        ("Nick not in ann,bob", On("Nick", DataType.Text, Operator.NotIn, "ann", "bob")),
        ("Nick is null", On("Nick", DataType.Text, Operator.IsNull)),
        ("Nick is not null", On("Nick", DataType.Text, Operator.IsNotNull)),
        ("Score > 5", On("Score", DataType.Number, Operator.GreaterThan, 5)),
        ("Score != 5", On("Score", DataType.Number, Operator.NotEqual, 5)),
        ("Score not between 3,7", On("Score", DataType.Number, Operator.NotBetween, 3, 7)),
        ("Score is null", On("Score", DataType.Number, Operator.IsNull)),
        ("Books.Title = T3-1", On("Books.Title", DataType.Text, Operator.Equal, "T3-1")),
        ("Books.Title != T3-1", On("Books.Title", DataType.Text, Operator.NotEqual, "T3-1")),
        ("Address.City = Even", On("Address.City", DataType.Text, Operator.Equal, "Even")),
        ("Address.City != Even", On("Address.City", DataType.Text, Operator.NotEqual, "Even")),
        ("no conditions", new ConditionGroup())
    };

    [Fact]
    public async Task Every_pair_of_nullable_and_negated_conditions_combines_as_the_key_lists_do()
    {
        List<(string Name, ConditionGroup Group)> groups = WriterGroups();
        List<List<int>> members = groups.Select(g => _db.Writers.Where(g.Group).Select(w => w.Id).ToList()).ToList();
        List<string> wrong = new();

        for (int first = 0; first < groups.Count; first++)
        {
            for (int second = 0; second < groups.Count; second++)
            {
                foreach (Intersection operation in Operations)
                {
                    List<int> expected = Oracle<int>((null, members[first]), (operation, members[second]))
                        .OrderBy(id => id).ToList();

                    SegmentResult<ComposedWriter> result = await _db.Writers.AsNoTracking()
                        .ToListAsync(Combine((null, groups[first].Group), (operation, groups[second].Group)));

                    List<int> actual = result.Data!.Select(w => w.Id).OrderBy(id => id).ToList();

                    if (!expected.SequenceEqual(actual) || result.TotalCount != expected.Count)
                    {
                        wrong.Add($"[{groups[first].Name}] {operation} [{groups[second].Name}]: " +
                                  $"expected {string.Join(",", expected)}, got {string.Join(",", actual)} ({result.TotalCount})");
                    }
                }
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task Chains_of_three_sets_combine_left_to_right()
    {
        List<(string Name, ConditionGroup Group)> groups = WriterGroups();
        List<List<int>> members = groups.Select(g => _db.Writers.Where(g.Group).Select(w => w.Id).ToList()).ToList();
        Random random = new(20260917);
        List<string> wrong = new();

        for (int round = 0; round < 150; round++)
        {
            int first = random.Next(groups.Count), second = random.Next(groups.Count), third = random.Next(groups.Count);
            Intersection middle = Operations[random.Next(3)], last = Operations[random.Next(3)];

            List<int> expected = Oracle<int>((null, members[first]), (middle, members[second]), (last, members[third]))
                .OrderBy(id => id).ToList();

            SegmentResult<ComposedWriter> result = await _db.Writers.AsNoTracking().ToListAsync(
                Combine((null, groups[first].Group), (middle, groups[second].Group), (last, groups[third].Group)));

            List<int> actual = result.Data!.Select(w => w.Id).OrderBy(id => id).ToList();

            if (!expected.SequenceEqual(actual))
            {
                wrong.Add($"(([{groups[first].Name}] {middle} [{groups[second].Name}]) {last} [{groups[third].Name}]): " +
                          $"expected {string.Join(",", expected)}, got {string.Join(",", actual)}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task Except_keeps_the_rows_the_excluded_set_left_out_because_of_a_null()
    {
        // "Nick != ann" matches bob, nan, Anna, zed and the empty nick, and never a null nick, as no
        // negated operator matches null. Removing that set from every writer leaves ann and the two
        // null nicks.
        SegmentResult<ComposedWriter> result = await _db.Writers.AsNoTracking().ToListAsync(Combine(
            (null, new ConditionGroup()),
            (Intersection.Except, On("Nick", DataType.Text, Operator.NotEqual, "ann"))));

        Assert.Equal(new[] { 1, 3, 6 }, result.Data!.Select(w => w.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task A_composite_key_matches_on_every_column()
    {
        List<(string Name, ConditionGroup Group)> groups = new()
        {
            ("Label = x", On("Label", DataType.Text, Operator.Equal, "x")),
            ("Label != x", On("Label", DataType.Text, Operator.NotEqual, "x")),
            ("Label is null", On("Label", DataType.Text, Operator.IsNull)),
            ("A = 1", On("A", DataType.Number, Operator.Equal, 1)),
            ("B = 1", On("B", DataType.Number, Operator.Equal, 1))
        };

        List<List<(int, int)>> members = groups
            .Select(g => _db.Pairs.Where(g.Group).AsEnumerable().Select(p => (p.A, p.B)).ToList())
            .ToList();

        List<string> wrong = new();

        for (int first = 0; first < groups.Count; first++)
        {
            for (int second = 0; second < groups.Count; second++)
            {
                foreach (Intersection operation in Operations)
                {
                    List<(int, int)> expected = Oracle<(int, int)>((null, members[first]), (operation, members[second]))
                        .OrderBy(key => key).ToList();

                    SegmentResult<ComposedPair> result = await _db.Pairs.AsNoTracking()
                        .ToListAsync(Combine((null, groups[first].Group), (operation, groups[second].Group)));

                    List<(int, int)> actual = result.Data!.Select(p => (p.A, p.B)).OrderBy(key => key).ToList();

                    if (!expected.SequenceEqual(actual))
                    {
                        wrong.Add($"[{groups[first].Name}] {operation} [{groups[second].Name}]");
                    }
                }
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData(Intersection.Union, new[] { 1, 2, 3 })]
    [InlineData(Intersection.Intersect, new[] { 1, 2 })]
    [InlineData(Intersection.Except, new[] { 3 })]
    public async Task A_struct_key_with_no_equality_operator_still_matches(Intersection operation, int[] expected)
    {
        Segment segment = Combine(
            (null, On("Name", DataType.Text, Operator.In, "a", "b")),
            (operation, On("Name", DataType.Text, Operator.Equal, "a")));

        SegmentResult<ComposedPlain> plain = await _db.Plains.AsNoTracking().ToListAsync(segment);
        SegmentResult<ComposedRecord> record = await _db.Records.AsNoTracking().ToListAsync(Combine(
            (null, On("Name", DataType.Text, Operator.In, "a", "b")),
            (operation, On("Name", DataType.Text, Operator.Equal, "a"))));

        Assert.Equal(expected, plain.Data!.Select(p => p.Id.Value).OrderBy(id => id));
        Assert.Equal(expected, record.Data!.Select(r => r.Id.Value).OrderBy(id => id));
    }

    [Theory]
    [InlineData(Intersection.Union, 2)]
    [InlineData(Intersection.Intersect, 1)]
    [InlineData(Intersection.Except, 1)]
    public async Task A_type_with_no_key_combines_whole_rows(Intersection operation, int expected)
    {
        // The database's own set operators, which compare every column and return distinct rows: the
        // duplicate (1, x) counts once.
        SegmentResult<ComposedRow> result = await _db.Rows.AsNoTracking().ToListAsync(Combine(
            (null, On("Code", DataType.Number, Operator.LessThanOrEqual, 2)),
            (operation, On("Label", DataType.Text, Operator.Equal, "x"))));

        Assert.Equal(expected, result.TotalCount);
        Assert.Equal(expected, result.Data!.Count);
    }

    [Theory]
    [InlineData(Intersection.Union, new[] { 2, 3 })]
    [InlineData(Intersection.Intersect, new int[0])]
    [InlineData(Intersection.Except, new[] { 3 })]
    public async Task A_derived_type_is_matched_on_the_key_it_inherits(Intersection operation, int[] expected)
    {
        SegmentResult<ComposedDog> result = await _db.Animals.AsNoTracking().OfType<ComposedDog>().ToListAsync(Combine(
            (null, On("Breed", DataType.Text, Operator.IsNull)),
            (operation, On("Name", DataType.Text, Operator.Equal, "Rex"))));

        Assert.Equal(expected, result.Data!.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public void A_keyed_type_is_combined_by_condition_and_key_and_a_keyless_one_by_set_operators()
    {
        // Every path gives the same rows on SQLite, so only the SQL tells them apart. Whole-row set
        // operators fail on a column the database cannot compare, such as PostgreSQL's json, which is
        // why a keyed type must never take that path; and Except has to exclude through EXISTS on the
        // key, which is right whatever the excluded condition evaluates to.
        List<ConditionSet> sets = Combine(
            (null, On("Nick", DataType.Text, Operator.IsNotNull)),
            (Intersection.Union, On("Score", DataType.Number, Operator.GreaterThan, 5)),
            (Intersection.Except, On("Address.City", DataType.Text, Operator.Equal, "Even"))).ConditionSets;

        string keyed = SegmentComposer.Compose(_db.Writers.AsNoTracking(), sets).ToQueryString();

        Assert.Contains("NOT EXISTS", keyed.Replace("NOT (EXISTS", "NOT EXISTS"), StringComparison.Ordinal);
        Assert.DoesNotContain("UNION", keyed, StringComparison.Ordinal);
        Assert.DoesNotContain("EXCEPT", keyed, StringComparison.Ordinal);

        List<ConditionSet> rowSets = Combine(
            (null, On("Code", DataType.Number, Operator.LessThanOrEqual, 2)),
            (Intersection.Except, On("Label", DataType.Text, Operator.Equal, "x"))).ConditionSets;

        string keyless = SegmentComposer.Compose(_db.Rows.AsNoTracking(), rowSets).ToQueryString();

        Assert.Contains("EXCEPT", keyless, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_combined_rows_are_ordered_paged_and_projected_in_the_database()
    {
        // Ordered by a collection path and projected through an owned type, neither of which the
        // projection carries.
        SegmentResult<ComposedWriter> result = await _db.Writers.AsNoTracking().ToListAsync(new Segment
        {
            ConditionSets = Combine(
                (null, On("Address.City", DataType.Text, Operator.Equal, "Even")),
                (Intersection.Union, On("Score", DataType.Number, Operator.GreaterThan, 5))).ConditionSets,
            Selects = new List<string> { "Id", "Address.City" },
            Orders = new List<OrderBy> { new() { Field = "Id", Direction = Direction.Descending } },
            Page = new PageBy { PageNumber = 2, PageSize = 2 }
        });

        // Even: 2, 4, 6. Score above 5: 4, 7. Together 2, 4, 6, 7; the second page of two, descending.
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(new[] { 4, 2 }, result.Data!.Select(w => w.Id));
        Assert.All(result.Data!, w => Assert.Equal("Even", w.Address.City));
        Assert.All(result.Data!, w => Assert.Null(w.Nick));
    }
}
