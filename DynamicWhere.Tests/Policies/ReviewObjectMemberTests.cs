using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
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
    // ============================================================================ members that can hold any object: models

    /// <summary>The application type an event's payload column is converted to and from.</summary>
    public class ZvCardIssued
    {
        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    /// <summary>An event whose payload column is typed object and converted to an application type.</summary>
    public class ZvEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        public object? Payload { get; set; }
    }

    /// <summary>The same, with a top-level denial that asks for a projection on its own.</summary>
    public class ZvAuditedEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public object? Payload { get; set; }
    }

    /// <summary>An owned member holding the converted payload, beside a top-level denial.</summary>
    public class ZvEnvelope
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public ZvMeta Meta { get; set; } = new();
    }

    public class ZvMeta
    {
        public string Tag { get; set; } = string.Empty;

        public object? Payload { get; set; }
    }

    /// <summary>An unmapped getter typed object, over a private automatically included navigation.</summary>
    public class ZvKeyring
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        private List<ZvKey> Keys { get; set; } = new();

        [NotMapped]
        public object Items => Keys;

        public void Add(ZvKey key) => Keys.Add(key);
    }

    /// <summary>Control: the same getter typed as the element list, which the policy reads.</summary>
    public class ZvTypedKeyring
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        private List<ZvTypedKey> Keys { get; set; } = new();

        [NotMapped]
        public IReadOnlyList<ZvTypedKey> Items => Keys;

        public void Add(ZvTypedKey key) => Keys.Add(key);
    }

    public class ZvKey
    {
        public int Id { get; set; }

        public int ZvKeyringId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Secret { get; set; }
    }

    public class ZvTypedKey
    {
        public int Id { get; set; }

        public int ZvTypedKeyringId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>
    /// An entity whose unmapped member the application fills once EF Core has read it, beside a top-level denial.
    /// </summary>
    public class ZvNotedEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        [NotMapped]
        public object? Note { get; set; }
    }

    /// <summary>The same, one level down: an owned member whose unmapped member the application fills.</summary>
    public class ZvNoteHolder
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        [DwDenied]
        public string? Actor { get; set; }

        public ZvNoteMeta Meta { get; set; } = new();
    }

    public class ZvNoteMeta
    {
        public string Tag { get; set; } = string.Empty;

        [NotMapped]
        public object? Note { get; set; }
    }

    public sealed class ZvObjectContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZvObjectContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZvEvent> Events => Set<ZvEvent>();

        public DbSet<ZvAuditedEvent> AuditedEvents => Set<ZvAuditedEvent>();

        public DbSet<ZvEnvelope> Envelopes => Set<ZvEnvelope>();

        public DbSet<ZvKeyring> Keyrings => Set<ZvKeyring>();

        public DbSet<ZvTypedKeyring> TypedKeyrings => Set<ZvTypedKeyring>();

        public DbSet<ZvNotedEvent> NotedEvents => Set<ZvNotedEvent>();

        public DbSet<ZvNoteHolder> NoteHolders => Set<ZvNoteHolder>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        private static string Write(object? value) => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);

        private static object? Read(string text) => JsonSerializer.Deserialize<ZvCardIssued>(text, (JsonSerializerOptions?)null);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZvEvent>().Property(e => e.Payload).HasConversion(v => Write(v), s => Read(s));
            model.Entity<ZvAuditedEvent>().Property(e => e.Payload).HasConversion(v => Write(v), s => Read(s));
            model.Entity<ZvEnvelope>().OwnsOne(e => e.Meta, m => m.Property(x => x.Payload).HasConversion(v => Write(v), s => Read(s)));
            model.Entity<ZvNoteHolder>().OwnsOne(h => h.Meta);
            model.Entity<ZvKeyring>(b =>
            {
                b.HasMany<ZvKey>("Keys").WithOne().HasForeignKey(k => k.ZvKeyringId);
                b.Navigation("Keys").AutoInclude();
            });
            model.Entity<ZvTypedKeyring>(b =>
            {
                b.HasMany<ZvTypedKey>("Keys").WithOne().HasForeignKey(k => k.ZvTypedKeyringId);
                b.Navigation("Keys").AutoInclude();
            });
        }
    }

    // ============================================================================ members that can hold any object: tests

    /// <summary>
    /// A member typed object asks for no projection. What EF Core materializes holds no application object, but a
    /// converted column can, so once a projection is built for another reason it is left out rather than kept
    /// whole; an unmapped getter typed as the list it hands out is read as that list.
    /// </summary>
    public sealed class ReviewObjectMemberTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZvObjectContext _db;

        public ReviewObjectMemberTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZvObjectContext(_connection);
            _db.Database.EnsureCreated();

            _db.Events.Add(new ZvEvent { Kind = "CardIssued", Payload = new ZvCardIssued { Label = "visa", Pan = "event-pan-secret" } });
            _db.AuditedEvents.Add(new ZvAuditedEvent
            {
                Kind = "CardIssued", Actor = "actor-secret", Payload = new ZvCardIssued { Label = "visa", Pan = "audited-pan-secret" }
            });
            _db.Envelopes.Add(new ZvEnvelope
            {
                Kind = "CardIssued", Actor = "actor-secret",
                Meta = new ZvMeta { Tag = "t", Payload = new ZvCardIssued { Label = "visa", Pan = "owned-pan-secret" } }
            });

            ZvKeyring keyring = new() { Name = "K1" };
            keyring.Add(new ZvKey { Label = "front", Secret = "key-secret" });
            _db.Keyrings.Add(keyring);

            ZvTypedKeyring typed = new() { Name = "K2" };
            typed.Add(new ZvTypedKey { Label = "back", Secret = "typed-key-secret" });
            _db.TypedKeyrings.Add(typed);

            _db.NotedEvents.Add(new ZvNotedEvent { Kind = "Noted", Actor = "noted-actor-secret" });
            _db.NoteHolders.Add(new ZvNoteHolder { Kind = "Held", Actor = "holder-actor-secret", Meta = new ZvNoteMeta { Tag = "t1" } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is ValueType || current is MemberInfo)
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

                foreach (PropertyInfo property in current.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    try
                    {
                        pending.Push(property.GetValue(current));
                    }
                    catch
                    {
                        // A getter that throws holds nothing readable.
                    }
                }
            }

            return false;
        }

        private static object? SafeRead(Func<object?> read)
        {
            try
            {
                return read();
            }
            catch (PolicyException refusal) when (refusal.ErrorCode is PolicyErrorCode.FieldDeniedForSelect or PolicyErrorCode.AllSelectsDenied)
            {
                return null;
            }
        }

        private string Describe<T>(PolicyQueryable<T> guarded, object? data) where T : class =>
            "sent=" + JsonSerializer.Serialize(data) + " decisions=["
            + string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed)
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>()) + "]";

        // ------------------------------------------------------------------------ a converted column typed object

        /// <summary>
        /// 3.1.0 synthesized a projection of scalars only whenever a top-level field was denied, so this column was
        /// dropped with the denied Actor. Now a synthesized projection keeps an entity's object column whole.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_D5_a_converted_object_column_beside_a_top_level_denial(DwTier tier)
        {
            Assert.True(Holds(_db.AuditedEvents.AsNoTracking().ToList(), "audited-pan-secret"));

            PolicyQueryable<ZvAuditedEvent> guarded = Guard(_db.AuditedEvents, tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine(Describe(guarded, data));

            Assert.False(Holds(data, "actor-secret"));
            Assert.False(Holds(data, "audited-pan-secret"));
        }

        /// <summary>An owned member holding the converted column, beside a top-level denial.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_D5_an_owned_member_holding_a_converted_object_column_beside_a_top_level_denial(DwTier tier)
        {
            Assert.True(Holds(_db.Envelopes.AsNoTracking().ToList(), "owned-pan-secret"));

            PolicyQueryable<ZvEnvelope> guarded = Guard(_db.Envelopes, tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine(Describe(guarded, data));

            Assert.False(Holds(data, "owned-pan-secret"));
        }

        // ------------------------------------------------------------------------ an unmapped getter typed object

        [Fact]
        public void Zv_D6_control_an_unmapped_getter_typed_as_the_list_is_read()
        {
            Assert.True(Holds(_db.TypedKeyrings.AsNoTracking().ToList(), "typed-key-secret"));

            PolicyQueryable<ZvTypedKeyring> guarded = Guard(_db.TypedKeyrings);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine(Describe(guarded, data));

            Assert.False(Holds(data, "typed-key-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_D5_an_unmapped_member_the_application_fills_beside_a_top_level_denial(DwTier tier)
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.Tracked += (_, tracked) =>
            {
                if (tracked.FromQuery && tracked.Entry.Entity is ZvNotedEvent noted)
                {
                    noted.Note = new ZvCardIssued { Label = "memo", Pan = "unmapped-pan-secret" };
                }
            };

            Assert.True(Holds(_db.NotedEvents.ToList(), "unmapped-pan-secret"));

            _db.ChangeTracker.Clear();

            object? data = Guard(_db.NotedEvents, tier).ToList(new Filter()).Data;

            Assert.False(Holds(data, "noted-actor-secret"));
            Assert.False(Holds(data, "unmapped-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_D5_an_owned_member_whose_unmapped_member_the_application_fills(DwTier tier)
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.Tracked += (_, tracked) =>
            {
                if (tracked.FromQuery && tracked.Entry.Entity is ZvNoteMeta meta)
                {
                    meta.Note = new ZvCardIssued { Label = "memo", Pan = "owned-unmapped-secret" };
                }
            };

            Assert.True(Holds(_db.NoteHolders.ToList(), "owned-unmapped-secret"));

            _db.ChangeTracker.Clear();

            PolicyQueryable<ZvNoteHolder> guarded = Guard(_db.NoteHolders, tier);
            object? data = guarded.ToList(new Filter()).Data;

            _out.WriteLine(Describe(guarded, data));

            Assert.False(Holds(data, "holder-actor-secret"));
            Assert.False(Holds(data, "owned-unmapped-secret"));
            Assert.Contains("t1", JsonSerializer.Serialize(data));
        }
    }
}
