using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 7, documentation review of 3.3.0 at 893cadc.
    //
    // Claim 1: "[DwAudit] records a read the request did not name: the members a projection the
    // caller never named hands back are recorded for Select, one event per query rather than per
    // row, only for a field the attribute names."
    //
    // Checked against every document that states it, and against llms.txt section 22, which is the
    // one place that still states the opposite.
    // =============================================================================================

    /// <summary>Nothing denied, one audited member, so the row comes back whole.</summary>
    public class Dx7Whole
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Audited for everything.</summary>
        [DwAudit]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>One member audited for Where alone, so a Select read must not record it.</summary>
    public class Dx7WhereAudited
    {
        public int Id { get; set; }

        [DwAudit(PolicyFeature.Where)]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// A denied member forces a projection, and an audited member the projection cannot keep.
    /// </summary>
    /// <remarks>
    /// <c>Computed</c> has no setter, so a synthesized projection cannot assign it and leaves it
    /// out — while a dry run, which synthesizes nothing, hands the whole row back with it in.
    /// </remarks>
    public class Dx7Uncarried
    {
        public int Id { get; set; }

        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        [DwAudit]
        public string Kept { get; set; } = string.Empty;

        /// <summary>Readable, audited, and not assignable by a projection.</summary>
        [DwAudit]
        public string Computed => "derived";
    }

    public sealed class Dx7AuditReadProbes
    {
        private readonly ITestOutputHelper _out;

        public Dx7AuditReadProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller(bool dryRun = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            context.DryRun = dryRun;

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
            new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

        private static PolicyQueryable<T> Guarded<T>(T[] rows, DwPolicyContext context, DwPolicyOptions options)
            where T : class =>
            rows.AsQueryable().ApplyPolicy(context, options, Attributes());

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing)"
                : string.Join("; ", context.PendingAuditEvents.Select(Describe));

        private static string Describe(DwAuditEvent e) =>
            $"{e.FieldPath}:{e.Feature}:{e.Effect}:dry={e.DryRun}";

        // =========================================================================================
        // The claim itself, and the one document that still denies it.
        // =========================================================================================

        /// <summary>
        /// A request that names no projection records every audited member it hands back, for
        /// <c>Select</c>.
        /// </summary>
        /// <remarks>
        /// llms.txt section 22 lists "a projection the library synthesized" under "Nothing is
        /// recorded for". This is that case, and something is recorded.
        /// </remarks>
        [Fact]
        public void A_request_naming_no_projection_records_the_audited_member_for_select()
        {
            DwPolicyContext context = Caller();

            Guarded(new[] { new Dx7Whole { Id = 1, Name = "a", Email = "a@b" } }, context, Options())
                .ToList(new Filter());

            _out.WriteLine($"no Selects, nothing denied : {Recorded(context)}");

            DwAuditEvent recorded = Assert.Single(context.PendingAuditEvents);

            Assert.Equal("Email", recorded.FieldPath);
            Assert.Equal(PolicyFeature.Select, recorded.Feature);
            Assert.Equal(PolicyEffect.Allow, recorded.Effect);
            Assert.Null(recorded.ErrorCode);
        }

        /// <summary>One event per query, whatever the row count.</summary>
        [Fact]
        public void One_event_per_query_not_one_per_row()
        {
            Dx7Whole[] rows =
            {
                new() { Id = 1, Email = "a@b" },
                new() { Id = 2, Email = "c@d" },
                new() { Id = 3, Email = "e@f" },
                new() { Id = 4, Email = "g@h" },
                new() { Id = 5, Email = "i@j" }
            };

            DwPolicyContext context = Caller();

            List<Dx7Whole> returned = Guarded(rows, context, Options()).ToList(new Filter()).Data;

            _out.WriteLine($"{returned.Count} rows returned, events: {Recorded(context)}");

            Assert.Equal(5, returned.Count);
            Assert.Single(context.PendingAuditEvents);
        }

        /// <summary>Only a member the attribute names, and only for a feature it names.</summary>
        [Fact]
        public void Only_a_field_the_attribute_names_for_a_feature_it_names()
        {
            DwPolicyContext whole = Caller();

            Guarded(new[] { new Dx7Whole { Id = 1, Name = "a", Email = "a@b" } }, whole, Options())
                .ToList(new Filter());

            _out.WriteLine($"audited-for-All  : {Recorded(whole)}");

            // Id and Name are handed back too, and neither carries the attribute.
            Assert.Equal(new[] { "Email" }, whole.PendingAuditEvents.Select(e => e.FieldPath).ToArray());

            DwPolicyContext narrowed = Caller();

            Guarded(new[] { new Dx7WhereAudited { Id = 1, Email = "a@b" } }, narrowed, Options())
                .ToList(new Filter());

            _out.WriteLine($"audited-for-Where: {Recorded(narrowed)}");

            // The member is read, but the attribute names Where, so the read is not in its set.
            Assert.Empty(narrowed.PendingAuditEvents);
        }

        // =========================================================================================
        // What the documents promise for the two halves of the rule.
        // =========================================================================================

        /// <summary>
        /// Where a projection IS built, the members it keeps are recorded and the ones it leaves
        /// out are not.
        /// </summary>
        [Fact]
        public void Where_a_projection_is_built_it_records_what_the_projection_keeps()
        {
            DwPolicyContext context = Caller();

            Guarded(
                    new[] { new Dx7Uncarried { Id = 1, Secret = "s", Kept = "k" } },
                    context,
                    Options())
                .ToList(new Filter());

            _out.WriteLine($"projection built, enforced : {Recorded(context)}");

            // Kept is assigned by the projection; Computed has no setter, so the projection cannot
            // assign it and the caller never receives it.
            Assert.Equal(new[] { "Kept" }, context.PendingAuditEvents.Select(e => e.FieldPath).ToArray());
        }

        /// <summary>
        /// FINDING candidate. A dry run synthesizes nothing, so the whole row is handed back — but
        /// the audit records only what the projection it did not build would have kept.
        /// </summary>
        /// <remarks>
        /// Every document states the rule in two halves: "what the synthesized projection keeps
        /// where one is built, and every member the caller may select where none is, since the row
        /// then comes back whole". A dry run is the second half — nothing is built and the row comes
        /// back whole — and the code takes the first.
        /// </remarks>
        [Fact]
        public void A_dry_run_records_every_member_it_hands_back()
        {
            foreach ((string leg, DwPolicyContext context, DwPolicyOptions options) in new[]
            {
                ("posture switch", Caller(), Options(dryRun: true)),
                ("caller switch", Caller(dryRun: true), Options())
            })
            {
                List<Dx7Uncarried> rows = Guarded(
                        new[] { new Dx7Uncarried { Id = 1, Secret = "s", Kept = "k" } },
                        context,
                        options)
                    .ToList(new Filter())
                    .Data;

                string[] paths = context.PendingAuditEvents.Select(e => e.FieldPath).ToArray();

                _out.WriteLine($"[{leg}] rows[0].Secret = '{rows[0].Secret}', "
                    + $"rows[0].Computed = '{rows[0].Computed}'");
                _out.WriteLine($"[{leg}] recorded: {Recorded(context)}");

                // The row came back whole: the denied member is still on it, so nothing was
                // projected and every member reached the caller.
                Assert.Equal("s", rows[0].Secret);

                // A dry run builds no projection, whatever is denied, so the whole row is what the
                // caller read — and both audited members it handed back are recorded.
                Assert.Contains("Kept", paths);
                Assert.Contains("Computed", paths);
            }
        }

        /// <summary>
        /// The same type with nothing denied, for contrast: no projection is built, and then every
        /// member the caller may select IS recorded, <c>Computed</c> included.
        /// </summary>
        /// <remarks>
        /// This is what pins the previous probe as a divergence rather than a rule about read-only
        /// members: the member is recordable, and a dry run is the one state that drops it.
        /// </remarks>
        [Fact]
        public void With_nothing_denied_a_read_only_audited_member_is_recorded()
        {
            DwPolicyContext context = Caller();

            Guarded(new[] { new Dx7Readable { Id = 1, Kept = "k" } }, context, Options())
                .ToList(new Filter());

            _out.WriteLine($"nothing denied, read-only member : {Recorded(context)}");

            Assert.Contains("Computed", context.PendingAuditEvents.Select(e => e.FieldPath));
        }

        /// <summary>A dry run of a query that denies nothing records both, as it always did.</summary>
        [Fact]
        public void A_dry_run_with_nothing_denied_records_both_members()
        {
            DwPolicyContext context = Caller(dryRun: true);

            Guarded(new[] { new Dx7Readable { Id = 1, Kept = "k" } }, context, Options())
                .ToList(new Filter());

            _out.WriteLine($"dry run, nothing denied : {Recorded(context)}");

            Assert.Contains("Computed", context.PendingAuditEvents.Select(e => e.FieldPath));
            Assert.All(context.PendingAuditEvents, e => Assert.True(e.DryRun));
        }
    }

    /// <summary>The uncarried type with the denial removed.</summary>
    public class Dx7Readable
    {
        public int Id { get; set; }

        [DwAudit]
        public string Kept { get; set; } = string.Empty;

        [DwAudit]
        public string Computed => "derived";
    }
}
