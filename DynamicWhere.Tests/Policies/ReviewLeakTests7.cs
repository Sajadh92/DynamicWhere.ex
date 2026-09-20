using System.Collections;
using System.Reflection;
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

namespace DynamicWhere.Tests.Policies
{
    // ---- an interface a subtype adds over a member it inherits ---------------------------------------------

    public interface IZwCarded
    {
        [DwDenied]
        string? Pan { get; }
    }

    public class ZwAccount
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Pan { get; set; }
    }

    /// <summary>Declares nothing of its own: the base class's member implements the denied interface member.</summary>
    public class ZwCardAccount : ZwAccount, IZwCarded
    {
    }

    // ---- an interface implemented by an override whose base declaration denies ----------------------------

    public interface IZwPinned
    {
        int Id { get; }

        string? Pin { get; }
    }

    public class ZwPinBase
    {
        public int Id { get; set; }

        [DwDenied]
        public virtual string? Pin { get; set; }
    }

    public class ZwPinned : ZwPinBase, IZwPinned
    {
        public override string? Pin
        {
            get => base.Pin;
            set => base.Pin = value;
        }
    }

    public class ZwPinHolder
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public List<IZwPinned> Pins { get; set; } = new();
    }

    // ---- one class implementing two instantiations of a generic interface, one of them denied --------------

    public interface IZwSlot<T>
    {
        string? Code { get; }
    }

    public class ZwTwoSlots : IZwSlot<int>, IZwSlot<string>
    {
        public int Id { get; set; }

        public string? Open { get; set; }

        public string? Sealed { get; set; }

        string? IZwSlot<int>.Code => Open;

        [DwDenied]
        string? IZwSlot<string>.Code => Sealed;
    }

    /// <summary>
    /// A denial on another declaration of a member than the one walked: an interface a subtype adds over a
    /// member it inherits, and an implementation that is an override of a denied member.
    /// </summary>
    public class ReviewLeakTests7
    {
        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { value } } }
            }
        };

        /// <summary>True when a value, read by runtime type and through every collection, holds the text.</summary>
        private static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is ValueType)
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

                foreach (PropertyInfo property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length == 0 && property.CanRead)
                    {
                        pending.Push(property.GetValue(current));
                    }
                }
            }

            return false;
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void An_interface_a_subtype_adds_over_an_inherited_member_denies_the_base_path(DwTier tier)
        {
            ZwAccount[] rows = { new ZwCardAccount { Id = 1, Name = "a", Pan = "inherited-pan-secret" } };

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(rows.AsQueryable(), tier).ToList(Where("Pan", "inherited-pan-secret")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            Assert.False(Holds(Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data, "inherited-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void An_implementation_that_overrides_a_denied_member_denies_the_interface_path(DwTier tier)
        {
            IZwPinned[] rows = { new ZwPinned { Id = 1, Pin = "override-pin-secret" } };

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(rows.AsQueryable(), tier).ToList(Where("Pin", "override-pin-secret")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_list_of_the_interface_is_not_returned_holding_the_denied_value(DwTier tier)
        {
            ZwPinHolder[] rows =
            {
                new() { Id = 1, Label = "desk", Pins = { new ZwPinned { Id = 2, Pin = "listed-pin-secret" } } }
            };

            object? data = Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data;

            Assert.False(Holds(data, "listed-pin-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_denial_on_one_instantiation_of_a_generic_interface_leaves_the_other_alone(DwTier tier)
        {
            ZwTwoSlots row = new() { Id = 1, Open = "open", Sealed = "sealed-secret" };
            IZwSlot<int>[] open = { row };
            IZwSlot<string>[] sealedRows = { row };

            Assert.Single(Guard(open.AsQueryable(), tier).ToList(Where("Code", "open")).Data);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(sealedRows.AsQueryable(), tier).ToList(Where("Code", "sealed-secret")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }
    }
}
