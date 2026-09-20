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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A hashed member with no salt supplied: the MissingHashSalt refusal.</summary>
    public class Ov7Hashed
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Hash)]
        [DwDeny(PolicyFeature.Order)]
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>A tokenized member with no vault supplied: the MissingTokenVault refusal.</summary>
    public class Ov7Tokenized
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Tokenize)]
        [DwDeny(PolicyFeature.Order)]
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>A generalized key, so two groups can collide once transformed.</summary>
    public class Ov7Grouped
    {
        public int Id { get; set; }

        [DwGeneralize(GeneralizeMode.Round, Step = 100, AllowAggregate = true, MinGroupSize = 1)]
        [DwDeny(PolicyFeature.Order)]
        public int Bracket { get; set; }

        public decimal Amount { get; set; }
    }

    /// <summary>Several audited members beneath an owned member.</summary>
    public class Ov7Nested
    {
        public int Id { get; set; }

        [DwNoSelect]
        public decimal Hidden { get; set; }

        public Ov7NestedOwn Own { get; set; } = new();
    }

    public class Ov7NestedOwn
    {
        [DwAudit(PolicyFeature.Select)]
        public string Zip { get; set; } = string.Empty;

        public string Street { get; set; } = string.Empty;
    }

    public sealed class Ov7RefusalContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public Ov7RefusalContext(SqliteConnection connection) => _connection = connection;

        public DbSet<Ov7Hashed> Hashed => Set<Ov7Hashed>();

        public DbSet<Ov7Tokenized> Tokenized => Set<Ov7Tokenized>();

        public DbSet<Ov7Grouped> Grouped => Set<Ov7Grouped>();

        public DbSet<Ov7Nested> Nested => Set<Ov7Nested>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Ov7Nested>().OwnsOne(n => n.Own);
    }

    /// <summary>
    /// Round 7: the four refusals' dry-run reading and audit path, the audit cap's own refusal, and
    /// what an audited member beneath an owned member records.
    /// </summary>
    public sealed class Ov7RefusalProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Ov7RefusalContext _db;

        public Ov7RefusalProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Ov7RefusalContext(_connection);
            _db.Database.EnsureCreated();

            for (int i = 1; i <= 4; i++)
            {
                _db.Hashed.Add(new Ov7Hashed { Name = $"n{i}", Secret = $"s{i}" });
                _db.Tokenized.Add(new Ov7Tokenized { Name = $"n{i}", Secret = $"s{i}" });

                // 10 and 20 fall in one bucket of 100; 150 and 199 in another.
                _db.Grouped.Add(new Ov7Grouped { Bracket = i <= 2 ? i * 10 : 100 + (i * 20), Amount = i });
                _db.Nested.Add(new Ov7Nested
                {
                    Hidden = i,
                    Own = new Ov7NestedOwn { Zip = $"z{i}", Street = $"st{i}" }
                });
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private void Say(string probe, string value) => _out.WriteLine($"OV7R {probe} = {value}");

        private static PolicyResolver Resolver() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Opts(DwTier tier, bool optionDry, int auditCap = 10_000)
        {
            DwPolicyOptions options = new() { Tier = tier, DryRun = optionDry, AuditRefusals = true };
            options.Caps.MinGroupSize = 1;
            options.Caps.MaxAuditEvents = auditCap;

            return options;
        }

        /// <summary>Runs one posture and reports the refusal's shape plus what the refusal audit stored.</summary>
        private string Shape(DwTier tier, bool optionDry, bool contextDry, Func<PolicyQueryable<object>, int> _,
            Func<DwPolicyContext, DwPolicyOptions, int> run)
        {
            DwPolicyContext context = new DwPolicyContext()
                .WithSubject(DwSubjectKind.User, "u1");
            context.DryRun = contextDry;

            DwPolicyOptions options = Opts(tier, optionDry);

            string outcome;

            try
            {
                outcome = $"OK({run(context, options)})";
            }
            catch (PolicyException refusal)
            {
                outcome = $"{refusal.ErrorCode}/field='{refusal.FieldPath}'/origin={(refusal.SourceOrigin is null ? "none" : "set")}";
            }
            catch (Exception other)
            {
                outcome = other.GetType().Name;
            }

            string audited = string.Join("|", context.PendingAuditEvents
                .Where(e => e.ErrorCode is not null)
                .Select(e => e.FieldPath));

            return $"{outcome} ;; auditPath='{audited}'";
        }

        private static readonly (string Label, DwTier Tier, bool OptionDry, bool ContextDry)[] Postures =
        {
            ("strict", DwTier.Strict, false, false),
            ("strict+optionDry", DwTier.Strict, true, false),
            ("strict+contextDry", DwTier.Strict, false, true),
            ("convenience", DwTier.Convenience, false, false)
        };

        // ---- 4. the four refusals ------------------------------------------------------------

        [Fact]
        public void Ov7_Four_Refusals()
        {
            foreach ((string label, DwTier tier, bool optionDry, bool contextDry) in Postures)
            {
                // 1. MissingHashSalt: no salt on the options.
                Say($"d1.hashsalt[{label}]", Shape(tier, optionDry, contextDry, null!, (context, options) =>
                    _db.Hashed.AsQueryable().ApplyPolicy(context, options, Resolver())
                        .ToList(new Filter()).Data.Count));

                // 2. MissingTokenVault: no vault on the options.
                Say($"d2.tokenvault[{label}]", Shape(tier, optionDry, contextDry, null!, (context, options) =>
                    _db.Tokenized.AsQueryable().ApplyPolicy(context, options, Resolver())
                        .ToList(new Filter()).Data.Count));

                // 3. TransformRequiresMaterialization: a transformed type handed back as a query.
                Say($"d3.materialize[{label}]", Shape(tier, optionDry, contextDry, null!, (context, options) =>
                {
                    IQueryable handed = _db.Hashed.AsQueryable().ApplyPolicy(context, options, Resolver())
                        .FilterDynamic(new Filter());

                    return handed is null ? 0 : 1;
                }));

                // 4. Two groups colliding once the key is generalized.
                Say($"d4.collision[{label}]", Shape(tier, optionDry, contextDry, null!, (context, options) =>
                    _db.Grouped.AsQueryable().ApplyPolicy(context, options, Resolver())
                        .ToList(new Summary
                        {
                            GroupBy = new GroupBy
                            {
                                Fields = new List<string> { "Bracket" },
                                AggregateBy = new List<AggregateBy>
                                {
                                    new() { Aggregator = Aggregator.Sumation, Field = "Amount", Alias = "total" }
                                }
                            }
                        }).Data.Count));
            }
        }

        // ---- 1b. the audit cap's own refusal, and a nested audited member ----------------------

        [Fact]
        public void Ov7_Audit_Cap_And_Nested()
        {
            // Four audited members on Ov7Aud. Walk the cap down through the count.
            foreach (int cap in new[] { 1, 2, 3, 4, 5 })
            {
                foreach ((string label, DwTier tier, bool optionDry, bool contextDry) in Postures)
                {
                    DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
                    context.DryRun = contextDry;

                    DwPolicyOptions options = Opts(tier, optionDry, cap);

                    string outcome;

                    try
                    {
                        using SqliteConnection side = new("DataSource=:memory:");
                        side.Open();
                        using Ov7Context db = new(side);
                        db.Database.EnsureCreated();
                        db.Auds.Add(new Ov7Aud
                        {
                            Name = "n", Email = "e", Phone = "p", Ssn = "s",
                            Dob = new DateTime(1990, 1, 1), Own = new Ov7Own { Street = "st", Zip = "z" }
                        });
                        db.SaveChanges();
                        db.ChangeTracker.Clear();

                        int count = db.Auds.AsQueryable().ApplyPolicy(context, options, Resolver())
                            .ToList(new Filter()).Data.Count;
                        outcome = $"OK({count})";
                    }
                    catch (PolicyException refusal)
                    {
                        outcome =
                            $"{refusal.ErrorCode}/field='{refusal.FieldPath}'/feature={refusal.Feature}" +
                            $"/origin={(refusal.SourceOrigin is null ? "none" : "set")}";
                    }
                    catch (Exception other)
                    {
                        outcome = other.GetType().Name;
                    }

                    Say($"e.cap{cap}[{label}]", outcome);
                }
            }

            // An audited member beneath an owned member, with nothing denied: the row comes back whole.
            DwPolicyContext whole = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
            FilterResult<Ov7Nested>? wholeResult = null;

            try
            {
                wholeResult = _db.Nested.AsQueryable()
                    .ApplyPolicy(whole, Opts(DwTier.Strict, false), Resolver())
                    .ToList(new Filter());
            }
            catch (Exception failure)
            {
                Say("f.nested.whole", failure.GetType().Name);
            }

            if (wholeResult is not null)
            {
                Say("f.nested.whole.zipReturned",
                    wholeResult.Data.Count > 0 ? wholeResult.Data[0].Own.Zip : "NONE");
                Say("f.nested.whole.events", whole.PendingAuditEvents.Count.ToString());
                Say("f.nested.whole.paths",
                    string.Join("|", whole.PendingAuditEvents.Select(e => e.FieldPath)));

                // An audited column of an owned type is read by whoever receives the object holding
                // it, named or not: a member kept whole hands back everything inside it.
                Assert.Contains(whole.PendingAuditEvents, e => e.FieldPath == "Own.Zip");
            }

            // The caller names the nested path themselves.
            DwPolicyContext named = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            try
            {
                _db.Nested.AsQueryable().ApplyPolicy(named, Opts(DwTier.Strict, false), Resolver())
                    .ToListDynamic(new Filter { Selects = new List<string> { "Own.Zip" } });
                Say("f.nested.named.events", named.PendingAuditEvents.Count.ToString());
                Say("f.nested.named.paths",
                    string.Join("|", named.PendingAuditEvents.Select(e => e.FieldPath)));
            }
            catch (Exception failure)
            {
                Say("f.nested.named", failure.GetType().Name + ":" + failure.Message);
            }
        }
    }
}
