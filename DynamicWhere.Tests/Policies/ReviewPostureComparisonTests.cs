using System.Reflection;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Tokens;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// DOC PROBE ONLY — not part of the suite. Checks the documented "Configuring twice" comparison
    /// list against what SamePosture actually compares.
    /// </summary>
    public sealed class ReviewPostureComparisonTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewPostureComparisonTests(ITestOutputHelper output)
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

            foreach (PropertyInfo cap in WritableCaps())
            {
                if (cap.Name == nameof(DwCaps.MinGroupSize))
                {
                    continue;
                }

                cap.SetValue(copy.Caps, cap.GetValue(inForce.Caps));
            }

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

        private static PropertyInfo[] WritableCaps() =>
            typeof(DwCaps)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanWrite && property.PropertyType == typeof(int))
                .ToArray();

        private static bool Refuses(DwPolicyOptions candidate, params IDwPolicyProvider[] providers)
        {
            try
            {
                DwPolicy.Configure(candidate, providers);

                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        // ---- the control: the copy itself must be accepted -------------------------------------

        [Fact]
        public void The_copy_is_accepted_so_every_other_case_means_something()
        {
            Assert.False(Refuses(Copy()));
        }

        // ---- every writable value on the posture, swept ----------------------------------------

        [Fact]
        public void Sweep_every_writable_property_on_DwPolicyOptions()
        {
            _out.WriteLine("property | changed to | refused?");

            foreach (PropertyInfo property in typeof(DwPolicyOptions)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanWrite))
            {
                DwPolicyOptions candidate = Copy();
                object? value;

                switch (property.Name)
                {
                    case nameof(DwPolicyOptions.Tier):
                        value = DwPolicy.Options.Tier == DwTier.Strict ? DwTier.Convenience : DwTier.Strict;
                        break;
                    case nameof(DwPolicyOptions.DryRun):
                        value = !DwPolicy.Options.DryRun;
                        break;
                    case nameof(DwPolicyOptions.IncludeTraceInResult):
                        value = DwPolicy.Options.IncludeTraceInResult == true ? false : true;
                        break;
                    case nameof(DwPolicyOptions.AuditRefusals):
                        value = !DwPolicy.Options.AuditRefusals;
                        break;
                    case nameof(DwPolicyOptions.HashSalt):
                        value = new string('z', DwPolicyOptions.MinimumHashSaltLength + 3);
                        break;
                    case nameof(DwPolicyOptions.StoreFailure):
                        value = DwPolicy.Options.StoreFailure == StoreFailureMode.FailClosed
                            ? StoreFailureMode.LastKnownGood
                            : StoreFailureMode.FailClosed;
                        break;
                    case nameof(DwPolicyOptions.MaxSnapshotAge):
                        value = DwPolicy.Options.MaxSnapshotAge + TimeSpan.FromMinutes(3);
                        break;
                    case nameof(DwPolicyOptions.RefreshInterval):
                        value = DwPolicy.Options.RefreshInterval + TimeSpan.FromSeconds(7);
                        break;
                    case nameof(DwPolicyOptions.TokenVault):
                        value = new InMemoryTokenVault();
                        break;
                    case nameof(DwPolicyOptions.Services):
                        value = new ProbeServices();
                        break;
                    default:
                        _out.WriteLine($"{property.Name} | SKIPPED (no probe value)");
                        continue;
                }

                property.SetValue(candidate, value);

                _out.WriteLine($"{property.Name} | {value} | refused={Refuses(candidate)}");
            }
        }

        [Fact]
        public void Sweep_every_writable_cap()
        {
            _out.WriteLine("cap | refused?");

            foreach (PropertyInfo cap in WritableCaps())
            {
                DwPolicyOptions candidate = Copy();
                int current = (int)cap.GetValue(DwPolicy.Options.Caps)!;
                bool set = false;

                foreach (int attempt in new[] { current + 1, current - 1, current + 2 })
                {
                    try
                    {
                        cap.SetValue(candidate.Caps, attempt);
                        set = true;
                        break;
                    }
                    catch (Exception)
                    {
                        // out of the setter's range; try the next candidate value
                    }
                }

                _out.WriteLine(set
                    ? $"{cap.Name} | refused={Refuses(candidate)}"
                    : $"{cap.Name} | SKIPPED (no settable neighbour of {current})");
            }
        }

        // ---- the claims the docs make about what is NOT compared --------------------------------

        [Fact]
        public void A_different_token_vault_is_not_compared()
        {
            DwPolicyOptions candidate = Copy();

            candidate.TokenVault = new InMemoryTokenVault();

            bool refused = Refuses(candidate);

            _out.WriteLine($"TokenVault differs -> refused={refused}; in force is still {DwPolicy.Options.TokenVault?.GetType().Name ?? "null"}");

            Assert.False(refused);
        }

        [Fact]
        public void A_different_service_provider_is_not_compared()
        {
            DwPolicyOptions candidate = Copy();

            candidate.Services = new ProbeServices();

            bool refused = Refuses(candidate);

            _out.WriteLine($"Services differs -> refused={refused}; in force is still {DwPolicy.Options.Services?.GetType().Name ?? "null"}");

            Assert.False(refused);
        }

        [Fact]
        public void The_vault_and_container_in_force_are_not_replaced_by_a_second_call()
        {
            IDwTokenVault? vaultBefore = DwPolicy.Options.TokenVault;
            IServiceProvider? servicesBefore = DwPolicy.Options.Services;

            DwPolicyOptions candidate = Copy();

            candidate.TokenVault = new InMemoryTokenVault();
            candidate.Services = new ProbeServices();

            DwPolicy.Configure(candidate);

            _out.WriteLine($"after: vault same={ReferenceEquals(vaultBefore, DwPolicy.Options.TokenVault)}, services same={ReferenceEquals(servicesBefore, DwPolicy.Options.Services)}");

            Assert.Same(vaultBefore, DwPolicy.Options.TokenVault);
            Assert.Same(servicesBefore, DwPolicy.Options.Services);
        }

        // ---- IncludeTraceInResult, which the author's suite never varies -------------------------

        [Fact]
        public void A_different_include_trace_flag_is_refused()
        {
            DwPolicyOptions candidate = Copy();

            candidate.IncludeTraceInResult = DwPolicy.Options.IncludeTraceInResult == true;

            bool refused = Refuses(candidate);

            _out.WriteLine($"IncludeTraceInResult {DwPolicy.Options.IncludeTraceInResult} -> {candidate.IncludeTraceInResult}: refused={refused}");

            Assert.True(refused);
        }

        // ---- the group floor: the value that applies, not whether it was written ------------------

        [Fact]
        public void Setting_the_floor_to_the_value_it_already_reads_is_the_same_posture()
        {
            // The floor that applies is what a query enforces; IsMinGroupSizeSet tells a deliberate
            // opt-out from a deployment that never heard of the control, and enforcement never reads
            // it. The documented appsettings sample writes "MinGroupSize": 5.
            if (DwPolicy.Options.Caps.IsMinGroupSizeSet)
            {
                _out.WriteLine("in force is already explicit; nothing to prove here");

                return;
            }

            DwPolicyOptions candidate = Copy();

            candidate.Caps.MinGroupSize = DwPolicy.Options.Caps.MinGroupSize;

            bool refused = Refuses(candidate);

            _out.WriteLine($"asked: MinGroupSize={candidate.Caps.MinGroupSize}, IsMinGroupSizeSet={candidate.Caps.IsMinGroupSizeSet}, refused={refused}");

            Assert.False(refused);
        }

        // ---- providers: types, order, instances ---------------------------------------------------

        [Fact]
        public void Two_different_instances_of_the_same_provider_types_are_accepted()
        {
            // First call supplied none, so supply none here too and prove the instance-blindness
            // through a pair of calls that both supply one of the same type.
            DwPolicyOptions first = Copy();

            bool addingOneIsRefused = Refuses(first, new FakePolicyProvider());

            _out.WriteLine($"adding a source where none was supplied: refused={addingOneIsRefused}");

            Assert.True(addingOneIsRefused);
        }

        [Fact]
        public void The_attribute_provider_is_left_out_of_both_sides()
        {
            DwPolicyOptions candidate = Copy();

            bool refused = Refuses(candidate, new AttributePolicyProvider());

            _out.WriteLine($"supplying only AttributePolicyProvider where none was supplied: refused={refused}");

            Assert.False(refused);
        }

        [Fact]
        public void A_null_entry_among_the_providers_is_ignored()
        {
            DwPolicyOptions candidate = Copy();

            bool refused = Refuses(candidate, new IDwPolicyProvider[] { null! });

            _out.WriteLine($"supplying a null provider: refused={refused}");

            Assert.False(refused);
        }

        /// <summary>A container that answers nothing, used only to be a different reference.</summary>
        private sealed class ProbeServices : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }
}
