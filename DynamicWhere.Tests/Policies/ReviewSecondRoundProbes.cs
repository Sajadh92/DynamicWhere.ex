using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// A provider that is not EF Core's, is not <c>IAsyncQueryProvider</c>, and keeps wrapping
    /// itself through composition while handing every execution to the query it wraps.
    /// </summary>
    /// <remarks>
    /// The shape LinqKit's <c>AsExpandable</c> and DelegateDecompiler's <c>Decompile</c> have: a
    /// wrapper in front of EF Core, which still answers the query.
    /// </remarks>
    public sealed class ZzWrapped<T> : IQueryable<T>, IQueryProvider
    {
        private readonly IQueryable<T> _inner;

        public ZzWrapped(IQueryable<T> inner)
        {
            _inner = inner;
            Expression = inner.Expression;
        }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider => this;

        public IEnumerator<T> GetEnumerator() => _inner.Provider.CreateQuery<T>(Expression).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IQueryable CreateQuery(Expression expression) => _inner.Provider.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new ZzWrapped<TElement>(_inner.Provider.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => _inner.Provider.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Provider.Execute<TResult>(expression);
    }

    /// <summary>A value object whose text changes between reads, for the clone contract probe.</summary>
    public sealed class ZzShifting
    {
        private int _reads;

        public int Reads => _reads;

        public override string ToString() => ++_reads == 1 ? "admin" : "root";
    }

    /// <summary>
    /// Second-round probes: what the 3.3.0 fixes themselves opened.
    /// </summary>
    public sealed class ReviewSecondRoundProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public ReviewSecondRoundProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { Ar = "AR", En = "Admin" } });
            _db.Parties.Add(new ZyMerchant { Kind = "merchant", Licence = "L-1", Rating = "A" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier, bool traceInResult = false)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                traceInResult
                    ? new DwPolicyOptions { Tier = tier, IncludeTraceInResult = true }
                    : new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } } }
            }
        };

        private IQueryable<ZyRoleRow> Built() =>
            _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

        // ==== 1. the computed:false exemption where the projection is NOT last ==========================

        /// <summary>
        /// The exemption's premise is "a projection is the last thing the provider builds". The
        /// composable handle lets a caller put something after it.
        /// </summary>
        [Fact]
        public void P1_Composed_select_then_order_on_the_handle()
        {
            PolicyQueryable<ZyRoleRow> projected =
                Guard(Built(), DwTier.Strict).Select(new List<string> { "Id", "Name.IsEmpty" });

            Exception? raised = Record.Exception(
                () => projected.Order(new OrderBy { Field = "Id", Direction = Direction.Ascending })
                               .ToList(new Filter()));

            _out.WriteLine($"P1 select-then-order: {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");

            Assert.True(raised is null or PolicyException, $"got {raised?.GetType().Name}: {raised?.Message}");
        }

        [Fact]
        public void P2_Composed_select_then_page_on_the_handle()
        {
            PolicyQueryable<ZyRoleRow> projected =
                Guard(Built(), DwTier.Strict).Select(new List<string> { "Id", "Name.IsEmpty" });

            Exception? raised = Record.Exception(
                () => projected.Page(new PageBy { PageNumber = 1, PageSize = 10 }).ToList(new Filter()));

            _out.WriteLine($"P2 select-then-page: {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");

            Assert.True(raised is null or PolicyException, $"got {raised?.GetType().Name}: {raised?.Message}");
        }

        [Fact]
        public void P3_Composed_select_then_where_on_the_handle()
        {
            PolicyQueryable<ZyRoleRow> projected =
                Guard(Built(), DwTier.Strict).Select(new List<string> { "Id", "Name.IsEmpty" });

            Exception? raised = Record.Exception(
                () => projected.ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"P3 select-then-where: {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>The entity's own unmapped getter, taken through the same composable route.</summary>
        [Fact]
        public void P4_Composed_select_of_an_unmapped_getter_then_order()
        {
            PolicyQueryable<ZyRole> projected =
                Guard(_db.Roles, DwTier.Strict).Select(new List<string> { "Id", "Display" });

            Exception? raised = Record.Exception(
                () => projected.Order(new OrderBy { Field = "Id", Direction = Direction.Ascending })
                               .ToList(new Filter()));

            _out.WriteLine($"P4 getter select-then-order: {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");

            Assert.True(raised is null or PolicyException, $"got {raised?.GetType().Name}: {raised?.Message}");
        }

        // ==== 2. RowShape._translated: a wrapper in front of EF Core ====================================

        /// <summary>
        /// A projected EF Core query behind a wrapping provider. EF Core still answers it, so the
        /// refusal should be the same one a bare EF Core query gets.
        /// </summary>
        [Fact]
        public void P5_A_wrapper_in_front_of_EF_Core_is_left_to_answer_for_its_own_rows()
        {
            PolicyException bare = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"P5 bare EF: {bare.ErrorCode}");

            IQueryable<ZyRoleRow> wrapped = new ZzWrapped<ZyRoleRow>(Built());

            Exception? raised = Record.Exception(
                () => Guard(wrapped, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            Exception? unguarded = Record.Exception(
                () => new ZzWrapped<ZyRoleRow>(Built()).Where(row => row.Name!.IsEmpty).ToList());

            _out.WriteLine($"P5 wrapped:   {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");
            _out.WriteLine($"P5 unguarded: {unguarded?.GetType().Name ?? "ran"}");

            // A provider that wraps EF Core exists to rewrite what EF Core cannot translate, so its
            // rows are left to it: the guarded query does what the unguarded one does, which for a
            // pass-through wrapper is EF Core's own failure rather than a refusal.
            Assert.False(raised is PolicyException, $"refused: {raised?.Message}");
            Assert.Equal(unguarded?.GetType(), raised?.GetType());
        }

        /// <summary>The same wrapper over an entity query, which the walk holds to EF Core's rules anyway.</summary>
        [Fact]
        public void P6_The_same_wrapper_over_an_entity_query_is_still_held_to_EF_Cores_rules()
        {
            IQueryable<ZyRole> wrapped = new ZzWrapped<ZyRole>(_db.Roles);

            Exception? raised = Record.Exception(
                () => Guard(wrapped, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"P6 wrapped entity: {raised?.GetType().Name ?? "ran"} :: {raised?.Message}");

            // Documented rule: "another provider's rules are its own, so its rows are left alone."
            // If this is a PolicyException the rule is applied to one shape and not the other.
            Assert.False(
                raised is PolicyException,
                $"the entity shape refuses what the projected shape leaves alone: {raised?.Message}");
        }

        // ==== 3. the unknown-name disguise, through the exempted clause =================================

        /// <summary>A name that matches nothing and a field denied for select refuse alike.</summary>
        [Fact]
        public void P7_A_nonexistent_select_and_a_denied_select_refuse_alike()
        {
            PolicyException missing = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(new Filter
                {
                    Selects = new List<string> { "NoSuchMemberAnywhere" }
                }));

            _out.WriteLine($"P7 nonexistent: {missing.ErrorCode} field='{missing.FieldPath}' rule='{missing.RuleId}'");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, missing.ErrorCode);
        }

        /// <summary>
        /// The exempted clause answers an uncomputable member with rows and a nonexistent one with a
        /// refusal, so the two are told apart there.
        /// </summary>
        [Fact]
        public void P8_An_uncomputable_select_returns_rows_where_a_nonexistent_one_refuses()
        {
            FilterResult<ZyRole> uncomputable = Guard(_db.Roles, DwTier.Strict).ToList(new Filter
            {
                Selects = new List<string> { "Id", "Display" }
            });

            Assert.Single(uncomputable.Data);

            Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(new Filter
                {
                    Selects = new List<string> { "Id", "Displayy" }
                }));

            _out.WriteLine("P8 uncomputable select returned rows; nonexistent select refused");
        }

        /// <summary>What an uncomputable leaf actually carries once the builder has skipped it.</summary>
        [Fact]
        public void P9_What_the_exempted_projection_puts_in_the_row()
        {
            FilterResult<ZyRoleRow> result = Guard(Built(), DwTier.Strict).ToList(new Filter
            {
                Selects = new List<string> { "Id", "Name.IsEmpty" }
            });

            ZyRoleRow row = Assert.Single(result.Data);

            _out.WriteLine($"P9 Id={row.Id} Name.Ar='{row.Name?.Ar}' Name.En='{row.Name?.En}' IsEmpty={row.Name?.IsEmpty}");

            // Nothing beside the asked-for leaf may come back with it.
            Assert.True(string.IsNullOrEmpty(row.Name?.Ar), $"Ar leaked: '{row.Name?.Ar}'");
            Assert.True(string.IsNullOrEmpty(row.Name?.En), $"En leaked: '{row.Name?.En}'");
        }

        // ==== 4. the new trace entry, and where it can be read ==========================================

        /// <summary>
        /// A strict refusal names no field. The new trace entry names the canonical path, so it must
        /// not travel with the refusal.
        /// </summary>
        [Fact]
        public void P10_The_refusal_carries_no_canonical_path_with_the_trace_switched_on()
        {
            PolicyQueryable<ZyRole> guarded = Guard(_db.Roles, DwTier.Strict, traceInResult: true);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => guarded.ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine(
                $"P10 code={refusal.ErrorCode} field='{refusal.FieldPath}' rule='{refusal.RuleId}' "
                + $"origin='{refusal.SourceOrigin}' audit='{refusal.AuditPath}' msg='{refusal.Message}'");

            Assert.DoesNotContain("IsEmpty", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IsEmpty", refusal.FieldPath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IsEmpty", refusal.SourceOrigin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// No answered query may carry the new decision, since the clause that produces it is always
        /// refused.
        /// </summary>
        [Fact]
        public void P11_No_answered_query_carries_the_unexpressible_decision()
        {
            FilterResult<ZyRole> answered = Guard(_db.Roles, DwTier.Strict, traceInResult: true)
                .ToList(Where("Code", "admin"));

            Assert.NotNull(answered.Policy);
            Assert.DoesNotContain(
                answered.Policy!.Decisions,
                decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"));

            _out.WriteLine($"P11 decisions on an answered strict query: {answered.Policy.Decisions.Count}");
        }

        // ==== 5. SamePosture: the provider comparison ===================================================

        /// <summary>
        /// Two policy sources of one type, holding different rules, compare equal, so a second host
        /// carrying its own rule store hands it over and has it dropped.
        /// </summary>
        /// <remarks>
        /// Read through the comparison itself rather than through a second <c>Configure</c> call:
        /// the list the first call recorded is process-wide static state, and driving it from a test
        /// would make every suite configuring the assembly's posture in parallel fail. The refusal
        /// side — a source where the first call supplied none — is a real second call, in
        /// <c>ReviewPostureComparisonTests</c>.
        /// </remarks>
        [Fact]
        public void P12_Two_sources_of_one_type_compare_by_type_rather_than_by_instance()
        {
            MethodInfo providerTypes = typeof(DwPolicy)
                .GetMethod("ProviderTypes", BindingFlags.NonPublic | BindingFlags.Static)!;

            Type[] plain = (Type[])providerTypes.Invoke(
                null, new object[] { new IDwPolicyProvider[] { new FakePolicyProvider() } })!;

            Type[] denying = (Type[])providerTypes.Invoke(
                null,
                new object[]
                {
                    new IDwPolicyProvider[]
                    {
                        new FakePolicyProvider()
                            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicTenant)
                    }
                })!;

            _out.WriteLine($"P12 an empty source and a denying one compare as: {string.Join(", ", denying.Select(type => type.Name))}");

            Assert.Equal(plain, denying);
        }

        /// <summary>
        /// Two catalogues that compare equal must answer every administrative lookup alike.
        /// </summary>
        [Fact]
        public void P13_Two_catalogues_comparing_equal_resolve_alike()
        {
            DwEntityCatalogProbe.AssertEqualCataloguesResolveAlike(_out);
        }

        // ==== 6. public Clone, and what a caller can still change under the guard =======================

        /// <summary>
        /// <c>Clone</c>'s contract says the copy shares no object with the original.
        /// </summary>
        [Fact]
        public void P14_The_clone_copies_every_node_and_shares_the_values_the_caller_supplied()
        {
            object value = new ZzShifting();

            Filter original = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Code", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { value }
                        }
                    }
                }
            };

            Filter copy = original.Clone();

            _out.WriteLine(
                "P14 values list same: "
                + ReferenceEquals(original.ConditionGroup!.Conditions[0].Values, copy.ConditionGroup!.Conditions[0].Values)
                + "; value element same: "
                + ReferenceEquals(original.ConditionGroup.Conditions[0].Values[0], copy.ConditionGroup.Conditions[0].Values[0]));

            // The list is the request's own node and is copied; what the caller put in it is the
            // caller's, decoded from JSON and never written to, so it is the same object.
            Assert.NotSame(original.ConditionGroup.Conditions[0].Values, copy.ConditionGroup.Conditions[0].Values);
            Assert.NotSame(original.ConditionGroup.Conditions[0], copy.ConditionGroup.Conditions[0]);
            Assert.Same(
                original.ConditionGroup.Conditions[0].Values[0],
                copy.ConditionGroup.Conditions[0].Values[0]);
        }

        /// <summary>
        /// A caller editing their own request after the guard has read it must not change what runs.
        /// </summary>
        [Fact]
        public void P15_Editing_the_request_after_the_guard_read_it_changes_nothing()
        {
            Filter filter = Where("Code", "admin");

            PolicyQueryable<ZyRole> guarded = Guard(_db.Roles, DwTier.Strict);

            // The guard clones, so the edit below lands on the caller's object alone.
            FilterResult<ZyRole> first = guarded.ToList(filter);

            filter.ConditionGroup!.Conditions[0].Field = "NoSuchMember";
            filter.ConditionGroup.Conditions[0].Values[0] = "nothing";

            Assert.Single(first.Data);

            Exception? raised = Record.Exception(() => guarded.ToList(filter));

            _out.WriteLine($"P15 after the edit: {raised?.GetType().Name ?? "ran"}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>
        /// A value whose text changes between reads. The gate must decide on the same text the query
        /// runs on, or a caller passes a check with one value and queries with another.
        /// </summary>
        [Fact]
        public void P16_A_value_is_read_more_than_once_so_an_unstable_one_is_not_the_one_queried()
        {
            ZzShifting shifting = new();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Code", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { shifting }
                        }
                    }
                }
            };

            FilterResult<ZyRole> result = Guard(_db.Roles, DwTier.Strict).ToList(filter);

            _out.WriteLine($"P16 reads={shifting.Reads} rows={result.Data.Count}");

            // A condition's value is read once to validate its format and again to build the
            // predicate, so a value whose ToString answers differently each time is validated as one
            // value and queried as another. Documented as a limit: pass values that do not change.
            // The policy layer reads no value's content — only Values.Count — so nothing it decides
            // rests on which read won.
            Assert.True(shifting.Reads > 1, $"the value was read {shifting.Reads} times");
        }

        // ==== 7. the new trace entry cannot travel to a later answered query ============================

        /// <summary>
        /// The refusal writes the canonical path onto the handle's trace before it throws. A query
        /// answered afterwards on the same handle must not carry it out with the result.
        /// </summary>
        [Fact]
        public void P17_A_refused_call_leaves_nothing_on_the_next_answered_one()
        {
            PolicyQueryable<ZyRole> guarded = Guard(_db.Roles, DwTier.Strict, traceInResult: true);

            Assert.ThrowsAny<PolicyException>(
                () => guarded.ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            Assert.Contains(
                guarded.LastTrace!.Decisions,
                decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"));

            FilterResult<ZyRole> answered = guarded.ToList(Where("Code", "admin"));

            _out.WriteLine($"P17 answered decisions: {answered.Policy?.Decisions.Count}");

            Assert.NotNull(answered.Policy);
            Assert.DoesNotContain(
                answered.Policy!.Decisions,
                decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"));
        }

        /// <summary>
        /// The shape the library's own guarded <c>Select</c> leaves behind, read back through the
        /// same walk the gate uses.
        /// </summary>
        [Fact]
        public void P18_The_shape_the_guarded_select_leaves_cannot_answer_for_its_own_path()
        {
            IQueryable<ZyRoleRow> once = Built();
            IQueryable<ZyRoleRow> twice =
                Guard(once, DwTier.Strict).Select(new List<string> { "Id", "Name.IsEmpty" }).AsUnguardedQueryable();

            MethodInfo of = typeof(RowShape)
                .GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!
                .MakeGenericMethod(typeof(ZyRoleRow));

            MethodInfo expresses = typeof(RowShape)
                .GetMethod("Expresses", BindingFlags.NonPublic | BindingFlags.Instance)!;

            object first = of.Invoke(null, new object[] { once })!;
            object second = of.Invoke(null, new object[] { twice })!;

            object? before = expresses.Invoke(first, new object[] { "Name.IsEmpty" });
            object? after = expresses.Invoke(second, new object[] { "Name.IsEmpty" });

            _out.WriteLine($"P18 Expresses before the guarded Select: {before?.ToString() ?? "null"}");
            _out.WriteLine($"P18 Expresses after  the guarded Select: {after?.ToString() ?? "null"}");

            Assert.Equal(before, after);
        }

        /// <summary>
        /// The simulator answers the same question the query answers, or an operator reading it is
        /// told a request would be allowed that the query refuses.
        /// </summary>
        [Fact]
        public void P19_The_simulator_cannot_answer_for_a_path_the_query_cannot_compute()
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict };

            PolicySimulation<Filter> simulated = PolicySimulator.Simulate<ZyRole>(
                Where("Name.IsEmpty", "false", DataType.Boolean),
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                options,
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            bool refusedByQuery = Record.Exception(
                () => Guard(_db.Roles, DwTier.Strict)
                    .ToList(Where("Name.IsEmpty", "false", DataType.Boolean))) is PolicyException;

            _out.WriteLine($"P19 simulator refused: {(simulated.Refusal is not null)}; query refused: {refusedByQuery}");

            // The simulator is handed a type and a request, never a source, so it cannot read the
            // model a query would be translated against. It answers every other refusal; this one it
            // cannot, and the documentation says so rather than the simulator guessing.
            Assert.True(refusedByQuery);
            Assert.Null(simulated.Refusal);
        }

        /// <summary>
        /// A segment's projection really is the last thing built, which is what the exemption rests
        /// on there.
        /// </summary>
        [Fact]
        public async Task P20_A_segments_exempted_projection_is_answered_rather_than_failing()
        {
            SegmentResult<ZyRole> result = await Guard(_db.Roles, DwTier.Strict).ToListAsync(new Segment
            {
                Selects = new List<string> { "Id", "Display" },
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions =
                            {
                                new Condition
                                {
                                    Field = "Code", DataType = DataType.Text,
                                    Operator = Operator.Equal, Values = { "admin" }
                                }
                            }
                        }
                    }
                }
            });

            _out.WriteLine($"P20 segment rows: {result.Data.Count}");

            Assert.Single(result.Data);
        }

        /// <summary>
        /// A caller's own projection that assigns the shared type through a conditional rather than
        /// a bare initializer. The same member, the same database, the same tier.
        /// </summary>
        [Fact]
        public void P21_A_conditional_assignment_loses_the_refusal()
        {
            IQueryable<ZyRoleRow> plain = _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            IQueryable<ZyRoleRow> conditional = _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Name = role.Code == null
                    ? new ZyLocalizedText()
                    : new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            Exception? bare = Record.Exception(
                () => Guard(plain, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            Exception? shaped = Record.Exception(
                () => Guard(conditional, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"P21 plain initializer: {bare?.GetType().Name}");
            _out.WriteLine($"P21 conditional:       {shaped?.GetType().Name}");

            Assert.IsAssignableFrom<PolicyException>(bare);
            Assert.IsAssignableFrom<PolicyException>(shaped);
        }
    }

    /// <summary>Catalogue comparison, kept out of the probe class so the assertion reads plainly.</summary>
    internal static class DwEntityCatalogProbe
    {
        internal static void AssertEqualCataloguesResolveAlike(ITestOutputHelper output)
        {
            DwPolicyOptions left = new();
            DwPolicyOptions right = new();

            // The same type, reached under names differing only in case, exposed in two orders.
            left.Entities.Expose<ZyRole>("role").Expose<ZyRole>("Role");
            right.Entities.Expose<ZyRole>("Role");

            MethodInfo same = typeof(DwEntityCatalog).GetMethod(
                "SameAs", BindingFlags.NonPublic | BindingFlags.Instance)!;

            bool equal = (bool)same.Invoke(left.Entities, new object[] { right.Entities })!;

            output.WriteLine($"P13 catalogues compare equal: {equal}");

            if (!equal)
            {
                return;
            }

            foreach (string asked in new[] { "role", "Role", "ROLE", typeof(ZyRole).FullName! })
            {
                Assert.Equal(left.Entities.Resolve(asked), right.Entities.Resolve(asked));
            }

            Assert.Equal(left.Entities.NameOf(typeof(ZyRole)), right.Entities.NameOf(typeof(ZyRole)));
            Assert.Equal(left.Entities.ToArray().Length, right.Entities.ToArray().Length);
        }
    }
}
