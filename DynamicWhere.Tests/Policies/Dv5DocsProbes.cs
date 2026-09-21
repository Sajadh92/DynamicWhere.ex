using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 5. Documentation against code for 3.3.0.
    //
    // Each probe answers one written claim by running it. Nothing here drives DwPolicy's static
    // fields: every guarded call is the three-argument ApplyPolicy, which takes its own posture.
    // =============================================================================================

    public sealed class Dv5DocsProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _watchedConnection;
        private readonly Dv5WatchedContext _watched;
        private readonly SqliteConnection _orderConnection;
        private readonly Dv5Context _orders;

        public Dv5DocsProbes(ITestOutputHelper output)
        {
            _out = output;

            _watchedConnection = new SqliteConnection("DataSource=:memory:");
            _watchedConnection.Open();
            _watched = new Dv5WatchedContext(_watchedConnection);
            _watched.Database.EnsureCreated();
            _watched.Rows.Add(new Dv5Watched { First = "a", Second = "b", Amount = 3, Sealed = "s" });
            _watched.SaveChanges();
            _watched.ChangeTracker.Clear();

            _orderConnection = new SqliteConnection("DataSource=:memory:");
            _orderConnection.Open();
            _orders = new Dv5Context(_orderConnection);
            _orders.Database.EnsureCreated();
            _orders.Orders.Add(new Dv5Order
            {
                Code = "AB123",
                Qty = 2,
                Total = new Dv5Money { Amount = 10m, Currency = "USD" },
                Lines = { new Dv5Line { Price = 4m } }
            });
            _orders.SaveChanges();
            _orders.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _watched.Dispose();
            _watchedConnection.Dispose();
            _orders.Dispose();
            _orderConnection.Dispose();
        }

        // ---- harness ------------------------------------------------------------------------------

        private static PolicyResolver Resolver() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyContext Caller() =>
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        /// <summary>A posture whose audit buffer holds exactly one event.</summary>
        private static DwPolicyOptions OneSlot(DwTier tier, bool dryRun = false)
        {
            DwPolicyOptions options = new() { Tier = tier, DryRun = dryRun };

            options.Caps.MaxAuditEvents = 1;
            options.Caps.MinGroupSize = 1;

            return options;
        }

        private static Condition On(string field, DataType type = DataType.Text) => new()
        {
            Field = field,
            DataType = type,
            Operator = Operator.Equal,
            Values = { Value(type) }
        };

        private static string Value(DataType type) => type switch
        {
            DataType.Number => "1",
            DataType.Boolean => "true",
            _ => "x"
        };

        /// <summary>What a call answered with, in a form two calls can be compared by.</summary>
        private static string Outcome(Func<object?> run)
        {
            try
            {
                run();

                return "OK";
            }
            catch (PolicyException refusal)
            {
                return $"PolicyException {refusal.ErrorCode}"
                       + $" path={refusal.FieldPath}"
                       + $" feature={refusal.Feature}"
                       + $" origin={refusal.SourceOrigin ?? "<null>"}"
                       + $" rule={refusal.RuleId ?? "<null>"}";
            }
            catch (LogicException logic)
            {
                return "LogicException " + logic.Message;
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        // =========================================================================================
        // Dv5-A. The audit cap under Strict raises the clause's own field refusal.
        //
        // Claim: "under Strict outside a dry run, a query that exhausts the audit buffer is refused
        // with the clause's own field refusal — same code, FieldPath "*", no SourceOrigin."
        // =========================================================================================

        [Fact]
        public void Dv5_A_The_audit_cap_raises_the_clauses_own_field_refusal_under_Strict()
        {
            (string What, PolicyErrorCode Expected, Func<DwPolicyOptions, string> Run)[] clauses =
            {
                ("Where", PolicyErrorCode.FieldDeniedForWhere, WhereTwice),
                ("Selects", PolicyErrorCode.FieldDeniedForSelect, SelectTwice),
                ("Orders", PolicyErrorCode.FieldDeniedForOrder, OrderTwice),
                ("GroupBy", PolicyErrorCode.FieldDeniedForGroup, GroupTwice),
                ("AggregateBy", PolicyErrorCode.FieldDeniedForAggregate, GroupThenAggregate),
                ("Segment", PolicyErrorCode.FieldDeniedForSegment, SegmentTwice)
            };

            List<string> wrong = new();

            foreach ((string what, PolicyErrorCode expected, Func<DwPolicyOptions, string> run) in clauses)
            {
                string strict = run(OneSlot(DwTier.Strict));

                _out.WriteLine($"{what,-12} strict      -> {strict}");

                string want = $"PolicyException {expected} path=* feature=";

                if (!strict.StartsWith(want, StringComparison.Ordinal))
                {
                    wrong.Add($"{what}: {strict}");
                }

                if (!strict.Contains("origin=<null>", StringComparison.Ordinal)
                    || !strict.Contains("rule=<null>", StringComparison.Ordinal))
                {
                    wrong.Add($"{what} carried an origin or a rule: {strict}");
                }
            }

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // Dv5-B. Convenience and a dry run still answer CapExceeded.
        // =========================================================================================

        [Fact]
        public void Dv5_B_Convenience_and_a_dry_run_still_answer_CapExceeded()
        {
            string convenience = WhereTwice(OneSlot(DwTier.Convenience));
            string dryRun = WhereTwice(OneSlot(DwTier.Strict, dryRun: true));
            string strict = WhereTwice(OneSlot(DwTier.Strict));

            _out.WriteLine($"Convenience        -> {convenience}");
            _out.WriteLine($"Strict + dry run   -> {dryRun}");
            _out.WriteLine($"Strict             -> {strict}");

            Assert.StartsWith("PolicyException CapExceeded", convenience, StringComparison.Ordinal);
            Assert.Contains("MaxAuditEvents", convenience, StringComparison.Ordinal);
            Assert.Contains("path=Second", convenience, StringComparison.Ordinal);

            Assert.StartsWith("PolicyException CapExceeded", dryRun, StringComparison.Ordinal);
            Assert.Contains("MaxAuditEvents", dryRun, StringComparison.Ordinal);

            // The request fails in every posture: the buffer still fails closed.
            Assert.StartsWith("PolicyException", strict, StringComparison.Ordinal);
        }

        // =========================================================================================
        // Dv5-C. Under Strict the cap cannot be told from a denied field or from a name matching
        //        nothing, and the trace still records which refusal it really was.
        // =========================================================================================

        [Fact]
        public void Dv5_C_The_three_refusals_are_alike_and_the_trace_keeps_the_real_reason()
        {
            DwPolicyOptions options = OneSlot(DwTier.Strict);

            string cap = WhereTwice(options);
            string denied = WhereOn(options, "First", "Sealed");
            string unknown = WhereOn(options, "First", "Zzzzz");

            _out.WriteLine($"audit cap reached  -> {cap}");
            _out.WriteLine($"a denied field     -> {denied}");
            _out.WriteLine($"a name matching nothing -> {unknown}");

            Assert.Equal(denied, cap);
            Assert.Equal(unknown, cap);

            // The trace names the real reason, and LastTrace is readable after the refusal.
            PolicyQueryable<Dv5Watched> guarded =
                _watched.Rows.ApplyPolicy(Caller(), options, Resolver());

            Filter filter = new() { ConditionGroup = new ConditionGroup() };

            filter.ConditionGroup.Conditions.Add(On("First"));
            filter.ConditionGroup.Conditions.Add(On("Second"));

            Assert.Throws<PolicyException>(() => guarded.ToList(filter));

            PolicyTrace? trace = guarded.LastTrace;

            Assert.NotNull(trace);

            foreach (PolicyDecision decision in trace!.Decisions)
            {
                _out.WriteLine($"trace: {decision.FieldPath} {decision.Feature} {decision.Action} {decision.Reason}");
            }

            Assert.Contains(
                trace.Decisions,
                d => d.FieldPath == "Second"
                     && d.Action == PolicyAction.Denied
                     && (d.Reason ?? string.Empty).Contains("MaxAuditEvents", StringComparison.Ordinal));
        }

        // =========================================================================================
        // Dv5-D. Which provider the uncomputable-path refusal belongs to.
        //
        // Claim: "EF Core's own provider type, from EF Core's own assembly; every other provider is
        // left alone, a host's own through ReplaceService<IAsyncQueryProvider, …> included. A
        // rewrite inside EF Core's own pipeline leaves EF Core's provider in place, so such a
        // member is refused."
        // =========================================================================================

        [Fact]
        public void Dv5_D_The_refusal_belongs_to_EF_Cores_own_provider_from_EF_Cores_own_assembly()
        {
            Type own = _orders.Orders.AsQueryable().Provider.GetType();

            _out.WriteLine($"plain context     provider = {own.FullName} @ {own.Assembly.GetName().Name}");

            Assert.Equal("Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider", own.FullName);
            Assert.Equal("Microsoft.EntityFrameworkCore", own.Assembly.GetName().Name);

            // EF Core's own provider: the unmapped getter and the owned type's getter are refused.
            string slug = GuardedWhere(_orders.Orders, "Slug");
            string zero = GuardedWhere(_orders.Orders, "Total.IsZero");

            _out.WriteLine($"EF Core's own     Slug          -> {slug}");
            _out.WriteLine($"EF Core's own     Total.IsZero  -> {zero}");

            Assert.StartsWith("PolicyException FieldDeniedForWhere path=*", slug, StringComparison.Ordinal);
            Assert.StartsWith("PolicyException FieldDeniedForWhere path=*", zero, StringComparison.Ordinal);

            // A host's own provider, registered through ReplaceService: left alone. The guarded
            // query does exactly what the unguarded one does.
            using SqliteConnection replacedConnection = new("DataSource=:memory:");

            replacedConnection.Open();

            using Dv5Context replaced = new(replacedConnection, typeof(Dv5PassThroughProvider));

            replaced.Database.EnsureCreated();
            replaced.Orders.Add(new Dv5Order { Code = "AB123", Total = new Dv5Money { Amount = 10m } });
            replaced.SaveChanges();
            replaced.ChangeTracker.Clear();

            Type host = replaced.Orders.AsQueryable().Provider.GetType();

            _out.WriteLine($"replaced provider          = {host.FullName} @ {host.Assembly.GetName().Name}");

            Assert.NotEqual("Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider", host.FullName);

            string hostRaw = Outcome(() => replaced.Orders.Where(o => o.Slug == "AB123-1").ToList());
            string hostGuarded = GuardedWhere(replaced.Orders, "Slug");

            _out.WriteLine($"replaced provider Slug unguarded -> {hostRaw}");
            _out.WriteLine($"replaced provider Slug guarded   -> {hostGuarded}");

            Assert.Equal(hostRaw, hostGuarded);
            Assert.DoesNotContain("FieldDeniedForWhere", hostGuarded, StringComparison.Ordinal);

            // A rewrite inside EF Core's own pipeline leaves EF Core's own provider in front of it,
            // so the member is refused with the rest.
            using SqliteConnection insideConnection = new("DataSource=:memory:");

            insideConnection.Open();

            using Dv5Context inside = new(insideConnection, replacePreprocessor: true);

            inside.Database.EnsureCreated();
            inside.Orders.Add(new Dv5Order { Code = "AB123", Total = new Dv5Money { Amount = 10m } });
            inside.SaveChanges();
            inside.ChangeTracker.Clear();

            Type stillOwn = inside.Orders.AsQueryable().Provider.GetType();

            _out.WriteLine($"pipeline rewrite  provider = {stillOwn.FullName} @ {stillOwn.Assembly.GetName().Name}");

            Assert.Equal("Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider", stillOwn.FullName);

            string insideGuarded = GuardedWhere(inside.Orders, "Slug");

            _out.WriteLine($"pipeline rewrite  Slug guarded   -> {insideGuarded}");

            Assert.StartsWith("PolicyException FieldDeniedForWhere path=*", insideGuarded, StringComparison.Ordinal);
        }

        // =========================================================================================
        // Dv5-E. Which projected rows the refusal reads.
        //
        // Claim: "A projection that does not build its rows with an object initializer — an
        // anonymous type, a constructor with arguments — has no member refused here. A member
        // assigned from a subquery is left alone."
        // =========================================================================================

        [Fact]
        public void Dv5_E_A_row_no_object_initializer_builds_has_no_member_refused()
        {
            List<string> wrong = new();

            // 1. An anonymous type. Nothing says which member each value sets.
            var anonymous = _orders.Orders.Select(o => new { o.Id, o.Total });

            Report("anonymous row, Total.IsZero", RowShape.Of(anonymous).Expresses("Total.IsZero"));

            if (RowShape.Of(anonymous).Expresses("Total.IsZero") == false)
            {
                wrong.Add("an anonymous row had a member refused");
            }

            // 2. A constructor with arguments, and no initializer at all.
            IQueryable<Dv5Row> built = _orders.Orders.Select(o => new Dv5Row(o.Id));

            foreach (string path in new[] { "Nest.Blank", "Money.IsZero", "Code" })
            {
                bool? answer = RowShape.Of(built).Expresses(path);

                Report($"new Dv5Row(o.Id), {path}", answer);

                if (answer == false)
                {
                    wrong.Add($"a constructor-built row had {path} refused");
                }
            }

            string builtRaw = Outcome(() =>
                _orders.Orders.Select(o => new Dv5Row(o.Id)).Where(r => r.Nest.Blank).ToList());
            string builtGuarded = GuardedWhere(built, "Nest.Blank", DataType.Boolean);

            _out.WriteLine($"new Dv5Row(o.Id) Nest.Blank unguarded -> {builtRaw}");
            _out.WriteLine($"new Dv5Row(o.Id) Nest.Blank guarded   -> {builtGuarded}");

            if (builtRaw != builtGuarded)
            {
                wrong.Add($"guarded and unguarded differ: {builtRaw} vs {builtGuarded}");
            }

            // 3. A constructor with arguments that DOES carry an object initializer. Recorded, and
            //    reported, because the text reads two ways.
            IQueryable<Dv5Row> mixed = _orders.Orders.Select(o => new Dv5Row(o.Id)
            {
                Nest = new Dv5Nest { A = o.Code }
            });

            bool? mixedNested = RowShape.Of(mixed).Expresses("Nest.Blank");
            bool? mixedTop = RowShape.Of(mixed).Expresses("Money");

            Report("new Dv5Row(o.Id) { Nest = new Dv5Nest { A = … } }, Nest.Blank", mixedNested);
            Report("new Dv5Row(o.Id) { Nest = new Dv5Nest { A = … } }, Money", mixedTop);

            _out.WriteLine(mixedNested == false
                ? "NOTE: a row built by a constructor WITH an initializer does have a member refused"
                : "a row built by a constructor with an initializer has no member refused");

            // 4. A member assigned from a subquery is left alone, and one copied from the entity is
            //    not — which is the contrast the sentence draws.
            IQueryable<Dv5Row> subquery = _orders.Orders.Select(o => new Dv5Row
            {
                Id = o.Id,
                Lines = o.Lines.Select(l => new Dv5LineRow { Id = l.Id, Price = l.Price }).ToList()
            });

            bool? beneathSubquery = RowShape.Of(subquery).Expresses("Lines.Price");

            Report("Lines = o.Lines.Select(…).ToList(), Lines.Price", beneathSubquery);

            if (beneathSubquery is not null)
            {
                wrong.Add($"a member assigned from a subquery answered {beneathSubquery}");
            }

            IQueryable<Dv5Row> copied = _orders.Orders.Select(o => new Dv5Row
            {
                Id = o.Id,
                Money = o.Total
            });

            bool? beneathCopy = RowShape.Of(copied).Expresses("Money.IsZero");

            Report("Money = o.Total, Money.IsZero", beneathCopy);

            if (beneathCopy != false)
            {
                wrong.Add($"a member copied from the entity answered {beneathCopy} rather than false");
            }

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // Dv5-F. The compute refusal raises no [DwAudit] event, and "left alone" is not a promise
        //        that the path runs.
        // =========================================================================================

        [Fact]
        public void Dv5_F_The_compute_refusal_records_no_audit_event()
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict };

            // Control: an audited field the query does name records exactly one event.
            DwPolicyContext allowed = Caller();

            _orders.Orders
                .ApplyPolicy(allowed, options, Resolver())
                .ToList(Where(On("Code")));

            _out.WriteLine($"an audited field named        -> {allowed.PendingAuditEvents.Count} event(s):"
                + $" {string.Join(",", allowed.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}");

            // Two uses of the one field: the caller filtered on it, and the row the request named no
            // projection for carries it back, which since 3.3.0 is recorded as the read it is.
            Assert.Equal(2, allowed.PendingAuditEvents.Count);
            Assert.Contains(allowed.PendingAuditEvents, e => e.Feature == PolicyFeature.Where);
            Assert.Contains(allowed.PendingAuditEvents, e => e.Feature == PolicyFeature.Select);

            // The refusal of a path the query cannot compute records none.
            DwPolicyContext refused = Caller();

            Assert.Throws<PolicyException>(() =>
                _orders.Orders
                    .ApplyPolicy(refused, options, Resolver())
                    .ToList(Where(On("Slug"))));

            _out.WriteLine($"a path the query cannot compute -> {refused.PendingAuditEvents.Count} event(s):"
                + $" {string.Join(",", refused.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}"))}");

            // No event names the refused path. The request's own projection is recorded as it is for
            // any other request — a use is what the request would have read — but the refusal itself
            // raises none, as an unknown name raises none.
            Assert.DoesNotContain(refused.PendingAuditEvents, e => e.FieldPath.Contains("IsZero"));

            // And a name matching nothing records none either, which is the comparison the text draws.
            DwPolicyContext unknown = Caller();

            Assert.Throws<PolicyException>(() =>
                _orders.Orders
                    .ApplyPolicy(unknown, options, Resolver())
                    .ToList(Where(On("Zzzzz"))));

            _out.WriteLine($"a name matching nothing         -> {unknown.PendingAuditEvents.Count} event(s)");

            Assert.DoesNotContain(unknown.PendingAuditEvents, e => e.FieldPath.Contains("Zzzzz"));
        }

        // =========================================================================================
        // Dv5-G. Convenience and a dry run fail exactly as the unguarded query does on a path the
        //        query cannot compute: the refusal is the strict tier's alone.
        // =========================================================================================

        [Fact]
        public void Dv5_G_Convenience_and_a_dry_run_fail_as_the_unguarded_query_does()
        {
            string raw = Outcome(() => _orders.Orders.Where(o => o.Slug == "AB123-1").ToList());
            string convenience = GuardedWhere(_orders.Orders, "Slug", DataType.Text, DwTier.Convenience);
            string dryRun = GuardedWhere(_orders.Orders, "Slug", DataType.Text, DwTier.Strict, dryRun: true);
            string strict = GuardedWhere(_orders.Orders, "Slug");

            _out.WriteLine($"unguarded        -> {raw}");
            _out.WriteLine($"Convenience      -> {convenience}");
            _out.WriteLine($"Strict + dry run -> {dryRun}");
            _out.WriteLine($"Strict           -> {strict}");

            Assert.Equal(raw, convenience);
            Assert.Equal(raw, dryRun);
            Assert.StartsWith("PolicyException FieldDeniedForWhere path=*", strict, StringComparison.Ordinal);
        }

        // =========================================================================================
        // Dv5-H. Selects is not one of the clauses the database has to compute.
        // =========================================================================================

        [Fact]
        public void Dv5_H_Selects_naming_an_uncomputable_member_still_returns_its_value()
        {
            Filter filter = new() { Selects = new List<string> { "Id", "Slug" } };

            string strict = Outcome(() => _orders.Orders
                .ApplyPolicy(Caller(), new DwPolicyOptions { Tier = DwTier.Strict }, Resolver())
                .ToList(filter));

            _out.WriteLine($"Selects = [Id, Slug] under Strict -> {strict}");

            Assert.Equal("OK", strict);
        }

        // =========================================================================================
        // Dv5-I. The clauses the compute refusal reaches: "a filter, an order, a grouping key, an
        //        aggregated field, and a filter or an order inside a Segment" — and not Selects.
        // =========================================================================================

        [Fact]
        public void Dv5_I_The_compute_refusal_reaches_every_clause_the_database_computes()
        {
            DwPolicyOptions strict = new() { Tier = DwTier.Strict };

            strict.Caps.MinGroupSize = 1;

            (string What, PolicyErrorCode Expected, Func<string> Run)[] clauses =
            {
                ("a filter", PolicyErrorCode.FieldDeniedForWhere,
                    () => Guarded(strict, g => g.ToList(Where(On("Slug"))))),
                ("an order", PolicyErrorCode.FieldDeniedForOrder,
                    () => Guarded(strict, g => g.ToList(new Filter
                    {
                        Orders = new List<OrderBy> { new() { Sort = 1, Field = "Slug" } }
                    }))),
                ("a grouping key", PolicyErrorCode.FieldDeniedForGroup,
                    () => Guarded(strict, g => g.ToList(new Summary
                    {
                        GroupBy = new GroupBy
                        {
                            Fields = new List<string> { "Slug" },
                            AggregateBy = new List<AggregateBy>
                            {
                                new() { Field = null, Alias = "n", Aggregator = Aggregator.Count }
                            }
                        }
                    }))),
                ("an aggregated field", PolicyErrorCode.FieldDeniedForAggregate,
                    () => Guarded(strict, g => g.ToList(new Summary
                    {
                        GroupBy = new GroupBy
                        {
                            Fields = new List<string> { "Code" },
                            AggregateBy = new List<AggregateBy>
                            {
                                new() { Field = "Slug", Alias = "s", Aggregator = Aggregator.Maximum }
                            }
                        }
                    }))),
                ("a filter inside a Segment", PolicyErrorCode.FieldDeniedForSegment,
                    () => Guarded(strict, g => g.ToListAsync(OneSet(On("Slug"))).GetAwaiter().GetResult()))
            };

            List<string> wrong = new();

            foreach ((string what, PolicyErrorCode expected, Func<string> run) in clauses)
            {
                string answer = run();

                _out.WriteLine($"{what,-28} -> {answer}");

                if (!answer.StartsWith($"PolicyException {expected} path=*", StringComparison.Ordinal))
                {
                    wrong.Add($"{what}: {answer}");
                }
            }

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // Dv5-J. A simulation has no source, so it cannot refuse a path the query cannot compute.
        // =========================================================================================

        [Fact]
        public void Dv5_J_A_simulation_shows_an_uncomputable_path_running()
        {
            PolicySimulation<Filter> simulated = PolicySimulator.Simulate<Dv5Order>(
                Where(On("Slug")),
                Caller(),
                new DwPolicyOptions { Tier = DwTier.Strict },
                Resolver());

            _out.WriteLine($"simulated WouldRun = {simulated.WouldRun}");
            _out.WriteLine($"simulated refusal  = {simulated.Refusal?.ErrorCode.ToString() ?? "<null>"}");
            _out.WriteLine($"the same request guarded -> {GuardedWhere(_orders.Orders, "Slug")}");

            Assert.True(simulated.WouldRun);
            Assert.Null(simulated.Refusal);
        }

        // =========================================================================================
        // Dv5-K. A shadow property is nameable by no clause, guarded or not.
        // =========================================================================================

        [Fact]
        public void Dv5_K_A_shadow_property_is_nameable_by_no_clause()
        {
            // Dv5Line.OrderId is a real CLR member; the shadow property here is the foreign key EF
            // Core creates for Dv5Order.Lines when no CLR member carries it. Read the model for one.
            List<string> shadow = _orders.Model
                .FindEntityType(typeof(Dv5Line))!
                .GetProperties()
                .Where(p => p.IsShadowProperty())
                .Select(p => p.Name)
                .ToList();

            _out.WriteLine("shadow properties on Dv5Line: " + (shadow.Count == 0 ? "<none>" : string.Join(", ", shadow)));

            Assert.NotEmpty(shadow);

            DwPolicyOptions strict = new() { Tier = DwTier.Strict };

            foreach (string name in shadow)
            {
                string guarded = Outcome(() => _orders.Lines
                    .ApplyPolicy(Caller(), strict, Resolver())
                    .ToList(Where(On(name, DataType.Number))));

                string convenience = Outcome(() => _orders.Lines
                    .ApplyPolicy(Caller(), new DwPolicyOptions { Tier = DwTier.Convenience }, Resolver())
                    .ToList(Where(On(name, DataType.Number))));

                string unguarded = Outcome(() => DynamicWhere.ex.Source.Extension
                    .Where(_orders.Lines.AsQueryable(), On(name, DataType.Number))
                    .ToList());

                _out.WriteLine($"shadow '{name}' strict      -> {guarded}");
                _out.WriteLine($"shadow '{name}' convenience -> {convenience}");
                _out.WriteLine($"shadow '{name}' unguarded   -> {unguarded}");

                Assert.StartsWith("LogicException", unguarded, StringComparison.Ordinal);
                Assert.StartsWith("LogicException", convenience, StringComparison.Ordinal);
            }
        }

        // =========================================================================================
        // Dv5-L. Clone()'s contract: every node new, the values a condition carries the caller's own
        //        objects in a new list.
        // =========================================================================================

        [Fact]
        public void Dv5_L_Clone_is_a_deep_copy_that_shares_only_the_values_themselves()
        {
            object value = new Dv5Box("x");

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { value }
                        }
                    },
                    SubConditionGroups = { new ConditionGroup { Sort = 2 } }
                },
                Selects = new List<string> { "Id" },
                Orders = new List<OrderBy> { new() { Sort = 1, Field = "Id" } },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            Filter copy = filter.Clone();

            _out.WriteLine($"filter           same reference = {ReferenceEquals(filter, copy)}");
            _out.WriteLine($"condition group  same reference = {ReferenceEquals(filter.ConditionGroup, copy.ConditionGroup)}");
            _out.WriteLine($"condition        same reference = {ReferenceEquals(filter.ConditionGroup!.Conditions[0], copy.ConditionGroup!.Conditions[0])}");
            _out.WriteLine($"sub-group        same reference = {ReferenceEquals(filter.ConditionGroup.SubConditionGroups[0], copy.ConditionGroup.SubConditionGroups[0])}");
            _out.WriteLine($"values LIST      same reference = {ReferenceEquals(filter.ConditionGroup.Conditions[0].Values, copy.ConditionGroup.Conditions[0].Values)}");
            _out.WriteLine($"one VALUE        same reference = {ReferenceEquals(filter.ConditionGroup.Conditions[0].Values[0], copy.ConditionGroup.Conditions[0].Values[0])}");
            _out.WriteLine($"selects          same reference = {ReferenceEquals(filter.Selects, copy.Selects)}");
            _out.WriteLine($"order            same reference = {ReferenceEquals(filter.Orders![0], copy.Orders![0])}");
            _out.WriteLine($"page             same reference = {ReferenceEquals(filter.Page, copy.Page)}");

            Assert.NotSame(filter, copy);
            Assert.NotSame(filter.ConditionGroup, copy.ConditionGroup);
            Assert.NotSame(filter.ConditionGroup.Conditions[0], copy.ConditionGroup.Conditions[0]);
            Assert.NotSame(filter.ConditionGroup.SubConditionGroups[0], copy.ConditionGroup.SubConditionGroups[0]);
            Assert.NotSame(filter.Selects, copy.Selects);
            Assert.NotSame(filter.Orders[0], copy.Orders[0]);
            Assert.NotSame(filter.Page, copy.Page);

            // A new list, holding the caller's own objects: both requests read the same value.
            Assert.NotSame(filter.ConditionGroup.Conditions[0].Values, copy.ConditionGroup.Conditions[0].Values);
            Assert.Same(value, copy.ConditionGroup.Conditions[0].Values[0]);
        }

        // =========================================================================================
        // Dv5-M. The group floor applies to a summary read through the IEnumerable<T> overload.
        // =========================================================================================

        [Fact]
        public void Dv5_M_The_group_floor_applies_to_a_summary_read_in_memory()
        {
            List<Dv5Watched> rows = new();

            // Five rows under "big", two under "small": only the first group clears a floor of five.
            for (int i = 0; i < 5; i++)
            {
                rows.Add(new Dv5Watched { Id = i + 1, First = "big", Second = "b", Amount = 1 });
            }

            for (int i = 0; i < 2; i++)
            {
                rows.Add(new Dv5Watched { Id = 100 + i, First = "small", Second = "b", Amount = 1 });
            }

            DwPolicyOptions floored = new() { Tier = DwTier.Convenience };
            DwPolicyOptions off = new() { Tier = DwTier.Convenience };

            off.Caps.MinGroupSize = 1;

            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "First" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = null, Alias = "n", Aggregator = Aggregator.Count }
                    }
                }
            };

            // ApplyPolicy(IEnumerable<T>, context) is AsQueryable() plus the same pipeline
            // (PolicyExtensions.cs), and only the one-argument form reads DwPolicy's statics, so the
            // posture is handed in here instead.
            int withFloor = ((IEnumerable<Dv5Watched>)rows).AsQueryable()
                .ApplyPolicy(Caller(), floored, Resolver())
                .ToList(summary.Clone())
                .Data!.Count;

            int withoutFloor = ((IEnumerable<Dv5Watched>)rows).AsQueryable()
                .ApplyPolicy(Caller(), off, Resolver())
                .ToList(summary.Clone())
                .Data!.Count;

            _out.WriteLine($"in memory, default floor (5) -> {withFloor} group(s)");
            _out.WriteLine($"in memory, floor off (1)     -> {withoutFloor} group(s)");

            Assert.Equal(1, withFloor);
            Assert.Equal(2, withoutFloor);
        }

        private static Segment OneSet(params Condition[] conditions)
        {
            Segment segment = new();
            ConditionSet set = new() { Sort = 1, ConditionGroup = new ConditionGroup() };

            foreach (Condition condition in conditions)
            {
                set.ConditionGroup.Conditions.Add(condition);
            }

            segment.ConditionSets.Add(set);

            return segment;
        }

        private string Guarded(DwPolicyOptions options, Func<PolicyQueryable<Dv5Order>, object?> run) =>
            Outcome(() => run(_orders.Orders.ApplyPolicy(Caller(), options, Resolver())));

        // ---- the clauses the cap probes run --------------------------------------------------------

        private static Filter Where(params Condition[] conditions)
        {
            Filter filter = new() { ConditionGroup = new ConditionGroup() };

            foreach (Condition condition in conditions)
            {
                filter.ConditionGroup.Conditions.Add(condition);
            }

            return filter;
        }

        private string WhereTwice(DwPolicyOptions options) => WhereOn(options, "First", "Second");

        private string WhereOn(DwPolicyOptions options, params string[] fields)
        {
            Condition[] conditions = Array.ConvertAll(fields, f => On(f));

            return Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToList(Where(conditions)));
        }

        private string SelectTwice(DwPolicyOptions options) =>
            Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToList(new Filter { Selects = new List<string> { "First", "Second" } }));

        private string OrderTwice(DwPolicyOptions options) =>
            Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy>
                    {
                        new() { Sort = 1, Field = "First" },
                        new() { Sort = 2, Field = "Second" }
                    }
                }));

        private string GroupTwice(DwPolicyOptions options) =>
            Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "First", "Second" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = null, Alias = "n", Aggregator = Aggregator.Count }
                        }
                    }
                }));

        private string GroupThenAggregate(DwPolicyOptions options) =>
            Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "First" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Field = "Amount", Alias = "s", Aggregator = Aggregator.Sumation }
                        }
                    }
                }));

        private string SegmentTwice(DwPolicyOptions options)
        {
            Segment segment = new();
            ConditionSet set = new() { Sort = 1, ConditionGroup = new ConditionGroup() };

            set.ConditionGroup.Conditions.Add(On("First"));
            set.ConditionGroup.Conditions.Add(On("Second"));
            segment.ConditionSets.Add(set);

            return Outcome(() => _watched.Rows
                .ApplyPolicy(Caller(), options, Resolver())
                .ToListAsync(segment).GetAwaiter().GetResult());
        }

        private string GuardedWhere<T>(
            IQueryable<T> source,
            string field,
            DataType type = DataType.Text,
            DwTier tier = DwTier.Strict,
            bool dryRun = false)
            where T : class =>
            Outcome(() => source
                .ApplyPolicy(Caller(), new DwPolicyOptions { Tier = tier, DryRun = dryRun }, Resolver())
                .ToList(Where(On(field, type))));

        private void Report(string what, bool? answer) =>
            _out.WriteLine($"{what,-62} Expresses = {(answer is null ? "null (left alone)" : answer.ToString())}");
    }
}
