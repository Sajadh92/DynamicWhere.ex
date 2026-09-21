using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies
{
    public class Rd8AuBase
    {
        public int Id { get; set; }

        [DwAudit]
        public string? Seen { get; set; }

        public Rd8Au2? B { get; set; }
    }

    public class Rd8AuSub : Rd8AuBase
    {
        [DwAudit]
        public string? Hidden { get; set; }

        [DwAudit(PolicyFeature.Where)]
        public string? FilteredOnly { get; set; }
    }

    public class Rd8Au2 { public int Id { get; set; } public Rd8Au3? C { get; set; } }

    public class Rd8Au3 { public int Id { get; set; } public Rd8Au4? D { get; set; } }

    public class Rd8Au4
    {
        public int Id { get; set; }

        [DwAudit]
        public string? Four { get; set; }

        public Rd8Au5? E { get; set; }
    }

    /// <summary>Audited and nothing else: nothing in what an Rd8AuBase row can hold declares a transform.</summary>
    public class Rd8Au5
    {
        public int Id { get; set; }

        [DwAudit]
        public string? Five { get; set; }
    }

    public class Rd8AuCard { public int Id { get; set; } }

    public class Rd8AuGold : Rd8AuCard
    {
        [DwAudit]
        [DwMask(MaskStrategy.Full)]
        public string? Pan { get; set; }
    }

    /// <summary>
    /// <c>[DwAudit]</c> answers who read a field. The gate records a use by path, before the query runs,
    /// and a member only a subtype of the row declares, or one past the four segments the policy names,
    /// has no path it could ask about: it came back with the row and nothing was written down. The rows
    /// say it is there, and it is recorded once they do.
    /// </summary>
    public sealed class Rd8UnnamedAuditTests
    {
        private static Rd8AuBase[] Rows() => new Rd8AuBase[]
        {
            new Rd8AuSub
            {
                Id = 1, Seen = "s", Hidden = "h", FilteredOnly = "f",
                B = new() { C = new() { D = new() { Four = "4", E = new() { Five = "5" } } } }
            },
            new Rd8AuSub { Id = 2, Seen = "s", Hidden = "h", B = new() { C = new() { D = new() { Four = "4", E = new() { Five = "5" } } } } }
        };

        private static PolicyQueryable<Rd8AuBase> Guarded(DwPolicyContext caller, DwTier tier, int capacity = 10_000) =>
            Rows().AsQueryable().ApplyPolicy(
                caller,
                new DwPolicyOptions { Tier = tier, Caps = { MaxAuditEvents = capacity } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_member_no_path_names_is_recorded_once_it_is_handed_back(DwTier tier)
        {
            DwPolicyContext caller = Caller();

            Guarded(caller, tier).ToList(new Filter());

            List<string> recorded = caller.PendingAuditEvents
                .Where(read => read.Feature == PolicyFeature.Select)
                .Select(read => read.FieldPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            // Once per path, not per row; a member audited for another feature is not a Select read; and
            // the two the policy names are recorded by the gate, once each, as they were.
            Assert.Equal(new[] { "B.C.D.E.Five", "B.C.D.Four", "Hidden", "Seen" }, recorded);

            DwAuditEvent five = caller.PendingAuditEvents.Single(read => read.FieldPath == "B.C.D.E.Five");

            Assert.Equal(PolicyEffect.Allow, five.Effect);
            Assert.Equal(typeof(Rd8AuBase).FullName, five.EntityType);

            // Nothing here is transformed, so it is the audit alone that has the rows read.
            Assert.False(DynamicWhere.ex.Policies.Masking.GraphWalker.HoldsTransform(typeof(Rd8AuBase)));
        }

        [Fact]
        public void A_masked_one_is_recorded_as_masked()
        {
            DwPolicyContext caller = Caller();
            Rd8AuCard[] cards = { new Rd8AuGold { Id = 1, Pan = "4111" } };

            Rd8AuCard card = cards.AsQueryable()
                .ApplyPolicy(caller, new DwPolicyOptions { Tier = DwTier.Strict }, new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(new Filter()).Data!.Single();

            Assert.Equal("****", ((Rd8AuGold)card).Pan);

            DwAuditEvent read = Assert.Single(caller.PendingAuditEvents);

            Assert.Equal("Pan", read.FieldPath);
            Assert.Equal(PolicyEffect.Mask, read.Effect);
        }

        /// <summary>
        /// A path the projection spells out is one the gate was asked about, past the walk's depth as
        /// within it, so it is recorded there and not a second time from the rows.
        /// </summary>
        [Fact]
        public void A_member_the_projection_spells_out_past_the_walk_is_recorded_once()
        {
            DwPolicyContext caller = Caller();

            Rows().AsQueryable()
                .ApplyPolicy(
                    caller,
                    new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MaxNavigationDepth = 6 } },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToListDynamic(new Filter { Selects = new() { "Id", "B.C.D.E.Five", "B.C.D.E" } });

            Assert.Single(caller.PendingAuditEvents, read => read.FieldPath == "B.C.D.E.Five");
        }

        [Fact]
        public void A_projection_that_leaves_the_member_out_records_nothing_for_it()
        {
            DwPolicyContext caller = Caller();

            Guarded(caller, DwTier.Strict).ToList(new Filter { Selects = new() { "Id" } });

            Assert.Empty(caller.PendingAuditEvents);
        }

        /// <summary>As the gate does at the cap: the read is refused rather than left unrecorded.</summary>
        [Fact]
        public void With_no_room_to_record_it_the_rows_are_withheld()
        {
            // Room for the two the gate records, and none for the two the rows then show.
            PolicyException strict = Assert.Throws<PolicyException>(
                () => Guarded(Caller(), DwTier.Strict, capacity: 2).ToList(new Filter()));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, strict.ErrorCode);
            Assert.Equal("*", strict.FieldPath);

            PolicyException convenience = Assert.Throws<PolicyException>(
                () => Guarded(Caller(), DwTier.Convenience, capacity: 2).ToList(new Filter()));

            Assert.Equal(PolicyErrorCode.CapExceeded, convenience.ErrorCode);
        }

        [Fact]
        public void A_model_with_nothing_audited_or_transformed_runs_no_second_pass()
        {
            Assert.False(DynamicWhere.ex.Policies.Masking.GraphWalker.HoldsAudit(typeof(Rd8Plain)));
            Assert.True(DynamicWhere.ex.Policies.Masking.GraphWalker.HoldsAudit(typeof(Rd8AuBase)));
        }
    }
}
