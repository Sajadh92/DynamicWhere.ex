using System.Collections;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit.Abstractions;

// A converted column beside a top-level [DwDenied] (Secret), which builds a projection. An everyday converted column
// comes back with its value; one that can hold an object of any type is left out, since the converter is the
// application's code and the policy cannot see what it hands back.

namespace DynamicWhere.Tests.Policies
{
    public enum ZycStatus
    {
        Draft,
        Active
    }

    public readonly record struct ZycAccountId(int Value);

    public sealed record ZycAccountKey(Guid Value);

    public sealed record ZycMoney(decimal Amount, string Currency);

    public class ZycPrefs
    {
        public string Theme { get; set; } = string.Empty;

        public int FontSize { get; set; }

        public List<string> Recent { get; set; } = new();
    }

    /// <summary>A JSON envelope holding an arbitrary payload: genuinely able to hold any object.</summary>
    public class ZycEnvelope
    {
        public string Kind { get; set; } = string.Empty;

        public object? Data { get; set; }
    }

    public class ZycPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    public class ZycAccount
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>A top-level denial: every guarded query over the account builds a projection.</summary>
        [DwDenied]
        public string? Secret { get; set; }

        public ZycStatus Status { get; set; }

        public DateTime CreatedUtc { get; set; }

        public List<string> Tags { get; set; } = new();

        public string[] Aliases { get; set; } = Array.Empty<string>();

        public HashSet<int> Codes { get; set; } = new();

        public ZycAccountId Ref { get; set; }

        public ZycAccountKey Key { get; set; } = new(Guid.Empty);

        public ZycMoney Price { get; set; } = new(0, "IQD");

        public ZycPrefs Prefs { get; set; } = new();

        public List<ZycPrefs> History { get; set; } = new();

        public ZycPoint Location { get; set; } = new();

        public Dictionary<string, string> Labels { get; set; } = new();

        public Dictionary<string, List<string>> Groups { get; set; } = new();

        public Dictionary<string, JsonElement> Attributes { get; set; } = new();

        public Dictionary<string, object> Bag { get; set; } = new();

        public ZycEnvelope Envelope { get; set; } = new();

        public BitArray Flags { get; set; } = new(0);

        public Uri? Site { get; set; }

        public IPAddress? Ip { get; set; }

        public CultureInfo? Culture { get; set; }

        public Version? Schema { get; set; }

        public BigInteger Big { get; set; }

        public JsonObject? Extra { get; set; }

        public JsonElement Element { get; set; }

