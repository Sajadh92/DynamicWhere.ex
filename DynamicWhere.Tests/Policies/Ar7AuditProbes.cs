using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 7, adversarial security review of 3.3.0 at 893cadc.
    //
    // Reviews round 6's fixes: the audit use recorded for a synthesized projection, AuditPath on the
    // four "*" refusals, the blank GroupBy guard, and the alias/rename agreement.
    // =============================================================================================

    /// <summary>An audited field the caller may not project, beside one they may.</summary>
    public class Ar7Audited
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Audited and refused for projection.</summary>
        [DwAudit]
        [DwNoSelect]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>An audited member a projection cannot assign, beside a denied one.</summary>
    public class Ar7Uncarried
    {
        public int Id { get; set; }

        /// <summary>Audited, allowed, and read-only — a projection cannot assign it.</summary>
        [DwAudit]
        public string Computed => "computed-" + Id;

        /// <summary>Denied, which is what makes a projection be synthesized at all.</summary>
        [DwDenied]
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>Every member audited and allowed, so nothing is denied and no projection is built.</summary>
    public class Ar7AllAudited
    {
        public int Id { get; set; }

        [DwAudit]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>An audited field named in a where clause and carried by the synthesized projection.</summary>
    public class Ar7Both
    {
        public int Id { get; set; }

        [DwAudit]
        public string Badge { get; set; } = string.Empty;

        [DwDenied]
        public string Secret { get; set; } = string.Empty;
    }

    public sealed class Ar7AuditProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7AuditProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller(bool dryRun = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            context.DryRun = dryRun;

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(DwTier tier = DwTier.Strict, bool dryRun = false) =>
            new()
            {
                Tier = tier,
                DryRun = dryRun,
                AuditRefusals = true,
                Caps = { MinGroupSize = 1 }
            };

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing recorded)"
                : string.Join(
                    "; ",
                    context.PendingAuditEvents.Select(
                        e => $"{e.FieldPath}:{e.Feature}:{e.Effect}:dry={e.DryRun}:{e.ErrorCode?.ToString() ?? "-"}"));

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
        // 1. The audit use a synthesized projection records.
        // =========================================================================================

        /// <summary>
        /// The control: enforced, the denied audited field is not returned and no use is recorded
        /// for it.
        /// </summary>
        [Fact]
        public void Enforced_records_no_use_for_a_field_the_projection_leaves_out()
        {
            Ar7Audited[] rows = { new() { Id = 1, Name = "a", NationalId = "AAA-111" } };

            DwPolicyContext context = Caller();

            List<Ar7Audited> got = rows.AsQueryable()
                .ApplyPolicy(context, Posture(DwTier.Convenience), Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows  : NationalId='{got[0].NationalId}' Name='{got[0].Name}'");
            _out.WriteLine($"audit : {Recorded(context)}");

            Assert.Equal(string.Empty, got[0].NationalId);
            Assert.DoesNotContain(context.PendingAuditEvents, e => e.FieldPath == "NationalId");
        }

        /// <summary>
        /// CANDIDATE. In a dry run the row comes back whole, so the audited denied field's value
        /// reaches the caller — and the audit loop records only what the projection would have kept,
        /// which does not include it.
        /// </summary>
        [Fact]
        public void Dry_run_returns_the_audited_denied_value_and_records_no_use()
        {
            Ar7Audited[] rows = { new() { Id = 1, Name = "a", NationalId = "AAA-111" } };

            DwPolicyContext context = Caller(dryRun: true);

            List<Ar7Audited> got = rows.AsQueryable()
                .ApplyPolicy(context, Posture(DwTier.Strict), Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows  : NationalId='{got[0].NationalId}'");
            _out.WriteLine($"audit : {Recorded(context)}");

            // What the caller receives.
            Assert.Equal("AAA-111", got[0].NationalId);

            // A dry run applies no projection, so what the caller receives is every member — the
            // audited denied one included — and every one of them is recorded, with the effect the
            // policy decided.
            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == "NationalId" && e.Feature == PolicyFeature.Select);

            // The asymmetry, in the same posture: naming the field records the use, so an empty
            // Selects is still one token past [DwAudit] — which is the hole round 6 set out to close.
            DwPolicyContext named = Caller(dryRun: true);

            rows.AsQueryable()
                .ApplyPolicy(named, Posture(DwTier.Strict), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "NationalId" } });

            _out.WriteLine($"named : {Recorded(named)}");

            Assert.Contains(named.PendingAuditEvents, e => e.FieldPath == "NationalId");
        }

        /// <summary>
        /// CANDIDATE. An audited member the projection cannot assign is still returned by the
        /// narrowed projection's sibling path... or is it? Records what actually happens.
        /// </summary>
        [Fact]
        public void An_audited_member_a_projection_cannot_assign()
        {
            Ar7Uncarried[] rows = { new() { Id = 1, Secret = "s" } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() =>
            {
                List<Ar7Uncarried> got = rows.AsQueryable()
                    .ApplyPolicy(context, Posture(DwTier.Convenience), Attributes())
                    .ToList(new Filter()).Data;

                _out.WriteLine($"rows  : Computed='{got[0].Computed}' Secret='{got[0].Secret}'");
            });

            _out.WriteLine($"error : {error?.GetType().Name} {error?.Message}");
            _out.WriteLine($"audit : {Recorded(context)}");
        }

        /// <summary>
        /// Nothing denied: no projection is built, the row comes back whole, and every audited
        /// member is recorded as used.
        /// </summary>
        [Fact]
        public void Nothing_denied_records_a_use_for_every_member_returned()
        {
            Ar7AllAudited[] rows = { new() { Id = 1, Name = "a" } };

            DwPolicyContext context = Caller();

            List<Ar7AllAudited> got = rows.AsQueryable()
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows  : Name='{got[0].Name}'");
            _out.WriteLine($"audit : {Recorded(context)}");

            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "Name" && e.Feature == PolicyFeature.Select);
        }

        /// <summary>
        /// A field the request names in a where clause and the synthesized projection also carries
        /// records one Where use and one Select use — not two of either.
        /// </summary>
        [Fact]
        public void A_field_named_and_projected_records_each_use_once()
        {
            Ar7Both[] rows = { new() { Id = 1, Badge = "b", Secret = "s" } };

            DwPolicyContext context = Caller();

            rows.AsQueryable()
                .ApplyPolicy(context, Posture(DwTier.Convenience), Attributes())
                .ToList(new Filter
                {
                    ConditionGroup = new ConditionGroup
                    {
                        Sort = 0,
                        Connector = DynamicWhere.ex.Enums.Connector.And,
                        Conditions = new List<Condition>
                        {
                            new()
                            {
                                Sort = 0, Field = "Badge",
                                DataType = DynamicWhere.ex.Enums.DataType.Text,
                                Operator = DynamicWhere.ex.Enums.Operator.Equal,
                                Values = new List<object> { "b" }
                            }
                        }
                    }
                });

            _out.WriteLine($"audit : {Recorded(context)}");

            int where = context.PendingAuditEvents.Count(
                e => e.FieldPath == "Badge" && e.Feature == PolicyFeature.Where);
            int select = context.PendingAuditEvents.Count(
                e => e.FieldPath == "Badge" && e.Feature == PolicyFeature.Select);

            _out.WriteLine($"where={where} select={select}");

            Assert.Equal(1, where);
            Assert.Equal(1, select);
        }

        /// <summary>
        /// The recording runs before the cost check, the scope injection and the required-filter
        /// check, so a refused query records uses of fields the caller neither named nor received.
        /// </summary>
        [Fact]
        public void A_refused_query_records_uses_of_the_projection_it_never_returned()
        {
            Ar7Both[] rows = { new() { Id = 1, Badge = "b", Secret = "s" } };

            DwPolicyContext context = Caller();

            DwPolicyOptions options = Posture();

            // Refused after the projection is synthesized and its uses recorded.
            options.Caps.MaxQueryCost = 1;
            options.Caps.DefaultFieldCost = 1000;

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, options, Attributes())
                .ToList(new Filter
                {
                    ConditionGroup = new ConditionGroup
                    {
                        Sort = 0,
                        Connector = DynamicWhere.ex.Enums.Connector.And,
                        Conditions = new List<Condition>
                        {
                            new()
                            {
                                Sort = 0, Field = "Id",
                                DataType = DynamicWhere.ex.Enums.DataType.Number,
                                Operator = DynamicWhere.ex.Enums.Operator.GreaterThan,
                                Values = new List<object> { "0" }
                            }
                        }
                    }
                }));

            _out.WriteLine($"refused : {(error as PolicyException)?.ErrorCode.ToString() ?? error?.GetType().Name ?? "OK"}");
            _out.WriteLine($"audit   : {Recorded(context)}");

            Assert.NotNull(error);

            // The caller received nothing, and the audit says they read Badge.
            Assert.Contains(
                context.PendingAuditEvents,
                e => e.FieldPath == "Badge" && e.Feature == PolicyFeature.Select && e.Effect == PolicyEffect.Allow);
        }
    }
}
