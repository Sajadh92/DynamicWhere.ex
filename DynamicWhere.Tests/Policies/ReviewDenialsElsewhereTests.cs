using System.Collections;
using System.Reflection;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Storage;
using DynamicWhere.ex.Policies.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ denials on other declarations: models

    // ---- A1: a generic interface, implemented by an open generic class that denies the member ----------

    public interface IZvHolder<T>
    {
        int Id { get; }

        string? Secret { get; }
    }

    public class ZvHolderImpl<T> : IZvHolder<T>
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>Control: the same interface shape, implemented by a closed class.</summary>
    public interface IZvClosedHolder<T>
    {
        int Id { get; }

        string? Secret { get; }
    }

    public class ZvIntClosedHolder : IZvClosedHolder<int>
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>Control: a plain interface implemented by an open generic class.</summary>
    public interface IZvPlainHolder
    {
        int Id { get; }

        string? Secret { get; }
    }

    public class ZvGenPlainHolder<T> : IZvPlainHolder
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    public class ZvHolderRow
    {
        public int Id { get; set; }

        public IZvHolder<int>? Holder { get; set; }
    }

    // ---- A1b: a generic base class whose open generic subclass overrides and denies ---------------------

    public class ZvBox<T>
    {
        public int Id { get; set; }

        public virtual string? Code { get; set; }
    }

    public class ZvSecretBox<T> : ZvBox<T>
    {
        [DwDenied]
        public override string? Code
        {
            get => base.Code;
            set => base.Code = value;
        }
    }

    // ---- A2: an explicit interface implementation that denies the member --------------------------------

    public interface IZvLocker
    {
        int Id { get; }

        string? Pin { get; }
    }

    public class ZvLocker : IZvLocker
    {
        private string? _pin;

        public int Id { get; set; }

        [DwDenied]
        string? IZvLocker.Pin => _pin;

        public void Seal(string pin) => _pin = pin;
    }

    public class ZvLockerRoom
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, IZvLocker> Lockers { get; set; } = new();
    }

    // ---- A3: an interface member's denial, on a member a subtype of the queried type implements ---------

    public interface IZvTaxed
    {
        [DwDenied]
        string? TaxId { get; }
    }

    public class ZvFirm
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvTaxedFirm : ZvFirm, IZvTaxed
    {
        public string? TaxId { get; set; }
    }

    /// <summary>Control: a subtype with the attribute on its own member, on a base of its own.</summary>
    public class ZvFirmB
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvDirectTaxedFirm : ZvFirmB
    {
        [DwDenied]
        public string? TaxNumber { get; set; }
    }

    public class ZvFirmHolder
    {
        public int Id { get; set; }

        public ZvFirm? Firm { get; set; }
    }

    // ---- A4: an interface member's denial, reached through a framework generic -------------------------

    public interface IZvCardish
    {
        [DwDenied]
        string? Pan { get; }
    }

    public class ZvCardImpl : IZvCardish
    {
        public string Label { get; set; } = string.Empty;

        public string? Pan { get; set; }
    }

    public class ZvWallet
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public Dictionary<string, ZvCardImpl> Cards { get; set; } = new();
    }

    public class ZvCardDirect
    {
        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZvWalletDirect
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public Dictionary<string, ZvCardDirect> Cards { get; set; } = new();
    }

    // ---- A5: an override of the setter alone ----------------------------------------------------------

    public class ZvGadget
    {
        public int Id { get; set; }

        public virtual string? Code { get; set; }
    }

    public class ZvSetOnlyGadget : ZvGadget
    {
        [DwDenied]
        public override string? Code
        {
            set => base.Code = value;
        }
    }

    // ---- A6: a member hidden with new rather than overridden --------------------------------------------

    public class ZvDevice
    {
        public int Id { get; set; }

        public string? Serial { get; set; }
    }

    public class ZvHiddenDevice : ZvDevice
    {
        [DwDenied]
        public new string? Serial
        {
            get => base.Serial;
            set => base.Serial = value;
        }
    }

    /// <summary>A member hidden with new that keeps its own value, as new usually does.</summary>
    public class ZvDevice2
    {
        public int Id { get; set; }

        public string? Serial { get; set; }
    }

    public class ZvHiddenDevice2 : ZvDevice2
    {
        [DwDenied]
        public new string? Serial { get; set; }
    }

    // ---- A7: an override two levels down, of an abstract member ----------------------------------------

    public abstract class ZvInstrument
    {
        public int Id { get; set; }

        public abstract string? Code { get; set; }
    }

    public class ZvMidInstrument : ZvInstrument
    {
        public override string? Code { get; set; }
    }

    public class ZvLeafInstrument : ZvMidInstrument
    {
        [DwDenied]
        public override string? Code
        {
            get => base.Code;
            set => base.Code = value;
        }
    }

    // ---- A8: an overridable and a sealed denial on a subtype's overrides --------------------------------

    public class ZvMeter
    {
        public int Id { get; set; }

        public virtual string? Reading { get; set; }

        public virtual string? Serial { get; set; }
    }

    public class ZvSmartMeter : ZvMeter
    {
        [DwNoWhere(Overridable = true)]
        public override string? Reading
        {
            get => base.Reading;
            set => base.Reading = value;
        }

        [DwNoWhere]
        public override string? Serial
        {
            get => base.Serial;
            set => base.Serial = value;
        }
    }

    public class ZvAnalogMeter : ZvMeter
    {
    }

    // ---- A9: a transform on a subtype's override, which is documented as read from the declaration walked

    public class ZvContactCard
    {
        public int Id { get; set; }

        public virtual string? Phone { get; set; }
    }

    public class ZvMaskedContactCard : ZvContactCard
    {
        [DwMask(MaskStrategy.Full)]
        public override string? Phone
        {
            get => base.Phone;
            set => base.Phone = value;
        }
    }

    // ---- A10: a rule on a subtype's member through a base-typed member, spelled in another letter case

    public class ZvKennel
    {
        public int Id { get; set; }

        public ZvKennelPet? Pet { get; set; }
    }

    public class ZvKennelPet
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ZvKennelHamster : ZvKennelPet
    {
        public string? Tag { get; set; }
    }

    // ---- B1: siblings, and a concrete type's own subtype ----------------------------------------------

    public class ZvCar
    {
        public int Id { get; set; }

        public virtual string? Plate { get; set; }
    }

    public class ZvArmoredCar : ZvCar
    {
        [DwDenied]
        public override string? Plate
        {
            get => base.Plate;
            set => base.Plate = value;
        }
    }

    public class ZvTaxi : ZvCar
    {
    }

    public class ZvLimo : ZvTaxi
    {
        [DwNoOrder]
        public override string? Plate
        {
            get => base.Plate;
            set => base.Plate = value;
        }
    }

    // ---- B2: a widely shared interface, one implementer denies -------------------------------------------

    public interface IZvNamed
    {
        string Name { get; }
    }

    public class ZvCity : IZvNamed
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvSpy : IZvNamed
    {
        public int Id { get; set; }

        [DwDenied]
        public string Name { get; set; } = string.Empty;
    }

    public class ZvPin
    {
        public int Id { get; set; }

        public IZvNamed? Place { get; set; }
    }

    // ---- B3: the startup scan over a default order a subtype's override denies overridably -------------

    [DwEntity(DefaultOrder = "Rank")]
    public class ZvRanked
    {
        public int Id { get; set; }

        public virtual int Rank { get; set; }
    }

    public class ZvSecretlyRanked : ZvRanked
    {
        [DwNoOrder(Overridable = true)]
        public override int Rank
        {
            get => base.Rank;
            set => base.Rank = value;
        }
    }

    /// <summary>Control: the same denial on the type's own member, which the scan reads as a warning.</summary>
    [DwEntity(DefaultOrder = "Rank")]
    public class ZvOwnRanked
    {
        public int Id { get; set; }

        [DwNoOrder(Overridable = true)]
        public int Rank { get; set; }
    }

    // ---- A3 on EF Core: a TPH root whose derived entity implements the interface ------------------------

    public class ZvEfFirm
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvEfTaxedFirm : ZvEfFirm, IZvTaxed
    {
        public string? TaxId { get; set; }
    }

    /// <summary>An entity implementing the shared interface one unrelated DTO denies.</summary>
    public class ZvEfCity : IZvNamed
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>An entity the model maps alone.</summary>
    public class ZvEfVehicle
    {
        public int Id { get; set; }

        public virtual string? Code { get; set; }
    }

    /// <summary>A class the model does not map, which EF Core can never materialize for ZvEfVehicle's set.</summary>
    public class ZvVehicleView : ZvEfVehicle
    {
        [DwDenied]
        public override string? Code
        {
            get => base.Code;
            set => base.Code = value;
        }
    }

    public sealed class ZvDenialsContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZvDenialsContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZvEfFirm> Firms => Set<ZvEfFirm>();

        public DbSet<ZvEfCity> Cities => Set<ZvEfCity>();

        public DbSet<ZvEfVehicle> Vehicles => Set<ZvEfVehicle>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) => model.Entity<ZvEfTaxedFirm>();
    }

    // ============================================================================ denials on other declarations: tests

    /// <summary>
    /// The denials read from another declaration of a member: an interface member, an implementation, an override
    /// and a member hidden with new, through every path a row reaches them by.
    /// </summary>
    public sealed class ReviewDenialsElsewhereTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZvDenialsContext _db;

        public ReviewDenialsElsewhereTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZvDenialsContext(_connection);
            _db.Database.EnsureCreated();
            _db.Firms.Add(new ZvEfTaxedFirm { Name = "Acme", TaxId = "ef-tax-secret" });
            _db.Firms.Add(new ZvEfFirm { Name = "Plain" });
            _db.Cities.Add(new ZvEfCity { Name = "Basra" });
            _db.Vehicles.Add(new ZvEfVehicle { Code = "V-1" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict, params IDwPolicyProvider[] more)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray()));

        private static Filter Where(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { value } } }
            }
        };

        private static Filter OrderedBy(string field) => new() { Orders = new List<OrderBy> { new() { Field = field } } };

        private static Summary GroupedBy(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { field },
                AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
            }
        };

        /// <summary>The error code a guarded call refused with, or null when it ran.</summary>
        private string Code(Action run)
        {
            try
            {
                run();

                return "ran";
            }
            catch (PolicyException refusal)
            {
                return refusal.ErrorCode.ToString();
            }
            catch (Exception other)
            {
                return $"{other.GetType().Name}: {other.Message.Split('\n')[0]}";
            }
        }

        private static IEnumerable<PolicyFragment> Denials(Type type, string path) =>
            new AttributePolicyProvider().GetFragments(type, new DwPolicyContext())
                .Where(f => f.Effect == PolicyEffect.Deny && string.Equals(f.FieldPath, path, StringComparison.Ordinal));

        /// <summary>Everything reachable from a value, read by runtime type, including explicit interface members.</summary>
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

                if (current is IDictionary map)
                {
                    foreach (object? item in map.Values)
                    {
                        pending.Push(item);
                    }

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

                foreach (PropertyInfo property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
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
            catch (LogicException)
            {
                // The core refused to build the projection: nothing returned.
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------------ A1

        [Fact]
        public void Zv_A1_a_generic_interface_implemented_by_an_open_generic_class_carries_its_denial()
        {
            _out.WriteLine("IZvHolder<int>.Secret denials: " + Denials(typeof(IZvHolder<int>), "Secret").Count());
            _out.WriteLine("control IZvClosedHolder<int>.Secret denials: " + Denials(typeof(IZvClosedHolder<int>), "Secret").Count());
            _out.WriteLine("control IZvPlainHolder.Secret denials: " + Denials(typeof(IZvPlainHolder), "Secret").Count());

            Assert.NotEmpty(Denials(typeof(IZvClosedHolder<int>), "Secret"));
            Assert.NotEmpty(Denials(typeof(IZvPlainHolder), "Secret"));
            Assert.NotEmpty(Denials(typeof(IZvHolder<int>), "Secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A1_filtering_a_generic_interface_on_the_member_its_open_generic_implementation_denies(DwTier tier)
        {
            IZvHolder<int>[] rows = { new ZvHolderImpl<int> { Id = 1, Secret = "generic-iface-secret" } };

            string where = Code(() => Guard(rows.AsQueryable(), tier).ToList(Where("Secret", "generic-iface-secret")));
            string order = Code(() => Guard(rows.AsQueryable(), tier).ToList(OrderedBy("Secret")));
            string group = Code(() => Guard(rows.AsQueryable(), tier).ToList(GroupedBy("Secret")));

            _out.WriteLine($"where={where} order={order} group={group}");

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), where);
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForGroup), group);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A1_rows_read_through_a_generic_interface_whose_open_generic_implementation_denies(DwTier tier)
        {
            IZvHolder<int>[] rows = { new ZvHolderImpl<int> { Id = 1, Secret = "generic-iface-secret" } };

            PolicyQueryable<IZvHolder<int>> guarded = Guard(rows.AsQueryable(), tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("result: " + JsonSerializer.Serialize(data) + " decisions: "
                           + string.Join(" | ", guarded.LastTrace?.Decisions.Select(d => $"{d.FieldPath} {d.Action}") ?? Array.Empty<string>()));

            Assert.False(Holds(data, "generic-iface-secret"));
        }

        [Fact]
        public void Zv_A1_a_member_typed_as_the_generic_interface()
        {
            ZvHolderRow[] rows = { new() { Id = 1, Holder = new ZvHolderImpl<int> { Id = 2, Secret = "generic-member-secret" } } };

            string where = Code(() => Guard(rows.AsQueryable()).ToList(Where("Holder.Secret", "generic-member-secret")));
            object? whole = SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data);

            _out.WriteLine($"where on Holder.Secret={where}; whole result holds it={Holds(whole, "generic-member-secret")}");

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), where);
            Assert.False(Holds(whole, "generic-member-secret"));
        }

        [Fact]
        public void Zv_A1_control_a_closed_implementation_of_the_generic_interface_is_refused()
        {
            IZvClosedHolder<int>[] rows = { new ZvIntClosedHolder { Id = 1, Secret = "closed-secret" } };

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(rows.AsQueryable()).ToList(Where("Secret", "closed-secret"))));
        }

        [Fact]
        public void Zv_A1b_a_generic_base_class_whose_open_generic_subclass_overrides_and_denies()
        {
            ZvBox<int>[] rows = { new ZvSecretBox<int> { Id = 1, Code = "box-secret" } };

            _out.WriteLine("ZvBox<int>.Code denials: " + Denials(typeof(ZvBox<int>), "Code").Count());

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(rows.AsQueryable()).ToList(Where("Code", "box-secret"))));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "box-secret"));
        }

        // ------------------------------------------------------------------------------------------------ A2

        [Fact]
        public void Zv_A2_an_explicit_interface_implementation_denies_the_interface_path()
        {
            ZvLocker locker = new() { Id = 1 };
            locker.Seal("pin-secret");
            IZvLocker[] rows = { locker };

            _out.WriteLine("IZvLocker.Pin denials: " + Denials(typeof(IZvLocker), "Pin").Count());

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(rows.AsQueryable()).ToList(Where("Pin", "pin-secret"))));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A2_an_explicit_implementation_held_in_a_framework_generic(DwTier tier)
        {
            ZvLocker locker = new() { Id = 1 };
            locker.Seal("pin-secret");
            ZvLockerRoom[] rows = { new() { Id = 1, Name = "R", Lockers = { ["a"] = locker } } };

            object? unguarded = rows;
            Assert.Contains("pin-secret", JsonSerializer.Serialize(unguarded));

            object? guarded = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data);
            object? named = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter { Selects = new List<string> { "Id", "Lockers" } }).Data);

            _out.WriteLine("whole: " + JsonSerializer.Serialize(guarded));
            _out.WriteLine("named: " + JsonSerializer.Serialize(named));

            Assert.DoesNotContain("pin-secret", JsonSerializer.Serialize(guarded));
            Assert.DoesNotContain("pin-secret", JsonSerializer.Serialize(named));
        }

        // ------------------------------------------------------------------------------------------------ A3

        [Fact]
        public void Zv_A3_control_the_class_that_implements_the_interface_is_policed()
        {
            ZvTaxedFirm[] rows = { new() { Id = 1, Name = "Acme", TaxId = "tax-secret" } };

            Assert.NotEmpty(Denials(typeof(ZvTaxedFirm), "TaxId"));
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(rows.AsQueryable()).ToList(Where("TaxId", "tax-secret"))));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "tax-secret"));
        }

        [Fact]
        public void Zv_A3_control_a_subtypes_own_attribute_through_the_base_type()
        {
            ZvFirmB[] rows = { new ZvDirectTaxedFirm { Id = 1, Name = "Acme", TaxNumber = "direct-tax-secret" } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "direct-tax-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A3_rows_in_memory_read_through_a_base_type_whose_subtype_implements_the_denying_interface(DwTier tier)
        {
            ZvFirm[] rows = { new ZvTaxedFirm { Id = 1, Name = "Acme", TaxId = "tax-secret" } };

            PolicyQueryable<ZvFirm> guarded = Guard(rows.AsQueryable(), tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("types: " + string.Join(",", ((IEnumerable?)data ?? Array.Empty<object>()).Cast<object>().Select(r => r.GetType().Name)));

            Assert.False(Holds(data, "tax-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A3_a_base_typed_member_holding_the_subtype(DwTier tier)
        {
            ZvFirmHolder[] rows = { new() { Id = 1, Firm = new ZvTaxedFirm { Id = 2, Name = "Acme", TaxId = "tax-secret" } } };

            object? whole = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data);
            object? named = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter { Selects = new List<string> { "Id", "Firm" } }).Data);

            Assert.False(Holds(whole, "tax-secret"));
            Assert.False(Holds(named, "tax-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A3_an_entity_hierarchy_root_whose_derived_entity_implements_the_denying_interface(DwTier tier)
        {
            Assert.True(Holds(_db.Firms.AsNoTracking().ToList(), "ef-tax-secret"));

            PolicyQueryable<ZvEfFirm> guarded = Guard(_db.Firms, tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("types: " + string.Join(",", ((IEnumerable?)data ?? Array.Empty<object>()).Cast<object>().Select(r => r.GetType().Name)));

            Assert.False(Holds(data, "ef-tax-secret"));
            Assert.False(Holds(SafeRead(() => Guard(_db.Firms, tier).ToListDynamic(new Filter()).Data), "ef-tax-secret"));
        }

        [Fact]
        public void Zv_A3_control_the_derived_entity_queried_itself_is_policed()
        {
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(_db.Firms.OfType<ZvEfTaxedFirm>()).ToList(Where("TaxId", "x"))));
            Assert.False(Holds(SafeRead(() => Guard(_db.Firms.OfType<ZvEfTaxedFirm>()).ToList(new Filter()).Data), "ef-tax-secret"));
        }

        // ------------------------------------------------------------------------------------------------ A4

        [Fact]
        public void Zv_A4_control_a_framework_generic_of_a_class_that_denies_its_own_member()
        {
            ZvWalletDirect[] rows = { new() { Id = 1, Owner = "O", Cards = { ["a"] = new ZvCardDirect { Label = "visa", Pan = "direct-pan-secret" } } } };

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data), "direct-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A4_a_framework_generic_of_a_class_whose_interface_denies_the_member(DwTier tier)
        {
            ZvWallet[] rows = { new() { Id = 1, Owner = "O", Cards = { ["a"] = new ZvCardImpl { Label = "visa", Pan = "iface-pan-secret" } } } };

            object? whole = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data);
            object? named = SafeRead(() => Guard(rows.AsQueryable(), tier).ToList(new Filter { Selects = new List<string> { "Id", "Cards" } }).Data);

            _out.WriteLine("whole: " + JsonSerializer.Serialize(whole));
            _out.WriteLine("named: " + JsonSerializer.Serialize(named));

            Assert.False(Holds(whole, "iface-pan-secret"));
            Assert.False(Holds(named, "iface-pan-secret"));
        }

        // ------------------------------------------------------------------------------------------------ A5

        [Fact]
        public void Zv_A5_an_override_of_the_setter_alone()
        {
            PropertyInfo declared = typeof(ZvSetOnlyGadget).GetProperty("Code")!;

            _out.WriteLine($"ZvSetOnlyGadget.Code: declaring={declared.DeclaringType!.Name} canRead={declared.CanRead}");
            _out.WriteLine("ZvSetOnlyGadget.Code denials: " + Denials(typeof(ZvSetOnlyGadget), "Code").Count());
            _out.WriteLine("ZvGadget.Code denials: " + Denials(typeof(ZvGadget), "Code").Count());

            ZvGadget[] rows = { new ZvSetOnlyGadget { Id = 1, Code = "setter-secret" } };
            ZvSetOnlyGadget[] own = { new() { Id = 1, Code = "setter-secret" } };

            string direct = Code(() => Guard(own.AsQueryable()).ToList(Where("Code", "setter-secret")));
            string through = Code(() => Guard(rows.AsQueryable()).ToList(Where("Code", "setter-secret")));

            _out.WriteLine($"queried as the subtype: {direct}; through the base type: {through}");

            object? data = SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data);
            string sent = JsonSerializer.Serialize(data);

            // The runtime type's PropertyInfo has no getter, so a reflection walk misses it; a serializer does not.
            _out.WriteLine("through the base type, the result sent: " + sent);

            // The same member of the same rows: whatever the subtype says, the base path should say too.
            Assert.Equal(direct, through);
            Assert.DoesNotContain("setter-secret", sent);
        }

        // ------------------------------------------------------------------------------------------------ A6

        [Fact]
        public void Zv_A6_a_member_hidden_with_new_denies_the_base_path()
        {
            ZvDevice[] rows = { new ZvHiddenDevice { Id = 1, Serial = "hidden-secret" } };
            ZvHiddenDevice[] own = { new() { Id = 1, Serial = "hidden-secret" } };

            string direct = Code(() => Guard(own.AsQueryable()).ToList(Where("Serial", "hidden-secret")));
            string through = Code(() => Guard(rows.AsQueryable()).ToList(Where("Serial", "hidden-secret")));
            object? data = SafeRead(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data);

            _out.WriteLine($"queried as the subtype: {direct}; through the base type: {through}; result holds: {Holds(data, "hidden-secret")}");

            // Whether the new member reads the one it hides cannot be told, so the base path is denied too.
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), direct);
            Assert.Equal(direct, through);
            Assert.False(Holds(data, "hidden-secret"));
        }

        [Fact]
        public void Zv_A6_a_member_hidden_with_new_that_keeps_its_own_value()
        {
            ZvDevice2[] rows = { new ZvHiddenDevice2 { Id = 1, Serial = "hidden-own-secret" } };
            ((ZvDevice2)rows[0]).Serial = "public-serial";

            PolicyQueryable<ZvDevice2> guarded = Guard(rows.AsQueryable());
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("types: " + string.Join(",", ((IEnumerable?)data ?? Array.Empty<object>()).Cast<object>().Select(r => r.GetType().Name))
                           + "; decisions: " + string.Join(" | ", guarded.LastTrace?.Decisions.Select(d => $"{d.FieldPath} {d.Action}") ?? Array.Empty<string>()));

            // A row of the subtype still holds the hidden member's own value; a runtime-type serializer writes it.
            Assert.False(Holds(data, "hidden-own-secret"));
        }

        // ------------------------------------------------------------------------------------------------ A7

        [Fact]
        public void Zv_A7_an_override_two_levels_down_of_an_abstract_member()
        {
            ZvInstrument[] root = { new ZvLeafInstrument { Id = 1, Code = "leaf-secret" } };
            ZvMidInstrument[] mid = { new ZvLeafInstrument { Id = 1, Code = "leaf-secret" } };

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(root.AsQueryable()).ToList(Where("Code", "leaf-secret"))));
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(mid.AsQueryable()).ToList(Where("Code", "leaf-secret"))));
            Assert.False(Holds(SafeRead(() => Guard(mid.AsQueryable()).ToList(new Filter()).Data), "leaf-secret"));
        }

        // ------------------------------------------------------------------------------------------------ A8

        [Fact]
        public void Zv_A8_a_rule_lifts_an_overridable_subtype_denial_and_not_a_sealed_one()
        {
            ZvMeter[] rows = { new ZvSmartMeter { Id = 1, Reading = "r", Serial = "s" } };
            FakePolicyProvider allow = new FakePolicyProvider()
                .Add("Reading", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicGlobal)
                .Add("Serial", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);

            string readingWithout = Code(() => Guard(rows.AsQueryable()).ToList(Where("Reading", "r")));
            string readingLifted = Code(() => Guard(rows.AsQueryable(), DwTier.Strict, allow).ToList(Where("Reading", "r")));
            string serialLifted = Code(() => Guard(rows.AsQueryable(), DwTier.Strict, allow).ToList(Where("Serial", "s")));

            _out.WriteLine($"Reading unlifted={readingWithout} lifted={readingLifted}; Serial with an allow rule={serialLifted}");

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), readingWithout);
            Assert.Equal("ran", readingLifted);
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), serialLifted);
        }

        [Fact]
        public void Zv_A8_the_store_refuses_a_rule_on_the_base_path_a_subtype_seals_and_not_on_a_sibling()
        {
            PolicyRule onBase = new(DwSubjectKind.Global, null, typeof(ZvMeter).FullName!, "Serial", PolicyFeature.Where, PolicyEffect.Allow);
            PolicyRule onSibling = new(DwSubjectKind.Global, null, typeof(ZvAnalogMeter).FullName!, "Serial", PolicyFeature.Where, PolicyEffect.Allow);
            PolicyRule overridable = new(DwSubjectKind.Global, null, typeof(ZvMeter).FullName!, "Reading", PolicyFeature.Where, PolicyEffect.Allow);

            Func<string, Type?> resolve = name => name == typeof(ZvMeter).FullName ? typeof(ZvMeter)
                : name == typeof(ZvAnalogMeter).FullName ? typeof(ZvAnalogMeter) : null;

            Exception? baseRefusal = Record.Exception(() => SealedFields.Refuse(onBase, resolve, "rule"));
            Exception? siblingRefusal = Record.Exception(() => SealedFields.Refuse(onSibling, resolve, "rule"));
            Exception? overridableRefusal = Record.Exception(() => SealedFields.Refuse(overridable, resolve, "rule"));

            _out.WriteLine($"base: {baseRefusal?.Message}");
            _out.WriteLine($"sibling: {siblingRefusal?.Message}");
            _out.WriteLine($"overridable: {overridableRefusal?.Message}");

            Assert.IsType<ArgumentException>(baseRefusal);
            Assert.Null(siblingRefusal);
            Assert.Null(overridableRefusal);
        }

        // ------------------------------------------------------------------------------------------------ A9

        // ------------------------------------------------------------------------------------------------ A10

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_A10_a_rule_on_a_subtype_member_spelled_in_another_case_is_enforced(DwTier tier)
        {
            ZvKennel[] rows = { new() { Id = 1, Pet = new ZvKennelHamster { Name = "Ham", Tag = "case-tag-secret" } } };
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("pet.TAG", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier, rules).ToList(new Filter()).Data), "case-tag-secret"));
            Assert.False(Holds(SafeRead(() => Guard(rows.AsQueryable(), tier, rules)
                .ToList(new Filter { Selects = new List<string> { "Id", "Pet" } }).Data), "case-tag-secret"));
        }

        // ------------------------------------------------------------------------------------------------ B1

        [Fact]
        public void Zv_B1_a_sibling_override_does_not_reach_a_concrete_type_and_its_own_subtype_does()
        {
            ZvTaxi[] taxis = { new() { Id = 1, Plate = "taxi-plate" } };

            Assert.Empty(Denials(typeof(ZvTaxi), "Plate").Where(f => (f.Features & PolicyFeature.Where) != 0));
            Assert.Equal("ran", Code(() => Guard(taxis.AsQueryable()).ToList(Where("Plate", "taxi-plate"))));
            Assert.Equal("ran", Code(() => Guard(taxis.AsQueryable()).ToList(GroupedBy("Plate"))));
            Assert.True(Holds(Guard(taxis.AsQueryable()).ToList(new Filter()).Data, "taxi-plate"));

            // ZvLimo, a subtype of ZvTaxi, denies ordering on Plate: that reaches ZvTaxi's path.
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForOrder), Code(() => Guard(taxis.AsQueryable()).ToList(OrderedBy("Plate"))));

            // ZvArmoredCar, a sibling, denies everything: the base path is denied, ZvTaxi's is not.
            ZvCar[] cars = { new ZvTaxi { Id = 1, Plate = "taxi-plate" } };
            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), Code(() => Guard(cars.AsQueryable()).ToList(Where("Plate", "taxi-plate"))));
        }

        // ------------------------------------------------------------------------------------------------ B2

        [Fact]
        public void Zv_B2_one_implementer_of_a_shared_interface_denies_the_interface_path_for_every_row()
        {
            ZvCity[] cities = { new() { Id = 1, Name = "Basra" } };
            IZvNamed[] named = { new ZvCity { Id = 1, Name = "Basra" } };
            ZvPin[] pins = { new() { Id = 1, Place = new ZvCity { Id = 2, Name = "Basra" } } };

            string city = Code(() => Guard(cities.AsQueryable()).ToList(Where("Name", "Basra")));
            string iface = Code(() => Guard(named.AsQueryable()).ToList(Where("Name", "Basra")));
            string member = Code(() => Guard(pins.AsQueryable()).ToList(Where("Place.Name", "Basra")));
            string whole = Code(() => Guard(pins.AsQueryable()).ToList(new Filter()));

            _out.WriteLine($"class={city} interface={iface} interface-typed member={member} whole row={whole}");

            Assert.Equal("ran", city);
        }

        /// <summary>
        /// An EF Core query over one entity set, read through the interface the entity implements: an unrelated
        /// implementer's denial (ZvSpy, not even an entity) decides the interface path for every row.
        /// </summary>
        [Fact]
        public void Zv_B2_an_entity_set_read_through_a_shared_interface_takes_an_unrelated_implementers_denial()
        {
            IQueryable<IZvNamed> cities = _db.Cities;

            string concrete = Code(() => Guard(_db.Cities).ToList(Where("Name", "Basra")));
            string where = Code(() => Guard(cities).ToList(Where("Name", "Basra")));
            string whole = Code(() => Guard(cities).ToList(new Filter()));
            string dynamic = Code(() => Guard(cities).ToListDynamic(new Filter()));

            _out.WriteLine($"concrete set Where={concrete}; through the interface: Where={where} ToList={whole} ToListDynamic={dynamic}");

            Assert.Equal("ran", concrete);
        }

        /// <summary>
        /// A class the EF Core model does not map overrides and denies an entity's member. EF Core never returns
        /// one from the entity's set, but the walk reads every loaded subtype, so the entity's own path is denied:
        /// a documented limit, which fails closed.
        /// </summary>
        [Fact]
        public void Zv_B5_a_subclass_EF_Core_does_not_map_still_denies_the_entitys_path()
        {
            Assert.Null(_db.Model.FindEntityType(typeof(ZvVehicleView)));

            string where = Code(() => Guard(_db.Vehicles).ToList(Where("Code", "V-1")));
            object? data = SafeRead(() => Guard(_db.Vehicles).ToList(new Filter()).Data);

            _out.WriteLine($"Where={where} whole={JsonSerializer.Serialize(data)}");

            Assert.Equal(nameof(PolicyErrorCode.FieldDeniedForWhere), where);
            Assert.False(Holds(data, "V-1"));
        }

        // ------------------------------------------------------------------------------------------------ B3

        [Fact]
        public void Zv_B3_the_startup_scan_reads_a_subtypes_overridable_denial_of_a_default_order_as_a_warning()
        {
            PolicyModelReport subtype = PolicyModelValidator.Inspect(new[] { typeof(ZvRanked) });
            PolicyModelReport own = PolicyModelValidator.Inspect(new[] { typeof(ZvOwnRanked) });

            _out.WriteLine("subtype's override: errors=[" + string.Join(" | ", subtype.Errors) + "] warnings=[" + string.Join(" | ", subtype.Warnings) + "]");
            _out.WriteLine("own member: errors=[" + string.Join(" | ", own.Errors) + "] warnings=[" + string.Join(" | ", own.Warnings) + "]");

            // The control: an overridable denial on the type's own member is a warning.
            Assert.Empty(own.Errors);
            Assert.NotEmpty(own.Warnings);

            // The same overridable denial on a subtype's override should read the same way.
            Assert.Empty(subtype.Errors);
        }
    }
}