        public object? Setting { get; set; }
    }

    public sealed class ZycContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZycContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZycAccount> Accounts => Set<ZycAccount>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        private static string J<T>(T value) => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);

        private static T U<T>(string text) => JsonSerializer.Deserialize<T>(text, (JsonSerializerOptions?)null)!;

        private static ValueComparer<T> ByJson<T>() => new(
            (left, right) => J(left) == J(right),
            value => J(value).GetHashCode(),
            value => U<T>(J(value)));

        protected override void OnModelCreating(ModelBuilder model)
        {
            var account = model.Entity<ZycAccount>();

            account.Property(a => a.Status).HasConversion<string>();
            account.Property(a => a.CreatedUtc).HasConversion(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            account.Property(a => a.Tags).HasConversion(v => string.Join(',', v), v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(), ByJson<List<string>>());
            account.Property(a => a.Aliases).HasConversion(v => string.Join(',', v), v => v.Split(',', StringSplitOptions.RemoveEmptyEntries), ByJson<string[]>());
            account.Property(a => a.Codes).HasConversion(v => J(v), v => U<HashSet<int>>(v), ByJson<HashSet<int>>());
            account.Property(a => a.Ref).HasConversion(v => v.Value, v => new ZycAccountId(v));
            account.Property(a => a.Key).HasConversion(v => v.Value, v => new ZycAccountKey(v));
            account.Property(a => a.Price).HasConversion(v => J(v), v => U<ZycMoney>(v));
            account.Property(a => a.Prefs).HasConversion(v => J(v), v => U<ZycPrefs>(v), ByJson<ZycPrefs>());
            account.Property(a => a.History).HasConversion(v => J(v), v => U<List<ZycPrefs>>(v), ByJson<List<ZycPrefs>>());
            account.Property(a => a.Location).HasConversion(v => J(v), v => U<ZycPoint>(v), ByJson<ZycPoint>());
            account.Property(a => a.Labels).HasConversion(v => J(v), v => U<Dictionary<string, string>>(v), ByJson<Dictionary<string, string>>());
            account.Property(a => a.Groups).HasConversion(v => J(v), v => U<Dictionary<string, List<string>>>(v), ByJson<Dictionary<string, List<string>>>());
            account.Property(a => a.Attributes).HasConversion(v => J(v), v => U<Dictionary<string, JsonElement>>(v), ByJson<Dictionary<string, JsonElement>>());
            account.Property(a => a.Bag).HasConversion(v => J(v), v => U<Dictionary<string, object>>(v), ByJson<Dictionary<string, object>>());
            account.Property(a => a.Envelope).HasConversion(v => J(v), v => U<ZycEnvelope>(v), ByJson<ZycEnvelope>());
            account.Property(a => a.Flags).HasConversion(
                v => string.Concat(v.Cast<bool>().Select(bit => bit ? '1' : '0')),
                v => new BitArray(v.Select(c => c == '1').ToArray()));
            account.Property(a => a.Site).HasConversion(v => v == null ? null : v.ToString(), v => v == null ? null : new Uri(v));
            account.Property(a => a.Ip).HasConversion(v => v == null ? null : v.ToString(), v => v == null ? null : IPAddress.Parse(v));
            account.Property(a => a.Culture).HasConversion(v => v == null ? null : v.Name, v => v == null ? null : CultureInfo.GetCultureInfo(v));
            account.Property(a => a.Schema).HasConversion(v => v == null ? null : v.ToString(), v => v == null ? null : Version.Parse(v));
            account.Property(a => a.Big).HasConversion(v => v.ToString(), v => BigInteger.Parse(v));
            account.Property(a => a.Extra).HasConversion(v => v == null ? null : v.ToJsonString((JsonSerializerOptions?)null), v => v == null ? null : (JsonObject)JsonNode.Parse(v, null, default)!);
            account.Property(a => a.Element).HasConversion(v => v.GetRawText(), v => JsonDocument.Parse(v, default).RootElement.Clone());
            account.Property(a => a.Setting).HasConversion(v => J(v), v => U<object>(v));
        }
    }

    public sealed class ReviewConverterPrecisionTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZycContext _db;

        public ReviewConverterPrecisionTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZycContext(_connection);
            _db.Database.EnsureCreated();

            _db.Accounts.Add(new ZycAccount
            {
                Name = "A1",
                Secret = "zyc-secret",
                Status = ZycStatus.Active,
                CreatedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Tags = { "a", "b" },
                Aliases = new[] { "x" },
                Codes = { 7 },
                Ref = new ZycAccountId(42),
                Key = new ZycAccountKey(Guid.Parse("11111111-2222-3333-4444-555555555555")),
                Price = new ZycMoney(9.5m, "USD"),
                Prefs = new ZycPrefs { Theme = "dark", FontSize = 12, Recent = { "r" } },
                History = { new ZycPrefs { Theme = "light" } },
                Location = new ZycPoint { X = 1, Y = 2 },
                Labels = { ["team"] = "blue" },
                Groups = { ["g"] = new List<string> { "m" } },
                Attributes = { ["n"] = JsonDocument.Parse("5").RootElement.Clone() },
                Bag = { ["colour"] = "red" },
                Envelope = new ZycEnvelope { Kind = "note", Data = "hello" },
                Flags = new BitArray(new[] { true, false, true }),
                Site = new Uri("https://example.org/a"),
                Ip = IPAddress.Parse("10.0.0.1"),
                Culture = CultureInfo.GetCultureInfo("ar-IQ"),
                Schema = new Version(1, 2),
                Big = BigInteger.Parse("123456789012345678901234567890"),
                Extra = new JsonObject { ["p"] = 1 },
                Element = JsonDocument.Parse("{\"q\":2}").RootElement.Clone(),
                Setting = "on"
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>What an unguarded read returns for each column, compared with the guarded read.</summary>
        private static readonly Dictionary<string, Func<ZycAccount, bool>> Holds = new()
        {
            ["Status"] = a => a.Status == ZycStatus.Active,
            ["CreatedUtc"] = a => a.CreatedUtc.Year == 2026,
            ["Tags"] = a => a.Tags.SequenceEqual(new[] { "a", "b" }),
            ["Aliases"] = a => a.Aliases.SequenceEqual(new[] { "x" }),
            ["Codes"] = a => a.Codes.Contains(7),
            ["Ref"] = a => a.Ref.Value == 42,
            ["Key"] = a => a.Key.Value != Guid.Empty,
            ["Price"] = a => a.Price.Amount == 9.5m,
            ["Prefs"] = a => a.Prefs.Theme == "dark",
            ["History"] = a => a.History.Count == 1,
            ["Location"] = a => a.Location.Y == 2,
            ["Labels"] = a => a.Labels.ContainsKey("team"),
            ["Groups"] = a => a.Groups.ContainsKey("g"),
            ["Attributes"] = a => a.Attributes.ContainsKey("n"),
            ["Bag"] = a => a.Bag.ContainsKey("colour"),
            ["Envelope"] = a => a.Envelope.Kind == "note",
            ["Flags"] = a => a.Flags.Length == 3,
            ["Site"] = a => a.Site is not null,
            ["Ip"] = a => a.Ip is not null,
            ["Culture"] = a => a.Culture is not null,
            ["Schema"] = a => a.Schema is not null,
            ["Big"] = a => !a.Big.IsZero,
            ["Extra"] = a => a.Extra is not null,
            ["Element"] = a => a.Element.ValueKind == JsonValueKind.Object,
            ["Setting"] = a => a.Setting is not null
        };

        /// <summary>
        /// Docs check (llms.txt 3.2.0 history): "The trace records a member left out only when a projection is built."
        /// A dry run builds none and returns the rows whole.
        /// </summary>
        [Fact]
        public void Zy_C_a_dry_run_returns_the_row_whole_and_records_what_it_would_leave_out()
        {
            PolicyQueryable<ZycAccount> guarded = _db.Accounts.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, DryRun = true, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            ZycAccount row = guarded.ToList(new Filter()).Data.Single();
            string leftOut = string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Reason?.StartsWith("left out", StringComparison.Ordinal) == true)
                .Select(d => $"{d.FieldPath}: {d.Reason}") ?? Array.Empty<string>());

            ZyLog.Write(_out, $"C dry run: secretReturned={row.Secret is not null} bagReturned={row.Bag.Count > 0} leftOut=[{leftOut}]");

            // A dry run drops nothing, and records what a real run would leave out.
            Assert.True(row.Bag.Count > 0);
            Assert.Contains("Bag: left out whole: it can hold what the policy cannot name", leftOut);
        }

        [Theory]
        [InlineData("Status")]
        [InlineData("CreatedUtc")]
        [InlineData("Tags")]
        [InlineData("Aliases")]
        [InlineData("Codes")]
        [InlineData("Ref")]
        [InlineData("Key")]
        [InlineData("Price")]
        [InlineData("Prefs")]
        [InlineData("History")]
        [InlineData("Location")]
        [InlineData("Labels")]
        [InlineData("Groups")]
        [InlineData("Attributes")]
        [InlineData("Flags")]
        [InlineData("Site")]
        [InlineData("Ip")]
        [InlineData("Culture")]
        [InlineData("Schema")]
        [InlineData("Big")]
        [InlineData("Extra")]
        [InlineData("Element")]
        public void Zy_C_a_converted_column_beside_a_top_level_denial_is_kept(string column)
        {
            ZycAccount unguarded = _db.Accounts.AsNoTracking().Single();

            Assert.True(Holds[column](unguarded), "the unguarded read itself lost " + column);

            PolicyQueryable<ZycAccount> guarded = _db.Accounts.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            ZycAccount row;

            try
            {
                row = guarded.ToList(new Filter()).Data.Single();
            }
            catch (Exception e)
            {
                ZyLog.Write(_out, $"C {column}: THREW {e.GetType().Name} {e.Message.Split('\n')[0]}");

                throw;
            }

            string about = string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed && d.FieldPath.StartsWith(column, StringComparison.Ordinal))
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

            bool kept = Holds[column](row);

            ZyLog.Write(_out, $"C {column}: {(kept ? "KEPT" : "LEFT OUT")} secretKept={row.Secret is not null} [{about}]");

            Assert.Null(row.Secret);
            Assert.True(kept, $"{column} left out: {about}");
        }

        [Theory]
        [InlineData("Bag")]
        [InlineData("Envelope")]
        [InlineData("Setting")]
        public void Zy_C_a_converted_column_that_can_hold_any_object_is_left_out_beside_a_top_level_denial(string column)
        {
            ZycAccount unguarded = _db.Accounts.AsNoTracking().Single();

            Assert.True(Holds[column](unguarded), "the unguarded read itself lost " + column);

            PolicyQueryable<ZycAccount> guarded = _db.Accounts.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            ZycAccount row;

            try
            {
                row = guarded.ToList(new Filter()).Data.Single();
            }
            catch (Exception e)
            {
                ZyLog.Write(_out, $"C {column}: THREW {e.GetType().Name} {e.Message.Split('\n')[0]}");

                throw;
            }

            string about = string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed && d.FieldPath.StartsWith(column, StringComparison.Ordinal))
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

            bool kept = Holds[column](row);

            ZyLog.Write(_out, $"C {column}: {(kept ? "KEPT" : "LEFT OUT")} secretKept={row.Secret is not null} [{about}]");

            Assert.Null(row.Secret);
            Assert.False(kept, $"{column} kept: {about}");
            Assert.Contains($"{column} Dropped: left out whole: it can hold what the policy cannot name", about);
        }
    }
}
