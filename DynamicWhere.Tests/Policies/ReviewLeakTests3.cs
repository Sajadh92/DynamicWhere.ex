using System.Collections;
using System.Reflection;
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

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ round 3, batch 3: models

    /// <summary>An application collection that implements only the non-generic IEnumerable.</summary>
    public sealed class ZrLooseBag : IEnumerable
    {
        private readonly List<object> _items = new();

        public void Add(object item) => _items.Add(item);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();
    }

    public class ZrVoucher
    {
        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Code { get; set; }
    }

    public class ZrWallet
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public ZrLooseBag Vouchers { get; set; } = new();
    }

    // ============================================================================ round 3, batch 3: tests

    public sealed class ReviewLeakTests3 : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext _db;

        public ReviewLeakTests3()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ZrProbeContext(_connection);
            _db.Database.EnsureCreated();

            _db.Loans.Add(new ZrLoan { Note = "gen", Org = new ZrGenOrg<int> { Name = "G", Secret = "generic-entity-secret" } });
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

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

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
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------------ P3e

        /// <summary>
        /// An entity query re-rooted by Select reads no model, so its derived types come from the subtype search,
        /// which never lists a generic one.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P3e_an_entity_query_re_rooted_by_Select_holding_a_closed_generic_derived_entity(DwTier tier)
        {
            IQueryable<ZrOrg> source = _db.Loans.Select(l => l.Org!);

            Assert.True(Holds(source.AsNoTracking().ToList(), "generic-entity-secret"));

            Assert.False(Holds(SafeRead(() => Guard(source, tier).ToList(new Filter()).Data), "generic-entity-secret"));
        }

        // ------------------------------------------------------------------------------------------------ P13
    }
}
