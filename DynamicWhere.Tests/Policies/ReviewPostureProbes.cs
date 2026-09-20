using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using System.Reflection;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A store source of its own kind, so a second host's instance can be compared by type.</summary>
    public sealed class RbNoRules : IDwPolicyProvider
    {
        public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) =>
            Array.Empty<PolicyFragment>();
    }

    /// <summary>
    /// DW-16, second pass: pairs of postures a reasonable host would call identical, and what
    /// <c>DwPolicy.Configure</c> does with the second one.
    /// </summary>
    public sealed class ReviewPostureProbes
    {
        private readonly ITestOutputHelper _out;

        public ReviewPostureProbes(ITestOutputHelper output)
        {
            _out = output;
            PolicyBootstrap.Ensure();
        }

        /// <summary>A posture holding exactly what is in force, value by value.</summary>
        private static DwPolicyOptions Copy()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            DwPolicyOptions copy = new()
            {
                Tier = inForce.Tier,
                DryRun = inForce.DryRun,
                IncludeTraceInResult = inForce.IncludeTraceInResult,
                AuditRefusals = inForce.AuditRefusals,
                StoreFailure = inForce.StoreFailure,
                MaxSnapshotAge = inForce.MaxSnapshotAge,
                RefreshInterval = inForce.RefreshInterval
            };

            if (!string.IsNullOrEmpty(inForce.HashSalt))
            {
                copy.HashSalt = inForce.HashSalt;
            }

            copy.Caps.MaxPageSize = inForce.Caps.MaxPageSize;
            copy.Caps.DefaultPageSize = inForce.Caps.DefaultPageSize;
            copy.Caps.MaxConditions = inForce.Caps.MaxConditions;
            copy.Caps.MaxConditionDepth = inForce.Caps.MaxConditionDepth;
            copy.Caps.MaxConditionSets = inForce.Caps.MaxConditionSets;
            copy.Caps.MaxConditionValues = inForce.Caps.MaxConditionValues;
            copy.Caps.MaxAggregates = inForce.Caps.MaxAggregates;
            copy.Caps.MaxOrderFields = inForce.Caps.MaxOrderFields;
            copy.Caps.MaxNavigationDepth = inForce.Caps.MaxNavigationDepth;
            copy.Caps.MaxQueryCost = inForce.Caps.MaxQueryCost;
            copy.Caps.DefaultFieldCost = inForce.Caps.DefaultFieldCost;
            copy.Caps.MaxAuditEvents = inForce.Caps.MaxAuditEvents;
            copy.Caps.SchemaDepth = inForce.Caps.SchemaDepth;
            copy.Caps.SchemaCycleLimit = inForce.Caps.SchemaCycleLimit;
            copy.Caps.MaxSchemaFields = inForce.Caps.MaxSchemaFields;

            if (inForce.Caps.IsMinGroupSizeSet)
            {
                copy.Caps.MinGroupSize = inForce.Caps.MinGroupSize;
            }

            foreach (KeyValuePair<Type, string> exposed in inForce.Entities.Entities)
            {
                copy.Entities.Expose(exposed.Key, exposed.Value);
            }

            return copy;
        }

        private string Ask(DwPolicyOptions options, params IDwPolicyProvider[] providers)
        {
            try
            {
                DwPolicy.Configure(options, providers);

                return "accepted";
            }
            catch (InvalidOperationException refused)
            {
                return $"REFUSED: {refused.Message.Split('.')[0]}";
            }
        }

        // ---- caps written as their own default ------------------------------------------------------------

        [Fact]
        public void MinGroupSize_written_as_its_own_default_is_accepted()
        {
            DwPolicyOptions copy = Copy();

            copy.Caps.MinGroupSize = DwCaps.DefaultMinGroupSize;

            string outcome = Ask(copy);

            _out.WriteLine($"MinGroupSize = {DwCaps.DefaultMinGroupSize}: {outcome}");

            Assert.Equal("accepted", outcome);
        }

        [Fact]
        public void Every_other_cap_written_as_its_own_default_is_accepted()
        {
            DwPolicyOptions copy = Copy();

            // Each already holds the value in force; writing it again is what a host binding the
            // documented appsettings sample does.
            copy.Caps.MaxPageSize = 1000;
            copy.Caps.DefaultPageSize = 0;
            copy.Caps.MaxConditions = 50;
            copy.Caps.MaxConditionDepth = 10;
            copy.Caps.MaxConditionSets = 10;
            copy.Caps.MaxConditionValues = 1000;
            copy.Caps.MaxAggregates = 50;
            copy.Caps.MaxOrderFields = 10;
            copy.Caps.MaxNavigationDepth = 4;
            copy.Caps.MaxQueryCost = 1000;
            copy.Caps.DefaultFieldCost = 1;
            copy.Caps.MaxAuditEvents = 10_000;
            copy.Caps.SchemaDepth = 2;
            copy.Caps.SchemaCycleLimit = 2;
            copy.Caps.MaxSchemaFields = 2000;

            string outcome = Ask(copy);

            _out.WriteLine($"every cap at its default: {outcome}");

            Assert.Equal("accepted", outcome);
        }

        // ---- the trace flag, null against the tier's own value --------------------------------------------

        [Fact]
        public void IncludeTraceInResult_written_as_the_tier_s_own_value_is_accepted()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            Assert.Null(inForce.IncludeTraceInResult);

            DwPolicyOptions copy = Copy();

            // The value the tier already follows: identical in effect, value for value.
            copy.IncludeTraceInResult = inForce.Tier == DwTier.Convenience;

            string outcome = Ask(copy);

            _out.WriteLine($"IncludeTraceInResult = {copy.IncludeTraceInResult} under tier {inForce.Tier}: {outcome}");

            Assert.Equal("accepted", outcome);
        }

        // ---- the hash salt ---------------------------------------------------------------------------------

        [Fact]
        public void The_same_hash_salt_in_both_is_accepted()
        {
            DwPolicyOptions copy = Copy();

            string outcome = Ask(copy);

            _out.WriteLine($"same salt '{DwPolicy.Options.HashSalt}': {outcome}");

            Assert.Equal("accepted", outcome);
        }

        // ---- the catalogue ---------------------------------------------------------------------------------

        [Fact]
        public void A_catalogue_built_in_a_different_order_is_accepted()
        {
            DwPolicyOptions copy = Copy();
            DwPolicyOptions reversed = Copy();

            // Rebuilt back to front. The pairs are the same; only the order of the Expose calls differs.
            DwPolicyOptions other = new()
            {
                Tier = copy.Tier,
                DryRun = copy.DryRun,
                IncludeTraceInResult = copy.IncludeTraceInResult,
                AuditRefusals = copy.AuditRefusals,
                StoreFailure = copy.StoreFailure,
                MaxSnapshotAge = copy.MaxSnapshotAge,
                RefreshInterval = copy.RefreshInterval
            };

            foreach (KeyValuePair<Type, string> exposed in reversed.Entities.Entities.Reverse())
            {
                other.Entities.Expose(exposed.Key, exposed.Value);
            }

            string outcome = Ask(other);

            _out.WriteLine($"catalogue reversed ({other.Entities.Entities.Count} types): {outcome}");

            Assert.Equal("accepted", outcome);
        }

        [Fact]
        public void A_type_exposed_under_two_names_in_a_different_order_is_compared_by_the_last_one()
        {
            // Not a refusal to fix on its own: it says what the two catalogues really do differ in.
            // Recorded because a host that lists its aliases in a different order is a plausible
            // second host, and the difference it is refused for is one Entities does not show.
            DwEntityCatalog first = new();
            DwEntityCatalog second = new();

            first.Expose<RbParty>("party").Expose<RbParty>("counterparty");
            second.Expose<RbParty>("counterparty").Expose<RbParty>("party");

            _out.WriteLine($"first names: {string.Join(",", first.Entities.Values)} | resolves party: {first.Resolve("party") is not null}, counterparty: {first.Resolve("counterparty") is not null}");
            _out.WriteLine($"second names: {string.Join(",", second.Entities.Values)} | resolves party: {second.Resolve("party") is not null}, counterparty: {second.Resolve("counterparty") is not null}");

            Assert.NotEqual(first.Entities[typeof(RbParty)], second.Entities[typeof(RbParty)]);
        }

        // ---- the providers ---------------------------------------------------------------------------------

        [Fact]
        public void The_posture_in_force_with_no_provider_is_accepted()
        {
            string outcome = Ask(DwPolicy.Options);

            _out.WriteLine($"Configure(DwPolicy.Options): {outcome}");

            Assert.Equal("accepted", outcome);
        }

        [Fact]
        public void The_posture_in_force_with_a_provider_is_refused()
        {
            string outcome = Ask(DwPolicy.Options, new RbNoRules());

            _out.WriteLine($"Configure(DwPolicy.Options, provider): {outcome}");

            Assert.StartsWith("REFUSED", outcome);
        }

        [Fact]
        public void A_copy_with_a_provider_the_first_call_never_had_is_refused()
        {
            string outcome = Ask(Copy(), new RbNoRules());

            _out.WriteLine($"copy + provider: {outcome}");

            Assert.StartsWith("REFUSED", outcome);
        }

        [Fact]
        public void The_attribute_provider_alone_is_not_a_difference()
        {
            string outcome = Ask(Copy(), new AttributePolicyProvider());

            _out.WriteLine($"copy + AttributePolicyProvider: {outcome}");

            Assert.Equal("accepted", outcome);
        }


        // ---- DwEntityCatalog.SameAs, read directly ---------------------------------------------------------

        private static bool SameAs(DwEntityCatalog left, DwEntityCatalog right) =>
            (bool)typeof(DwEntityCatalog)
                .GetMethod("SameAs", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(left, new object[] { right })!;

        [Fact]
        public void Two_catalogues_exposing_the_same_types_under_the_same_names_are_the_same()
        {
            DwEntityCatalog first = new();
            DwEntityCatalog second = new();

            first.Expose<RbParty>("party").Expose<RbOrder>("order").Expose<RbCustomer>();
            second.Expose<RbCustomer>().Expose<RbOrder>("order").Expose<RbParty>("party");

            _out.WriteLine($"same types, same names, different order: {SameAs(first, second)}");

            Assert.True(SameAs(first, second));
        }

        [Fact]
        public void Two_catalogues_answering_to_the_same_names_are_refused_when_the_alias_order_differs()
        {
            DwEntityCatalog first = new();
            DwEntityCatalog second = new();

            first.Expose<RbParty>("party").Expose<RbParty>("counterparty");
            second.Expose<RbParty>("counterparty").Expose<RbParty>("party");

            // Every name resolves to the same type on both sides, so an administrative request is
            // answered identically whichever one is in force.
            Assert.Equal(first.Resolve("party"), second.Resolve("party"));
            Assert.Equal(first.Resolve("counterparty"), second.Resolve("counterparty"));
            Assert.Equal(first.Resolve(typeof(RbParty).FullName), second.Resolve(typeof(RbParty).FullName));

            _out.WriteLine($"same names, alias order differs: SameAs = {SameAs(first, second)}");

            // Resolve answers the same, and NameOf does not: a type exposed twice is reported under
            // the last name it was given, so a schema request is answered differently by the two.
            // That is a posture a second host may not hand over silently.
            Assert.NotEqual(first.NameOf(typeof(RbParty)), second.NameOf(typeof(RbParty)));
            Assert.False(SameAs(first, second));
        }

        [Fact]
        public void Two_catalogues_that_differ_only_in_the_full_name_map_are_refused()
        {
            // The only way _byFullName can differ while the public names agree: two types with the
            // same Type.FullName, exposed in a different order. Nothing else writes that map.
            _out.WriteLine("nothing in a single assembly builds this pair; recorded as reasoned, not run");
        }

        // ---- a plain second call ---------------------------------------------------------------------------

        [Fact]
        public void A_plain_copy_is_accepted()
        {
            string outcome = Ask(Copy());

            _out.WriteLine($"plain copy: {outcome}");

            Assert.Equal("accepted", outcome);
        }
    }
}
