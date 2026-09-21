using System.Reflection;
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
using DynamicWhere.ex.Policies.Tokens;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A nested branch whose member carries the alias, so the alias names a deep path.</summary>
    public class Ar7Branch
    {
        public int Id { get; set; }

        [DwAlias("org")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A division holding the branch, so an alias can name a path two navigations deep.</summary>
    public class Ar7Division
    {
        public int Id { get; set; }

        public Ar7Branch? Branch { get; set; }
    }

    /// <summary>
    /// A root whose alias resolves to a path deeper than a lowered navigation cap allows, while a
    /// name that matches nothing stays one segment long.
    /// </summary>
    public class Ar7Aliased
    {
        public int Id { get; set; }

        public Ar7Division? Division { get; set; }

        public string Tag { get; set; } = string.Empty;
    }

    public sealed class Ar7OracleProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7OracleProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller()
            => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|origin={refusal.SourceOrigin ?? "-"}",
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

        private static Exception? Ask(string name, int depth)
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict, AuditRefusals = true };

            options.Caps.MinGroupSize = 1;
            options.Caps.MaxNavigationDepth = depth;

            Ar7Aliased[] rows = { new() { Id = 1, Tag = "t" } };

            return Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), options, Attributes())
                .ToList(new Filter
                {
                    Orders = new List<OrderBy>
                    {
                        new() { Sort = 0, Field = name, Direction = Direction.Ascending }
                    }
                }));
        }

        /// <summary>
        /// CANDIDATE. Under Strict a name that matches nothing and a name that does must be refused
        /// alike. The navigation cap is measured on the canonical path, which an alias hides, so a
        /// one-token alias standing for a deep path is refused with a different code from a
        /// one-token name standing for nothing.
        /// </summary>
        [Fact]
        public void An_alias_and_an_unknown_name_are_refused_alike_under_the_navigation_cap()
        {
            // 'org' stands for Division.Branch.Name, three segments; 'zzz' stands for nothing and
            // stays one. Both are one token as the caller writes them.
            Exception? alias = Ask("org", depth: 2);
            Exception? unknown = Ask("zzz", depth: 2);
            Exception? real = Ask("Id", depth: 2);

            _out.WriteLine($"alias 'org' (Division.Branch.Name) : {Shape(alias)}");
            _out.WriteLine($"unknown 'zzz'                      : {Shape(unknown)}");
            _out.WriteLine($"real 'Id'                          : {Shape(real)}");

            Exception? deepAlias = Ask("Division.Branch.Name", depth: 2);
            Exception? deepUnknown = Ask("Zzz.Yyy.Xxx", depth: 2);

            _out.WriteLine($"real deep path                : {Shape(deepAlias)}");
            _out.WriteLine($"unknown of the same shape     : {Shape(deepUnknown)}");

            // A name that matches nothing and a real one of the SAME WRITTEN SHAPE read alike:
            // Unknown() collapses the padding so both count three segments. That half holds.
            Assert.Equal(
                (deepAlias as PolicyException)?.ErrorCode,
                (deepUnknown as PolicyException)?.ErrorCode);

            // A one-token alias is answered the same way. The cap is measured on the canonical path
            // the alias stands for, and a name matching nothing stays one segment, so answering with
            // the cap's own code would tell the caller their token named something several
            // navigations deep. A caller who wrote the path themselves still meets the cap.
            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, ((PolicyException)alias!).ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, ((PolicyException)unknown!).ErrorCode);
        }

        /// <summary>
        /// The cap's origin is returned under Strict. It must not describe anything the caller did
        /// not write.
        /// </summary>
        [Fact]
        public void The_cap_origin_says_nothing_about_a_name_the_caller_did_not_write()
        {
            Exception? alias = Ask("org", depth: 2);
            Exception? unknown = Ask("zzz", depth: 2);

            _out.WriteLine($"alias   : {Shape(alias)}");
            _out.WriteLine($"unknown : {Shape(unknown)}");

            // It says nothing: the origin used to state the navigation depth of a canonical path
            // the caller wrote as one token, while the name matching nothing was given none.
            Assert.Null(((PolicyException)alias!).SourceOrigin);
            Assert.Null(((PolicyException)unknown!).SourceOrigin);
        }

        // =========================================================================================
        // A posture a second host hands over.
        // =========================================================================================

        /// <summary>
        /// Every value of the posture that decides what a query may do has to be compared, or a
        /// second host's configuration is accepted as the same and enforces differently.
        /// </summary>
        [Fact]
        public void Every_posture_value_is_compared_by_the_same_posture_check()
        {
            MethodInfo same = typeof(DwPolicy)
                .GetMethod("SamePosture", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Documented as deliberately uncompared: objects a host builds for itself.
            HashSet<string> exempt = new(StringComparer.Ordinal)
            {
                "TokenVault", "Services", "IsFrozen", "IncludeTraceInResult"
            };

            List<string> uncompared = new();

            foreach (PropertyInfo property in typeof(DwPolicyOptions)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (exempt.Contains(property.Name))
                {
                    continue;
                }

                uncompared.Add(property.Name);
            }

            _out.WriteLine("posture properties compared or exempt must cover: "
                           + string.Join(", ", uncompared));

            Assert.NotNull(same);
        }

        /// <summary>
        /// The concrete shape: a second host handing over a posture that differs in any cap is
        /// refused rather than accepted, so the caps a query is measured against cannot be swapped.
        /// </summary>
        [Fact]
        public void A_cap_that_differs_is_refused_by_the_same_posture_check()
        {
            MethodInfo same = typeof(DwPolicy)
                .GetMethod("SamePosture", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Two identical postures must read as the same, or every comparison below is vacuously
            // "different" and the probe proves nothing.
            Assert.True((bool)same.Invoke(
                null,
                new object?[] { new DwPolicyOptions(), new DwPolicyOptions(), Array.Empty<IDwPolicyProvider>() })!);

            List<string> accepted = new();

            foreach (PropertyInfo cap in typeof(DwCaps)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.PropertyType == typeof(int) && p.CanWrite))
            {
                DwPolicyOptions inForce = new();
                DwPolicyOptions asked = new();

                int baseline = (int)cap.GetValue(inForce.Caps)!;

                cap.SetValue(asked.Caps, baseline + 1);

                bool agreed = (bool)same.Invoke(
                    null, new object?[] { inForce, asked, Array.Empty<IDwPolicyProvider>() })!;

                _out.WriteLine($"{cap.Name,-22} {baseline} vs {baseline + 1} -> same? {agreed}");

                if (agreed)
                {
                    accepted.Add(cap.Name);
                }
            }

            Assert.Empty(accepted);
        }

        /// <summary>The same for every non-cap value of the posture.</summary>
        [Fact]
        public void A_posture_value_that_differs_is_refused_by_the_same_posture_check()
        {
            MethodInfo same = typeof(DwPolicy)
                .GetMethod("SamePosture", BindingFlags.NonPublic | BindingFlags.Static)!;

            List<(string Name, Action<DwPolicyOptions> Change)> changes = new()
            {
                ("Tier", o => o.Tier = DwTier.Strict),
                ("DryRun", o => o.DryRun = true),
                ("IncludeTraceInResult", o => o.IncludeTraceInResult = true),
                ("IncludeTraceInResult(false)", o => o.IncludeTraceInResult = false),
                ("AuditRefusals", o => o.AuditRefusals = true),
                ("HashSalt", o => o.HashSalt = new string('s', 32)),
                ("StoreFailure", o => o.StoreFailure = StoreFailureMode.FailClosed),
                ("MaxSnapshotAge", o => o.MaxSnapshotAge = TimeSpan.FromHours(3)),
                ("RefreshInterval", o => o.RefreshInterval = TimeSpan.FromHours(3)),
                ("TokenVault", o => o.TokenVault = new InMemoryTokenVault()),
                ("Entities", o => o.Entities.Expose<Ar7Aliased>("aliased"))
            };

            List<string> accepted = new();

            foreach ((string name, Action<DwPolicyOptions> change) in changes)
            {
                DwPolicyOptions inForce = new();
                DwPolicyOptions asked = new();

                change(asked);

                bool agreed = (bool)same.Invoke(
                    null, new object?[] { inForce, asked, Array.Empty<IDwPolicyProvider>() })!;

                _out.WriteLine($"{name,-22} differs -> same? {agreed}");

                if (agreed)
                {
                    accepted.Add(name);
                }
            }

            _out.WriteLine("accepted although different: " + (accepted.Count == 0 ? "-" : string.Join(", ", accepted)));

            // TokenVault is documented as deliberately uncompared; IncludeTraceInResult is compared
            // by the value that applies, and true is the Convenience tier's own answer.
            Assert.Equal(new[] { "IncludeTraceInResult", "TokenVault" }, accepted.OrderBy(a => a, StringComparer.Ordinal).ToArray());
        }
    }
}
