using System.Collections;
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
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ an application class that implements IEnumerable

    /// <summary>
    /// An application type with its own policed members that also enumerates, non-generically. It can hold
    /// anything, and its own members are still read.
    /// </summary>
    public class ZvLedgerBag : IEnumerable
    {
        public string Owner { get; set; } = string.Empty;

        [DwDenied]
        public string? AccountNo { get; set; }

        public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    }

    public class ZvBagHolder
    {
        public int Id { get; set; }

        public Dictionary<string, ZvLedgerBag> Bags { get; set; } = new();
    }

    public class ZvBagBase
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvBagSub : ZvBagBase
    {
        public ZvLedgerBag? Bag { get; set; }
    }

    public sealed class ReviewOpaqueCollectionTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewOpaqueCollectionTests(ITestOutputHelper output) => _out = output;

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        /// <summary>Everything reachable, read by runtime type; an IEnumerable is read as its properties too.</summary>
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

                if (current is IEnumerable items && current.GetType().Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
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

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_E1_an_enumerable_application_type_inside_a_framework_generic(DwTier tier)
        {
            ZvBagHolder[] rows = { new() { Id = 1, Bags = { ["a"] = new ZvLedgerBag { Owner = "o", AccountNo = "acct-secret" } } } };

            PolicyQueryable<ZvBagHolder> guarded = Guard(rows.AsQueryable(), tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("holds: " + Holds(data, "acct-secret") + "; decisions: "
                           + string.Join(" | ", guarded.LastTrace?.Decisions.Where(d => d.Action != PolicyAction.Allowed).Select(d => $"{d.FieldPath} {d.Action}") ?? Array.Empty<string>()));

            Assert.False(Holds(data, "acct-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_E2_rows_in_memory_whose_subtype_holds_an_enumerable_application_type(DwTier tier)
        {
            ZvBagBase[] rows = { new ZvBagSub { Id = 1, Name = "n", Bag = new ZvLedgerBag { Owner = "o", AccountNo = "acct-secret" } } };

            PolicyQueryable<ZvBagBase> guarded = Guard(rows.AsQueryable(), tier);
            object? data = SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine("holds: " + Holds(data, "acct-secret") + "; types: "
                           + string.Join(",", ((IEnumerable?)data ?? Array.Empty<object>()).Cast<object>().Select(r => r.GetType().Name)));

            Assert.False(Holds(data, "acct-secret"));
        }
    }
}
