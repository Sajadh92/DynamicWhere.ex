using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// A second <c>DwPolicy.Configure</c> asking for the posture already in force.
    /// </summary>
    /// <remarks>
    /// An integration suite starts many hosts over one composition root, and each one runs the
    /// registration again. Refusing the second host made every such suite write its own
    /// <c>IsConfigured</c> check, which is a check-then-act two hosts can both pass — so the package
    /// answers it, inside the same lock that does the configuring.
    /// <para>
    /// The posture in this process is whatever configured it first, so every case here builds its
    /// candidate from <c>DwPolicy.Options</c> rather than from a fixed set of values. A case that
    /// expects a refusal changes nothing: the refusal happens before anything is installed.
    /// </para>
    /// </remarks>
    public sealed class ConfigureOnceTests
    {
        public ConfigureOnceTests() => Ensure();

        /// <summary>
        /// Configures the assembly's one posture, which every suite here asks for.
        /// </summary>
        /// <remarks>
        /// Calling it from each test is the feature under test: before 3.3.0 only the first caller
        /// could do this, and the cases below all read what is in force rather than assuming a
        /// posture of their own.
        /// </remarks>
        private static void Ensure() => PolicyBootstrap.Ensure();

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

        [Fact]
        public void The_same_posture_configured_again_changes_nothing()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            DwPolicy.Configure(Copy());

            Assert.Same(inForce, DwPolicy.Options);
            Assert.True(DwPolicy.IsConfigured);
        }

        [Fact]
        public void The_posture_handed_to_a_second_call_is_frozen_rather_than_left_settable()
        {
            DwPolicyOptions second = Copy();

            DwPolicy.Configure(second);

            Assert.True(second.IsFrozen);
            Assert.Throws<InvalidOperationException>(() => second.Tier = DwTier.Convenience);
        }

        [Fact]
        public void A_different_tier_is_still_refused()
        {
            DwPolicyOptions different = Copy();

            different.Tier = DwPolicy.Options.Tier == DwTier.Strict ? DwTier.Convenience : DwTier.Strict;

            Refused(different);
        }

        [Fact]
        public void A_different_cap_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.Caps.MaxPageSize = DwPolicy.Options.Caps.MaxPageSize - 1;

            Refused(different);
        }

        [Fact]
        public void A_different_group_floor_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.Caps.MinGroupSize = DwPolicy.Options.Caps.MinGroupSize == 7 ? 8 : 7;

            Refused(different);
        }

        [Fact]
        public void A_different_dry_run_flag_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.DryRun = !DwPolicy.Options.DryRun;

            Refused(different);
        }

        [Fact]
        public void A_different_refusal_audit_flag_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.AuditRefusals = !DwPolicy.Options.AuditRefusals;

            Refused(different);
        }

        [Fact]
        public void A_different_snapshot_age_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.MaxSnapshotAge = DwPolicy.Options.MaxSnapshotAge + TimeSpan.FromMinutes(1);

            Refused(different);
        }

        [Fact]
        public void A_different_refresh_interval_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.RefreshInterval = DwPolicy.Options.RefreshInterval + TimeSpan.FromSeconds(1);

            Refused(different);
        }

        [Fact]
        public void A_different_store_failure_mode_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.StoreFailure = DwPolicy.Options.StoreFailure == StoreFailureMode.FailClosed
                ? StoreFailureMode.LastKnownGood
                : StoreFailureMode.FailClosed;

            Refused(different);
        }

        [Fact]
        public void A_different_hash_salt_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.HashSalt = new string('s', DwPolicyOptions.MinimumHashSaltLength + 1);

            Refused(different);
        }

        [Fact]
        public void A_catalogue_exposing_one_more_type_is_refused()
        {
            DwPolicyOptions different = Copy();

            different.Entities.Expose<ConfigureOnceOnlyHere>("configure-once-only-here");

            Refused(different);
        }

        [Fact]
        public void A_catalogue_exposing_a_type_under_another_name_is_refused()
        {
            // The floor leg runs this suite alone, where nothing has exposed anything, so the
            // difference has to be made rather than found.
            DwPolicyOptions different = Copy();

            if (DwPolicy.Options.Entities.IsEmpty)
            {
                different.Entities.Expose<ConfigureOnceOnlyHere>("under-one-name");

                Refused(different);

                return;
            }

            KeyValuePair<Type, string> first = DwPolicy.Options.Entities.Entities.First();

            different = new DwPolicyOptions { Tier = DwPolicy.Options.Tier };

            foreach (KeyValuePair<Type, string> exposed in DwPolicy.Options.Entities.Entities)
            {
                different.Entities.Expose(
                    exposed.Key, exposed.Key == first.Key ? $"{exposed.Value}-renamed" : exposed.Value);
            }

            Refused(different);
        }

        [Fact]
        public void One_more_policy_source_is_refused()
        {
            Refused(Copy(), new FakePolicyProvider());
        }

        [Fact]
        public async Task Many_hosts_starting_at_once_all_get_through()
        {
            // The race the package now answers: two hosts reading IsConfigured as false and both
            // configuring. Nothing here takes a lock of its own, which is the point.
            Task[] hosts = Enumerable
                .Range(0, 16)
                .Select(_ => Task.Run(() => DwPolicy.Configure(Copy())))
                .ToArray();

            await Task.WhenAll(hosts);

            Assert.True(DwPolicy.IsConfigured);
        }

        [Fact]
        public void A_second_registration_hands_the_container_the_posture_in_force()
        {
            IServiceCollection services = new ServiceCollection();
            IConfiguration section = new ConfigurationBuilder().Build().GetSection("DynamicWhere:Policies");

            services.AddDwPolicies(section, options =>
            {
                options.Tier = DwPolicy.Options.Tier;

                foreach (KeyValuePair<Type, string> exposed in DwPolicy.Options.Entities.Entities)
                {
                    options.Entities.Expose(exposed.Key, exposed.Value);
                }
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Same(DwPolicy.Options, provider.GetRequiredService<DwPolicyOptions>());
        }

        private static void Refused(DwPolicyOptions different, params IDwPolicyProvider[] providers)
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            InvalidOperationException refusal =
                Assert.Throws<InvalidOperationException>(() => DwPolicy.Configure(different, providers));

            Assert.Contains("already configured", refusal.Message);
            Assert.Same(inForce, DwPolicy.Options);
        }
    }

    /// <summary>A type nothing else exposes, so exposing it is a difference by itself.</summary>
    public sealed class ConfigureOnceOnlyHere
    {
        public int Id { get; set; }
    }
}
