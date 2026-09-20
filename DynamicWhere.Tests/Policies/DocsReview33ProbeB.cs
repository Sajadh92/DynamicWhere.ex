using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // What the entity row of the source table is actually read from, plus the group floor.
    // =============================================================================================

    public sealed class QpDerivedAndFloorProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _zyConnection;
        private readonly ZyContext _zy;
        private readonly SqliteConnection _qpConnection;
        private readonly QpContext _qp;

        public QpDerivedAndFloorProbe(ITestOutputHelper output)
        {
            _out = output;

            _zyConnection = new SqliteConnection("DataSource=:memory:");
            _zyConnection.Open();
            _zy = new ZyContext(_zyConnection);
            _zy.Database.EnsureCreated();
            _zy.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { En = "Admin" } });
            _zy.Parties.Add(new ZyMerchant { Kind = "merchant", Licence = "L-1", Rating = "A" });
            _zy.SaveChanges();
            _zy.ChangeTracker.Clear();

            _qpConnection = new SqliteConnection("DataSource=:memory:");
            _qpConnection.Open();
            _qp = new QpContext(_qpConnection);
            _qp.Database.EnsureCreated();

            foreach (string code in new[] { "a", "b", "c" })
            {
                _qp.Tickets.Add(new QpTicket { Code = code, CreatedAt = new DateTime(2026, 1, 1), Tag = new QpTag() });
            }

            _qp.SaveChanges();
            _qp.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _zy.Dispose();
            _zyConnection.Dispose();
            _qp.Dispose();
            _qpConnection.Dispose();
        }

        private static bool? Ask(IQueryable source, string path)
        {
            object shape = typeof(RowShape)
                .GetMethod("Of", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                .MakeGenericMethod(source.ElementType)
                .Invoke(null, new object[] { source })!;

            return (bool?)typeof(RowShape)
                .GetMethod("Expresses", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shape, new object[] { path });
        }

        private static string Show(bool? answer) => answer is null ? "null (left alone)" : answer.ToString()!;

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwPolicyOptions options) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                options,
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        /// <summary>
        /// The one claim two tables disagree on: whether a derived type's columns count as members
        /// the queried type can produce.
        /// </summary>
        [Fact]
        public void The_entity_row_reads_the_queried_types_own_model_not_a_derived_types()
        {
            bool derivedMaps = _zy.Model.FindEntityType(typeof(ZyMerchant))!.FindProperty("Licence") is not null;
            bool baseMaps = _zy.Model.FindEntityType(typeof(ZyParty))!.FindProperty("Licence") is not null;

            bool? throughBase = Ask(_zy.Parties, "Licence");
            bool? throughDerived = Ask(_zy.Set<ZyMerchant>(), "Licence");

            _out.WriteLine($"model maps Licence: on ZyMerchant = {derivedMaps}, on ZyParty = {baseMaps}");
            _out.WriteLine($"Expresses(\"Licence\") on DbSet<ZyParty>    = {Show(throughBase)}");
            _out.WriteLine($"Expresses(\"Licence\") on Set<ZyMerchant>() = {Show(throughDerived)}");

            Assert.True(derivedMaps);
            Assert.False(baseMaps);
            Assert.False(throughBase);
            Assert.True(throughDerived);
        }

        /// <summary>The projected rows of the table, read straight off the shape.</summary>
        [Fact]
        public void The_projected_rows_of_the_table()
        {
            IQueryable<ZyRoleRow> built = _zy.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            IQueryable<ZyRoleRow> copied =
                _zy.Roles.Select(role => new ZyRoleRow { Id = role.Id, Code = role.Code, Name = role.Name });

            IQueryable<ZyRoleRow> another = new ZyOwnProvider<ZyRoleRow>(
                new[] { new ZyRole { Id = 1, Code = "admin", Name = new ZyLocalizedText() } }
                    .AsQueryable()
                    .Select(role => new ZyRoleRow { Id = role.Id, Code = role.Code, Name = new ZyLocalizedText { En = role.Name.En } }));

            _out.WriteLine($"a Select that builds the row        Expresses(\"Name.IsEmpty\") = {Show(Ask(built, "Name.IsEmpty"))}");
            _out.WriteLine($"  ... and what it does assign       Expresses(\"Name.En\")      = {Show(Ask(built, "Name.En"))}");
            _out.WriteLine($"a Select that copies the member     Expresses(\"Name.IsEmpty\") = {Show(Ask(copied, "Name.IsEmpty"))}");
            _out.WriteLine($"a projection another provider ran   Expresses(\"Name.IsEmpty\") = {Show(Ask(another, "Name.IsEmpty"))}");
            _out.WriteLine($"an entity, an owned type's getter   Expresses(\"Name.IsEmpty\") = {Show(Ask(_zy.Roles, "Name.IsEmpty"))}");

            Assert.False(Ask(built, "Name.IsEmpty"));
            Assert.True(Ask(built, "Name.En"));
            Assert.False(Ask(copied, "Name.IsEmpty"));
            Assert.Null(Ask(another, "Name.IsEmpty"));
            Assert.False(Ask(_zy.Roles, "Name.IsEmpty"));
        }

        /// <summary>The exact wording the docs quote for the trace.</summary>
        [Fact]
        public void The_trace_reason_is_worded_as_the_docs_quote_it()
        {
            PolicyQueryable<ZyRole> guarded = Guard(_zy.Roles, new DwPolicyOptions { Tier = DwTier.Strict });

            Assert.ThrowsAny<PolicyException>(() => guarded.ToList(new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Name.IsEmpty", DataType = DataType.Boolean, Operator = Operator.Equal, Values = { "false" } } }
                }
            }));

            string reason = guarded.LastTrace!.Decisions
                .Select(decision => decision.Reason)
                .First(text => text is not null && text.Contains("cannot compute"))!;

            _out.WriteLine($"trace reason: {reason}");

            Assert.Equal(
                "the member exists on the type and the query cannot compute it, so it is refused as an unknown name is",
                reason);
        }

        /// <summary>LastTrace lands on the handle the method was called on, not the one it returns.</summary>
        [Fact]
        public void LastTrace_lands_on_the_handle_the_method_was_called_on()
        {
            PolicyQueryable<QpTicket> guarded = Guard(_qp.Tickets, new DwPolicyOptions { Tier = DwTier.Strict });

            PolicyQueryable<QpTicket> returned = guarded.Where(new Condition
            {
                Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" }
            });

            _out.WriteLine(
                $"called-on handle LastTrace = {(guarded.LastTrace is null ? "null" : "set")}; "
                + $"returned handle LastTrace = {(returned.LastTrace is null ? "null" : "set")}");

            Assert.NotNull(guarded.LastTrace);
            Assert.Null(returned.LastTrace);
        }

        /// <summary>The floor the three pages now lead with: on at 5, silent, and never unguarded.</summary>
        [Fact]
        public void The_group_floor_is_on_at_five_and_silent()
        {
            static Summary Ask() => new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            };

            SummaryResult unguarded = _qp.Tickets.ToList(Ask());

            SummaryResult floored = Guard(_qp.Tickets, new DwPolicyOptions { Tier = DwTier.Convenience }).ToList(Ask());

            SummaryResult unfloored = Guard(
                _qp.Tickets,
                new DwPolicyOptions { Tier = DwTier.Convenience, Caps = { MinGroupSize = 1 } }).ToList(Ask());

            _out.WriteLine(
                $"3 groups of 1 row each -> unguarded={unguarded.Data.Count} "
                + $"guarded(default)={floored.Data.Count} guarded(MinGroupSize=1)={unfloored.Data.Count}");
            _out.WriteLine($"guarded(default): TotalCount={floored.TotalCount}, trace on result={(floored.Policy is null ? "null" : "set")}");

            Assert.Equal(3, unguarded.Data.Count);
            Assert.Empty(floored.Data);
            Assert.Equal(3, unfloored.Data.Count);
        }

        /// <summary>
        /// Whether the composable pair is floored too, which decides whether a page describing them
        /// without the floor is a gap.
        /// </summary>
        [Fact]
        public void The_composable_Group_and_Summary_are_floored_the_same_way()
        {
            static GroupBy Grouping() => new()
            {
                Fields = new List<string> { "Code" },
                AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
            };

            static Summary Asked() => new() { GroupBy = Grouping() };

            int unguardedGroup = _qp.Tickets.Group(Grouping()).Cast<dynamic>().Count();

            PolicyQueryable<QpTicket> guarded = Guard(_qp.Tickets, new DwPolicyOptions { Tier = DwTier.Convenience });
            int guardedGroup = guarded.Group(Grouping()).Cast<dynamic>().Count();

            int unguardedSummary = _qp.Tickets.Summary(Asked()).Cast<dynamic>().Count();

            PolicyQueryable<QpTicket> guardedTwo = Guard(_qp.Tickets, new DwPolicyOptions { Tier = DwTier.Convenience });
            int guardedSummary = guardedTwo.Summary(Asked()).Cast<dynamic>().Count();

            _out.WriteLine($"composable .Group   -> unguarded={unguardedGroup} guarded(default floor)={guardedGroup}");
            _out.WriteLine($"composable .Summary -> unguarded={unguardedSummary} guarded(default floor)={guardedSummary}");

            Assert.Equal(3, unguardedGroup);
            Assert.Equal(3, unguardedSummary);
            Assert.Equal(0, guardedGroup);
            Assert.Equal(0, guardedSummary);
        }
    }
}
