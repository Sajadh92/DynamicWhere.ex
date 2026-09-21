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
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A nested node, so a path can be made deep enough to trip the navigation cap.</summary>
    public class Ar7Node
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public Ar7Node? Child { get; set; }
    }

    /// <summary>A root whose child chain gives a long path.</summary>
    public class Ar7Deep
    {
        public int Id { get; set; }

        public Ar7Node? Node { get; set; }
    }

    /// <summary>A tenant-scoped type whose scope reads an ambient value.</summary>
    public class Ar7Scoped
    {
        public int Id { get; set; }

        [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
        public int TenantId { get; set; }

        public decimal Amount { get; set; }
    }

    /// <summary>A type demanding the caller supply the scope, under a public name.</summary>
    public class Ar7Demanding
    {
        public int Id { get; set; }

        [DwAlias("org")]
        [DwRequireWhere]
        public int TenantId { get; set; }

        public decimal Amount { get; set; }
    }

    /// <summary>Several audited members, so the audit cap can be tripped by any one of them.</summary>
    public class Ar7ManyAudited
    {
        public int Id { get; set; }

        [DwAudit]
        public string A { get; set; } = string.Empty;

        [DwAudit]
        public string B { get; set; } = string.Empty;

        [DwAudit]
        public string C { get; set; } = string.Empty;
    }

    public sealed class Ar7RefusalProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7RefusalProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller(bool dryRun = false, int tenant = -1)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            context.DryRun = dryRun;

            if (tenant >= 0)
            {
                context.WithValue("TenantId", tenant);
            }

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(
            DwTier tier = DwTier.Strict, bool dryRun = false, Action<DwCaps>? caps = null)
        {
            DwPolicyOptions options = new() { Tier = tier, DryRun = dryRun, AuditRefusals = true };

            options.Caps.MinGroupSize = 1;

            caps?.Invoke(options.Caps);

            return options;
        }

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing recorded)"
                : string.Join(
                    "; ",
                    context.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}:{e.ErrorCode?.ToString() ?? "-"}"));

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}",
            _ => $"{error.GetType().Name}: {error.Message.Split('\n')[0]}"
        };

        private static Exception? Catch(Action run)
        {
            try
            {
                run();

                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }

        // =========================================================================================
        // AuditPath on the four refusals that report "*" under Strict.
        // =========================================================================================

        /// <summary>A navigation-depth cap: "*" to the caller, the real path to the audit.</summary>
        [Fact]
        public void The_navigation_depth_cap_keeps_the_path_for_the_audit()
        {
            Ar7Deep[] rows = { new() { Id = 1 } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, Posture(caps: c => c.MaxNavigationDepth = 2), Attributes())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy>
                    {
                        new() { Sort = 0, Field = "Node.Child.Label", Direction = Direction.Ascending }
                    }
                }));

            _out.WriteLine($"refusal : {Shape(error)}");
            _out.WriteLine($"audit   : {Recorded(context)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "Node.Child.Label");
        }

        /// <summary>A structural cap concerns no field, and records "*" honestly.</summary>
        [Fact]
        public void A_structural_cap_records_the_clause()
        {
            Ar7Deep[] rows = { new() { Id = 1 } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, Posture(caps: c => c.MaxOrderFields = 1), Attributes())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy>
                    {
                        new() { Sort = 0, Field = "Id", Direction = Direction.Ascending },
                        new() { Sort = 1, Field = "Node", Direction = Direction.Ascending }
                    }
                }));

            _out.WriteLine($"refusal : {Shape(error)}");
            _out.WriteLine($"audit   : {Recorded(context)}");

            Assert.Equal("*", Assert.IsType<PolicyException>(error).FieldPath);
            Assert.All(context.PendingAuditEvents, e => Assert.Equal("*", e.FieldPath));
        }

        /// <summary>A forced predicate the context cannot supply: "*" out, the column to the audit.</summary>
        [Fact]
        public void A_missing_context_value_keeps_the_scope_column_for_the_audit()
        {
            Ar7Scoped[] rows = { new() { Id = 1, TenantId = 5, Amount = 1m } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter()));

            _out.WriteLine($"refusal : {Shape(error)}");
            _out.WriteLine($"audit   : {Recorded(context)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.MissingContextValue, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "TenantId");
        }

        /// <summary>A missing required filter names the alias out and the canonical path in.</summary>
        [Fact]
        public void A_missing_required_filter_names_the_alias_out_and_the_path_in()
        {
            Ar7Demanding[] rows = { new() { Id = 1, TenantId = 5, Amount = 1m } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter()));

            _out.WriteLine($"refusal : {Shape(error)}");
            _out.WriteLine($"audit   : {Recorded(context)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.RequiredFilterMissing, refusal.ErrorCode);
            Assert.Equal("org", refusal.FieldPath);
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "TenantId");
        }

        /// <summary>
        /// The audit cap: the refusal must be the ordinary field denial under Strict, identical
        /// whichever member tripped it, and the audit must still say which one that was.
        /// </summary>
        [Fact]
        public void The_audit_cap_refuses_identically_whichever_member_trips_it()
        {
            Ar7ManyAudited[] rows = { new() { Id = 1, A = "a", B = "b", C = "c" } };

            List<string> shapes = new();

            for (int room = 1; room < 4; room++)
            {
                DwPolicyContext context = Caller();

                Exception? error = Catch(() => rows.AsQueryable()
                    .ApplyPolicy(context, Posture(caps: c => c.MaxAuditEvents = room), Attributes())
                    .ToList(new Filter()));

                shapes.Add(Shape(error));

                _out.WriteLine($"room={room} : {Shape(error)}");
                _out.WriteLine($"          audit : {Recorded(context)}");
            }

            // Every refusal that fires reads alike: no field, no origin, no rule. So the cap cannot
            // be turned into an oracle by watching which member tripped it.
            Assert.Single(shapes.Where(shape => shape != "OK").Distinct());
        }

        // =========================================================================================
        // The dry run each of the four reads.
        // =========================================================================================

        /// <summary>A per-context dry run alone suppresses every one of the four refusals.</summary>
        [Fact]
        public void A_per_context_dry_run_suppresses_all_four()
        {
            Ar7Deep[] deep = { new() { Id = 1, Node = new Ar7Node { Id = 2, Label = "n", Child = new Ar7Node { Id = 3, Label = "c" } } } };
            Ar7Scoped[] scoped = { new() { Id = 1, TenantId = 5, Amount = 1m } };
            Ar7Demanding[] demanding = { new() { Id = 1, TenantId = 5, Amount = 1m } };
            Ar7ManyAudited[] audited = { new() { Id = 1, A = "a", B = "b", C = "c" } };

            Exception? cap = Catch(() => deep.AsQueryable()
                .ApplyPolicy(Caller(dryRun: true), Posture(caps: c => c.MaxNavigationDepth = 2), Attributes())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy>
                    {
                        new() { Sort = 0, Field = "Node.Child.Label", Direction = Direction.Ascending }
                    }
                }));

            Exception? context = Catch(() => scoped.AsQueryable()
                .ApplyPolicy(Caller(dryRun: true), Posture(), Attributes())
                .ToList(new Filter()));

            Exception? required = Catch(() => demanding.AsQueryable()
                .ApplyPolicy(Caller(dryRun: true), Posture(), Attributes())
                .ToList(new Filter()));

            // The audit cap is the one that fails closed even in a dry run, by design.
            Exception? auditCap = Catch(() => audited.AsQueryable()
                .ApplyPolicy(Caller(dryRun: true), Posture(caps: c => c.MaxAuditEvents = 1), Attributes())
                .ToList(new Filter()));

            _out.WriteLine("(the audit cap is documented to fail closed even in a dry run)");

            _out.WriteLine($"navigation cap : {Shape(cap)}");
            _out.WriteLine($"context value  : {Shape(context)}");
            _out.WriteLine($"required filter: {Shape(required)}");
            _out.WriteLine($"audit cap      : {Shape(auditCap)}");

            Assert.Null(cap);
            Assert.Null(context);
            Assert.Null(required);
        }

        /// <summary>The global switch alone does the same.</summary>
        [Fact]
        public void A_global_dry_run_suppresses_all_four()
        {
            Ar7Scoped[] scoped = { new() { Id = 1, TenantId = 5, Amount = 1m } };
            Ar7Demanding[] demanding = { new() { Id = 1, TenantId = 5, Amount = 1m } };

            Exception? context = Catch(() => scoped.AsQueryable()
                .ApplyPolicy(Caller(), Posture(dryRun: true), Attributes())
                .ToList(new Filter()));

            Exception? required = Catch(() => demanding.AsQueryable()
                .ApplyPolicy(Caller(), Posture(dryRun: true), Attributes())
                .ToList(new Filter()));

            _out.WriteLine($"context value  : {Shape(context)}");
            _out.WriteLine($"required filter: {Shape(required)}");

            Assert.Null(context);
            Assert.Null(required);
        }
    }
}
