using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ round 3 probes: models

    // ---- P1: an unmapped getter over a private, mapped field ----------------------------------------

    public class ZrEmployee
    {
        private string? _band;

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Computed from a private field EF Core maps as a column; the model does not map this member.</summary>
        [NotMapped]
        public ZrPay Pay => new() { Grade = "G", Band = _band };

        public void SetBand(string band) => _band = band;
    }

    public class ZrPay
    {
        public string? Grade { get; set; }

        [DwDenied]
        public string? Band { get; set; }
    }

    // ---- P1b: the same inside an owned type -----------------------------------------------------------

    public class ZrClinic
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZrContact Contact { get; set; } = new();
    }

    public class ZrContact
    {
        private string? _phone;

        public string City { get; set; } = string.Empty;

        [NotMapped]
        public ZrPhoneView Phone => new() { Number = _phone };

        public void SetPhone(string phone) => _phone = phone;
    }

    public class ZrPhoneView
    {
        [DwDenied]
        public string? Number { get; set; }
    }

    // ---- P2: an unmapped getter exposing a private, automatically included navigation ------------------

    public class ZrLedger
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        private List<ZrEntry> Entries { get; set; } = new();

        [NotMapped]
        public IReadOnlyList<ZrEntry> Items => Entries;

        public void Add(ZrEntry entry) => Entries.Add(entry);
    }

    public class ZrEntry
    {
        public int Id { get; set; }

        public int ZrLedgerId { get; set; }

        public string Memo { get; set; } = string.Empty;

        [DwDenied]
        public string? Amount { get; set; }
    }

    /// <summary>An entity that includes the ledger, so the ledger is a loaded navigation beneath the row.</summary>
    public class ZrBook
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public int ZrLedgerId { get; set; }

        public ZrLedger? Ledger { get; set; }
    }

    // ---- P3: generic subtypes, which the subtype search never lists -----------------------------------

    public class ZrCreature
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZrTagged<TTag> : ZrCreature
    {
        [DwDenied]
        public string? Secret { get; set; }

        public TTag? Tag { get; set; }
    }

    public class ZrPen
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public ZrCreature? Pet { get; set; }
    }

    /// <summary>An EF Core hierarchy whose derived entity is a closed generic type.</summary>
    public class ZrOrg
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZrGenOrg<TMarker> : ZrOrg
    {
        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>A derived entity whose field no attribute denies, for a rule to deny instead.</summary>
    public class ZrBank : ZrOrg
    {
        public string? Swift { get; set; }
    }

    public class ZrLoan
    {
        public int Id { get; set; }

        public string Note { get; set; } = string.Empty;

        public int ZrOrgId { get; set; }

        public ZrOrg? Org { get; set; }
    }

    public class ZrLoanRow
    {
        public int Id { get; set; }

        public string Note { get; set; } = string.Empty;

        public ZrOrg? Org { get; set; }
    }

    // ---- P4: a member typed as a framework base class ---------------------------------------------------

    public class ZrDeclinedException : Exception
    {
        public ZrDeclinedException()
            : base("declined")
        {
        }

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZrJobResult
    {
        public int Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public Exception? Error { get; set; }
    }

    // ---- P5: a rule on a subtype's field, reached through a base-typed member ---------------------------

    public class ZrCat : ZrCreature
    {
        public string? Microchip { get; set; }
    }

    // ---- P8: two members differing only in letter case -------------------------------------------------

    public class ZrCaseRow
    {
        public int Id { get; set; }

        public ZrCardInfo INFO { get; set; } = new();

        public ZrSafeInfo Info { get; set; } = new();
    }

    public class ZrCardInfo
    {
        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZrSafeInfo
    {
        public string? Label { get; set; }
    }

    // ---- P6: a Concat of two projections ---------------------------------------------------------------

    public class ZrAcctRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZrPersonView? Person { get; set; }
    }

    public class ZrPersonView
    {
        public string? Name { get; set; }

        [DwDenied]
        public string? TaxId { get; set; }
    }

    // ---- P9: many-to-many with an explicit join entity carrying a denied payload ------------------------

    public class ZrPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public List<ZrTag> Tags { get; set; } = new();

        public List<ZrPostTag> PostTags { get; set; } = new();
    }

    public class ZrTag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZrPost> Posts { get; set; } = new();

        public List<ZrPostTag> PostTags { get; set; } = new();
    }

    public class ZrPostTag
    {
        public int ZrPostId { get; set; }

        public int ZrTagId { get; set; }

        public ZrPost? Post { get; set; }

        public ZrTag? Tag { get; set; }

        [DwDenied]
        public string? AddedBy { get; set; }
    }

    public sealed class ZrProbeContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZrProbeContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZrEmployee> Employees => Set<ZrEmployee>();

        public DbSet<ZrClinic> Clinics => Set<ZrClinic>();

        public DbSet<ZrLedger> Ledgers => Set<ZrLedger>();

        public DbSet<ZrBook> Books => Set<ZrBook>();

        public DbSet<ZrOrg> Orgs => Set<ZrOrg>();

        public DbSet<ZrLoan> Loans => Set<ZrLoan>();

        public DbSet<ZrPost> Posts => Set<ZrPost>();

        public DbSet<ZrTag> Tags => Set<ZrTag>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZrEmployee>().Property<string?>("_band");
            model.Entity<ZrClinic>().OwnsOne(c => c.Contact, o => o.Property<string?>("_phone"));
            model.Entity<ZrLedger>(b =>
            {
                b.HasMany<ZrEntry>("Entries").WithOne().HasForeignKey(e => e.ZrLedgerId);
                b.Navigation("Entries").AutoInclude();
            });
            model.Entity<ZrGenOrg<int>>();
            model.Entity<ZrBank>();
            model.Entity<ZrPost>()
                .HasMany(p => p.Tags)
                .WithMany(t => t.Posts)
                .UsingEntity<ZrPostTag>(
                    j => j.HasOne(pt => pt.Tag).WithMany(t => t.PostTags).HasForeignKey(pt => pt.ZrTagId),
                    j => j.HasOne(pt => pt.Post).WithMany(p => p.PostTags).HasForeignKey(pt => pt.ZrPostId),
                    j => j.HasKey(pt => new { pt.ZrPostId, pt.ZrTagId }));
        }
    }

    // ============================================================================ round 3 probes: tests

    /// <summary>
    /// Round 3 adversarial probes. Every assertion states the safe outcome, so a red test is a leak. A refusal with
    /// FieldDeniedForSelect is a safe outcome.
    /// </summary>
    public sealed class ReviewLeakTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext _db;

        public ReviewLeakTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ZrProbeContext(_connection);
            _db.Database.EnsureCreated();

            ZrEmployee employee = new() { Name = "E1" };
            employee.SetBand("band-secret");
            _db.Employees.Add(employee);

            ZrClinic clinic = new() { Name = "C1", Contact = new ZrContact { City = "Basra" } };
            clinic.Contact.SetPhone("phone-secret");
            _db.Clinics.Add(clinic);

            ZrLedger ledger = new() { Name = "L1" };
            ledger.Add(new ZrEntry { Memo = "m", Amount = "amount-secret" });
            _db.Ledgers.Add(ledger);
            _db.Books.Add(new ZrBook { Title = "B1", Ledger = ledger });

            _db.Loans.Add(new ZrLoan { Note = "gen", Org = new ZrGenOrg<int> { Name = "G", Secret = "generic-entity-secret" } });
            _db.Loans.Add(new ZrLoan { Note = "bank", Org = new ZrBank { Name = "K", Swift = "swift-by-rule" } });

            ZrPost post = new() { Title = "P1" };
            ZrTag tag = new() { Name = "T1" };
            _db.Posts.Add(post);
            _db.Tags.Add(tag);
            _db.SaveChanges();
            _db.Set<ZrPostTag>().Add(new ZrPostTag { ZrPostId = post.Id, ZrTagId = tag.Id, AddedBy = "join-secret" });
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

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict, params IDwPolicyProvider[] more)
            where T : class =>
            source.ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray()));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        private static FakePolicyProvider Denying(params string[] paths)
        {
            FakePolicyProvider rules = new();

            foreach (string path in paths)
            {
                rules.Add(path, PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);
            }

            return rules;
        }

        private static FakePolicyProvider DenyingAllBut(params string[] allowed)
        {
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            foreach (string path in allowed)
            {
                rules.Add(path, PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);
            }

            return rules;
        }

        /// <summary>
        /// True when anything reachable from a value holds the text, read by each object's runtime type, through
        /// every readable public property (getter-only ones included, as a serializer writes them).
        /// </summary>
        private static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is ValueType || current is System.Reflection.MemberInfo)
                {
                    continue;
                }

                if (current is string held)
                {
                    if (held == text)
                    {
                        return true;
                    }

                    continue;
                }

                if (!seen.Add(current))
                {
                    continue;
                }

                if (current is IEnumerable items)
                {
                    foreach (object? item in items)
                    {
                        pending.Push(item);
                    }

                    continue;
                }

                foreach (System.Reflection.PropertyInfo property in current.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    object? read;

                    try
                    {
                        read = property.GetValue(current);
                    }
                    catch
                    {
                        continue;
                    }

                    pending.Push(read);
                }
            }

            return false;
        }

        /// <summary>Runs a guarded read; a FieldDeniedForSelect refusal is safe and reads as no rows.</summary>
        private static object? SafeRead(Func<object?> read)
        {
            try
            {
                return read();
            }
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------------ P1

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P1_an_unmapped_getter_over_a_private_mapped_field_does_not_carry_the_denied_value(DwTier tier)
        {
            // Unguarded, the entity carries the value through the getter.
            Assert.True(Holds(_db.Employees.AsNoTracking().ToList(), "band-secret"));

            Assert.False(Holds(SafeRead(() => Guard(_db.Employees, tier).ToList(new Filter()).Data), "band-secret"));
        }

        [Fact]
        public void Zr_P1_dynamic_terminal()
        {
            Assert.False(Holds(SafeRead(() => Guard(_db.Employees).ToListDynamic(new Filter()).Data), "band-secret"));
        }

        [Fact]
        public async Task Zr_P1_segment_terminal()
        {
            object? rows = null;

            try
            {
                rows = (await Guard(_db.Employees).ToListAsync(new Segment(), CancellationToken.None)).Data;
            }
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
            }

            Assert.False(Holds(rows, "band-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P1b_an_owned_type_with_an_unmapped_getter_over_a_private_mapped_field(DwTier tier)
        {
            Assert.True(Holds(_db.Clinics.AsNoTracking().ToList(), "phone-secret"));

            Assert.False(Holds(SafeRead(() => Guard(_db.Clinics, tier).ToList(new Filter()).Data), "phone-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P2

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P2_an_unmapped_getter_exposing_a_private_auto_included_navigation(DwTier tier)
        {
            Assert.True(Holds(_db.Ledgers.AsNoTracking().ToList(), "amount-secret"));

            Assert.False(Holds(SafeRead(() => Guard(_db.Ledgers, tier).ToList(new Filter()).Data), "amount-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P2b_the_same_beneath_an_included_navigation(DwTier tier)
        {
            IQueryable<ZrBook> source = _db.Books.Include(b => b.Ledger);

            Assert.True(Holds(source.AsNoTracking().ToList(), "amount-secret"));

            Assert.False(Holds(SafeRead(() => Guard(source, tier).ToList(new Filter()).Data), "amount-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P3

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P3_rows_in_memory_of_a_generic_subtype_with_a_denied_field(DwTier tier)
        {
            ZrCreature[] rows = { new ZrTagged<int> { Id = 1, Name = "Rex", Secret = "generic-secret", Tag = 7 } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data), "generic-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P3b_a_base_typed_member_in_memory_holding_a_generic_subtype(DwTier tier)
        {
            ZrPen[] rows = { new() { Id = 1, Label = "pen", Pet = new ZrTagged<int> { Id = 2, Name = "Rex", Secret = "generic-secret" } } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data), "generic-secret"));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(Selecting("Id", "Pet")).Data), "generic-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P3c_a_projected_member_holding_a_closed_generic_derived_entity(DwTier tier)
        {
            IQueryable<ZrLoanRow> rows = _db.Loans.Select(l => new ZrLoanRow { Id = l.Id, Note = l.Note, Org = l.Org });

            Assert.True(Holds(rows.ToList(), "generic-entity-secret"));

            Assert.False(Holds(SafeRead(() => Guard(rows, tier).ToList(new Filter()).Data), "generic-entity-secret"));
            Assert.False(Holds(SafeRead(() => Guard(rows, tier).ToList(Selecting("Id", "Org")).Data), "generic-entity-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P3c_control_the_entity_query_reads_the_model(DwTier tier)
        {
            IQueryable<ZrLoan> source = _db.Loans.Include(l => l.Org);

            Assert.True(Holds(source.AsNoTracking().ToList(), "generic-entity-secret"));
            Assert.False(Holds(SafeRead(() => Guard(source, tier).ToList(new Filter()).Data), "generic-entity-secret"));
            Assert.False(Holds(SafeRead(() => Guard(_db.Orgs, tier).ToList(new Filter()).Data), "generic-entity-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P4

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P4_a_member_typed_as_a_framework_base_class_holding_an_application_subtype(DwTier tier)
        {
            ZrJobResult[] rows = { new() { Id = 1, Status = "failed", Error = new ZrDeclinedException { Pan = "exception-secret" } } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data), "exception-secret"));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(Selecting("Id", "Error")).Data), "exception-secret"));
        }

        [Fact]
        public void Zr_P4b_under_a_deny_by_default_policy()
        {
            ZrJobResult[] rows = { new() { Id = 1, Status = "failed", Error = new ZrDeclinedException { Pan = "exception-secret" } } };

            FakePolicyProvider rules = DenyingAllBut("Id", "Error");

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), DwTier.Strict, rules).ToList(Selecting("Id", "Error")).Data), "exception-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P5

        [Fact]
        public void Zr_P5_control_a_rule_on_a_subtype_field_at_the_root_is_enforced()
        {
            ZrCreature[] rows = { new ZrCat { Id = 1, Name = "Tom", Microchip = "chip-by-rule" } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), DwTier.Strict, Denying("Microchip")).ToList(new Filter()).Data), "chip-by-rule"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P5_a_rule_on_a_subtype_field_through_a_base_typed_member_in_memory(DwTier tier)
        {
            ZrPen[] rows = { new() { Id = 1, Label = "pen", Pet = new ZrCat { Id = 2, Name = "Tom", Microchip = "chip-by-rule" } } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier, Denying("Pet.Microchip")).ToList(new Filter()).Data), "chip-by-rule"));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier, Denying("Pet.Microchip")).ToList(Selecting("Id", "Pet")).Data), "chip-by-rule"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P5_control_the_entity_query_enforces_the_rule(DwTier tier)
        {
            Assert.True(Holds(_db.Loans.Include(l => l.Org).AsNoTracking().ToList(), "swift-by-rule"));

            Assert.False(Holds(SafeRead(() => Guard(_db.Loans.Include(l => l.Org), tier, Denying("Org.Swift")).ToList(new Filter()).Data), "swift-by-rule"));
            Assert.False(Holds(SafeRead(() => Guard(_db.Loans, tier, Denying("Org.Swift")).ToList(Selecting("Id", "Org")).Data), "swift-by-rule"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P5_a_rule_on_a_subtype_field_through_a_projected_member(DwTier tier)
        {
            IQueryable<ZrLoanRow> rows = _db.Loans.Select(l => new ZrLoanRow { Id = l.Id, Note = l.Note, Org = l.Org });

            Assert.False(Holds(SafeRead(() => Guard(rows, tier, Denying("Org.Swift")).ToList(new Filter()).Data), "swift-by-rule"));
            Assert.False(Holds(SafeRead(() => Guard(rows, tier, Denying("Org.Swift")).ToList(Selecting("Id", "Org")).Data), "swift-by-rule"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P5_a_rule_on_a_subtype_field_through_the_composable_Select(DwTier tier)
        {
            Assert.False(Holds(
                SafeRead(() => Guard(_db.Loans, tier, Denying("Org.Swift")).Select(new List<string> { "Id", "Org" }).ToList(new Filter()).Data),
                "swift-by-rule"));
        }

        // ------------------------------------------------------------------------------------------------ P6

        [Fact]
        public void Zr_P6_a_Concat_of_two_projections_assigning_different_members()
        {
            IQueryable<ZrAcctRow> rows = _db.Employees
                .Select(e => new ZrAcctRow { Id = e.Id, Name = e.Name })
                .Concat(_db.Employees.Select(e => new ZrAcctRow
                {
                    Id = e.Id,
                    Name = e.Name,
                    Person = new ZrPersonView { Name = e.Name, TaxId = "concat-secret" }
                }));

            Exception? unguarded = Record.Exception(() => rows.ToList());

            if (unguarded is not null)
            {
                // EF Core cannot run it at all, so nothing can leak through it.
                return;
            }

            Assert.False(Holds(SafeRead(() => Guard(rows).ToList(new Filter()).Data), "concat-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P8

        [Fact]
        public void Zr_P8_two_members_differing_only_in_letter_case()
        {
            ZrCaseRow[] rows = { new() { Id = 1, INFO = new ZrCardInfo { Pan = "case-secret" }, Info = new ZrSafeInfo { Label = "ok" } } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "case-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P9

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P9_including_a_skip_navigation_does_not_carry_the_join_entity_payload(DwTier tier)
        {
            IQueryable<ZrPost> source = _db.Posts.Include(p => p.Tags);

            bool unguardedLeaks = Holds(source.AsNoTracking().ToList(), "join-secret");

            Assert.False(Holds(SafeRead(() => Guard(source, tier).ToList(new Filter()).Data), "join-secret"),
                $"unguarded leaks: {unguardedLeaks}");
        }
    }
}
