using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A realistically audited record: twelve audited columns, a navigation, a collection.</summary>
    public class Ov7Wide
    {
        public int Id { get; set; }

        public string Reference { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A1 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A2 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A3 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A4 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A5 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A6 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A7 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A8 { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)] public string A9 { get; set; } = string.Empty;

        [DwAudit] public string A10 { get; set; } = string.Empty;

        [DwAudit] public string A11 { get; set; } = string.Empty;

        [DwAudit] public string A12 { get; set; } = string.Empty;

        public List<Ov7WideLine> Lines { get; set; } = new();

        /// <summary>An audited navigation. Nothing includes it, so no query loads it.</summary>
        [DwAudit(PolicyFeature.Select)]
        public Ov7Side? Side { get; set; }
    }

    public class Ov7Side
    {
        public int Id { get; set; }

        public string Tag { get; set; } = string.Empty;
    }

    public class Ov7WideLine
    {
        public int Id { get; set; }

        public int Ov7WideId { get; set; }

        [DwAudit(PolicyFeature.Select)]
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>A masked key, so two groups collide once the values are starred out.</summary>
    public class Ov7Collide
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Full)]
        [DwDeny(PolicyFeature.Order)]
        public string Label { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    public sealed class Ov7ScaleContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public Ov7ScaleContext(SqliteConnection connection) => _connection = connection;

        public DbSet<Ov7Wide> Wides => Set<Ov7Wide>();

        public DbSet<Ov7WideLine> WideLines => Set<Ov7WideLine>();

        public DbSet<Ov7Collide> Collides => Set<Ov7Collide>();

        public DbSet<Ov7Side> Sides => Set<Ov7Side>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// Round 7: how many events a realistic request now costs, and the fourth refusal.
    /// </summary>
    public sealed class Ov7ScaleProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Ov7ScaleContext _db;

        public Ov7ScaleProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Ov7ScaleContext(_connection);
            _db.Database.EnsureCreated();

            for (int i = 1; i <= 50; i++)
            {
                Ov7Wide wide = new()
                {
                    Reference = $"R{i}",
                    A1 = "a", A2 = "b", A3 = "c", A4 = "d", A5 = "e", A6 = "f",
                    A7 = "g", A8 = "h", A9 = "i", A10 = "j", A11 = "k", A12 = "l"
                };

                wide.Lines.Add(new Ov7WideLine { Detail = $"d{i}a" });
                wide.Lines.Add(new Ov7WideLine { Detail = $"d{i}b" });
                _db.Wides.Add(wide);

                // Two distinct labels of the same length mask to the same run of stars.
                _db.Collides.Add(new Ov7Collide { Label = i % 2 == 0 ? "aaaa" : "bbbb", Amount = i });
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private void Say(string probe, string value) => _out.WriteLine($"OV7S {probe} = {value}");

        private static PolicyResolver Resolver() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyContext Ctx(bool dry = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
            context.DryRun = dry;

            return context;
        }

        private static DwPolicyOptions Opts(DwTier tier = DwTier.Strict, bool dry = false)
        {
            DwPolicyOptions options = new() { Tier = tier, DryRun = dry, AuditRefusals = true };
            options.Caps.MinGroupSize = 1;

            return options;
        }

        [Fact]
        public void Ov7_Scale_And_Collision()
        {
            // Fifty rows, twelve audited columns, no projection named.
            DwPolicyContext wide = Ctx();
            int rows = _db.Wides.AsQueryable().ApplyPolicy(wide, Opts(), Resolver())
                .ToList(new Filter()).Data.Count;
            Say("g1.wide.rows", rows.ToString());
            Say("g1.wide.events", wide.PendingAuditEvents.Count.ToString());

            // The same request with the navigation loaded, which is what an Include would do.
            DwPolicyContext included = Ctx();
            List<Ov7Wide> loaded = _db.Wides.Include(w => w.Lines)
                .ApplyPolicy(included, Opts(), Resolver())
                .ToList(new Filter()).Data;
            int withLines = loaded.Count;
            Say("g2.included.rows", withLines.ToString());
            Say("g2.included.detailReturned",
                withLines > 0 && loaded[0].Lines.Count > 0 ? loaded[0].Lines[0].Detail : "NONE");
            Say("g2.included.events", included.PendingAuditEvents.Count.ToString());
            Say("g2.included.paths",
                string.Join("|", included.PendingAuditEvents.Select(e => e.FieldPath)));

            // Ten terminals on one long-lived context: the multiplier per request.
            DwPolicyContext many = Ctx();

            for (int i = 0; i < 10; i++)
            {
                _db.Wides.AsQueryable().ApplyPolicy(many, Opts(), Resolver()).ToList(new Filter());
            }

            Say("g3.ten-queries.events", many.PendingAuditEvents.Count.ToString());

            // Side is audited and nothing loads it: the query returns null for it every time.
            DwPolicyContext side = Ctx();
            List<Ov7Wide> unloaded = _db.Wides.AsQueryable().ApplyPolicy(side, Opts(), Resolver())
                .ToList(new Filter()).Data;
            Say("g5.navigation.loaded",
                unloaded.Count > 0 ? (unloaded[0].Side is null ? "null" : "loaded") : "NONE");
            Say("g5.navigation.recorded",
                side.PendingAuditEvents.Any(e => e.FieldPath == "Side") ? "yes" : "no");
            Say("g5.navigation.events", side.PendingAuditEvents.Count.ToString());

            // Nothing loads it, so the caller receives null for it: not a read, and not an event
            // counted against a cap that refuses rather than dropping a record.
            Assert.DoesNotContain(side.PendingAuditEvents, e => e.FieldPath == "Side");

            // The fourth refusal: two groups sharing a key once masked.
            foreach ((string label, DwTier tier, bool optionDry, bool contextDry) in
                new[]
                {
                    ("strict", DwTier.Strict, false, false),
                    ("strict+optionDry", DwTier.Strict, true, false),
                    ("strict+contextDry", DwTier.Strict, false, true),
                    ("convenience", DwTier.Convenience, false, false)
                })
            {
                DwPolicyContext context = Ctx(contextDry);
                string outcome;

                try
                {
                    int groups = _db.Collides.AsQueryable()
                        .ApplyPolicy(context, Opts(tier, optionDry), Resolver())
                        .ToList(new Summary
                        {
                            GroupBy = new GroupBy
                            {
                                Fields = new List<string> { "Label" },
                                AggregateBy = new List<AggregateBy>
                                {
                                    new() { Aggregator = Aggregator.Count, Alias = "n" }
                                }
                            }
                        }).Data.Count;
                    outcome = $"OK({groups})";
                }
                catch (PolicyException refusal)
                {
                    outcome =
                        $"{refusal.ErrorCode}/field='{refusal.FieldPath}'" +
                        $"/origin={(refusal.SourceOrigin is null ? "none" : "set")}";
                }
                catch (Exception other)
                {
                    outcome = other.GetType().Name + ":" +
                        (other.InnerException?.GetType().Name ?? "-") + ":" +
                        (other.InnerException?.Message ?? other.Message);
                }

                string audited = string.Join("|", context.PendingAuditEvents
                    .Where(e => e.ErrorCode is not null)
                    .Select(e => e.FieldPath));

                Say($"g4.collision[{label}]", $"{outcome} ;; auditPath='{audited}'");
            }
        }
    }
}
