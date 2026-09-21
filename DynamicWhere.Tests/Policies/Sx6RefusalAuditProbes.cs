using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 6, adversarial security review of 3.3.0 at 7034717.
    //
    // Reviews round 5's fixes 2, 3 and 4 — AmbiguousGroupKey, TransformRequiresMaterialization and
    // MissingHashSalt/MissingTokenVault reporting "*" under Strict — from the side the fix did not
    // look at: what the AUDIT records once the caller-facing path is blanked, and whether the
    // posture the three new predicates read is the same posture the rest of the library reads.
    // =============================================================================================

    /// <summary>A generalized field a caller only ever names by its alias.</summary>
    public class Sx6Banded
    {
        public int Id { get; set; }

        [DwAlias("band")]
        [DwGeneralize(GeneralizeMode.Round, Step = 100)]
        [DwNoOrder]
        public decimal Payroll { get; set; }
    }

    /// <summary>A hashed identifier, so the missing-salt refusal can be reached.</summary>
    public class Sx6Hashed
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Hash)]
        [DwNoOrder]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>A tokenized identifier, so the missing-vault refusal can be reached.</summary>
    public class Sx6Tokenized
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Tokenize)]
        [DwNoOrder]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>A field denied outright, as the control case for what an audit records.</summary>
    public class Sx6Sealed
    {
        public int Id { get; set; }

        [DwDenied]
        public string Secret { get; set; } = string.Empty;
    }

    public sealed class Sx6RefusalAuditProbes
    {
        private readonly ITestOutputHelper _out;

        public Sx6RefusalAuditProbes(ITestOutputHelper output) => _out = output;

        // ---- harness ---------------------------------------------------------------------------

        private static DwPolicyContext Caller(bool dryRun = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            context.DryRun = dryRun;

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Audited(DwTier tier = DwTier.Strict, bool dryRun = false) =>
            new()
            {
                Tier = tier,
                DryRun = dryRun,
                AuditRefusals = true,
                Caps = { MinGroupSize = 1 }
            };

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

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing recorded)"
                : string.Join(
                    "; ",
                    context.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}:{e.ErrorCode?.ToString() ?? "-"}"));

        private static Summary ByBand() => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "band" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
                }
            }
        };

        private static Sx6Banded[] Colliding() => new[]
        {
            new Sx6Banded { Id = 1, Payroll = 100m },
            new Sx6Banded { Id = 2, Payroll = 149m }
        };

        // =========================================================================================
        // FINDING candidate. The audit loses the field with the caller-facing path.
        // =========================================================================================

        /// <summary>
        /// The control. A denied field's refusal names no field to the caller and the canonical path
        /// to the audit, which is the rule <c>PolicyException.AuditPath</c> is documented to keep.
        /// </summary>
        [Fact]
        public void A_field_denial_names_nothing_to_the_caller_and_the_path_to_the_audit()
        {
            DwPolicyContext context = Caller();

            Exception? error = Catch(() => Array.Empty<Sx6Sealed>().AsQueryable()
                .ApplyPolicy(context, Audited(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "Secret" } }));

            _out.WriteLine($"denied field : {Shape(error)}");
            _out.WriteLine($"   audit     : {Recorded(context)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal("*", refusal.FieldPath);

            // The audit is not the caller: it keeps the field that was probed.
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "Secret");
        }

        /// <summary>
        /// <c>MissingHashSalt</c> under Strict. Round 5 blanked the caller-facing path and set no
        /// <c>AuditPath</c>, so the audit is blanked with it.
        /// </summary>
        [Fact]
        public void Missing_hash_salt_records_the_field_in_the_audit_under_strict()
        {
            Sx6Hashed[] rows = { new() { Id = 1, NationalId = "AAA-111" } };

            DwPolicyContext strict = Caller();

            Exception? underStrict = Catch(() => rows.AsQueryable()
                .ApplyPolicy(strict, Audited(), Attributes()).ToList(new Filter()));

            DwPolicyContext convenience = Caller();

            Exception? underConvenience = Catch(() => rows.AsQueryable()
                .ApplyPolicy(convenience, Audited(DwTier.Convenience), Attributes()).ToList(new Filter()));

            _out.WriteLine($"strict      : {Shape(underStrict)}");
            _out.WriteLine($"   audit    : {Recorded(strict)}");
            _out.WriteLine($"convenience : {Shape(underConvenience)}");
            _out.WriteLine($"   audit    : {Recorded(convenience)}");

            Assert.Equal(
                PolicyErrorCode.MissingHashSalt,
                Assert.IsType<PolicyException>(underStrict).ErrorCode);

            // The convenience tier records the field a deployment failed to configure for.
            Assert.Contains(convenience.PendingAuditEvents, e => e.FieldPath == "NationalId");

            // Under Strict the same record says "*". Documented contract of AuditPath: "a record of
            // a refusal that cannot say which field was probed answers nothing".
            // The caller is told nothing; the audit keeps the field, which is what an audited refusal
            // is for.
            Assert.NotEmpty(strict.PendingAuditEvents);
            Assert.All(strict.PendingAuditEvents, e => Assert.NotEqual("*", e.FieldPath));
            Assert.NotEmpty(strict.PendingAuditEvents);
        }

        /// <summary><c>MissingTokenVault</c> behaves the same way.</summary>
        [Fact]
        public void Missing_token_vault_records_the_field_in_the_audit_under_strict()
        {
            Sx6Tokenized[] rows = { new() { Id = 1, NationalId = "AAA-111" } };

            DwPolicyContext strict = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(strict, Audited(), Attributes()).ToList(new Filter()));

            _out.WriteLine($"strict   : {Shape(error)}");
            _out.WriteLine($"  audit  : {Recorded(strict)}");

            Assert.Equal(
                PolicyErrorCode.MissingTokenVault,
                Assert.IsType<PolicyException>(error).ErrorCode);

            Assert.NotEmpty(strict.PendingAuditEvents);
            // The caller is told nothing; the audit keeps the field, which is what an audited refusal
            // is for.
            Assert.NotEmpty(strict.PendingAuditEvents);
            Assert.All(strict.PendingAuditEvents, e => Assert.NotEqual("*", e.FieldPath));
        }

        /// <summary><c>AmbiguousGroupKey</c> behaves the same way.</summary>
        [Fact]
        public void Ambiguous_group_key_records_the_field_in_the_audit_under_strict()
        {
            DwPolicyContext strict = Caller();

            Exception? underStrict = Catch(() => Colliding().AsQueryable()
                .ApplyPolicy(strict, Audited(), Attributes()).ToList(ByBand()));

            DwPolicyContext convenience = Caller();

            Exception? underConvenience = Catch(() => Colliding().AsQueryable()
                .ApplyPolicy(convenience, Audited(DwTier.Convenience), Attributes()).ToList(ByBand()));

            _out.WriteLine($"strict      : {Shape(underStrict)}");
            _out.WriteLine($"   audit    : {Recorded(strict)}");
            _out.WriteLine($"convenience : {Shape(underConvenience)}");
            _out.WriteLine($"   audit    : {Recorded(convenience)}");

            Assert.Equal(
                PolicyErrorCode.AmbiguousGroupKey,
                Assert.IsType<PolicyException>(underStrict).ErrorCode);

            Assert.Contains(convenience.PendingAuditEvents, e => e.FieldPath == "Payroll");

            Assert.NotEmpty(strict.PendingAuditEvents);
            // The caller is told nothing; the audit keeps the field, which is what an audited refusal
            // is for.
            Assert.NotEmpty(strict.PendingAuditEvents);
            Assert.All(strict.PendingAuditEvents, e => Assert.NotEqual("*", e.FieldPath));
        }

        /// <summary><c>TransformRequiresMaterialization</c> behaves the same way.</summary>
        [Fact]
        public void Transform_materialization_records_the_field_in_the_audit_under_strict()
        {
            DwPolicyContext strict = Caller();

            Exception? error = Catch(() => Array.Empty<Sx6Banded>().AsQueryable()
                .ApplyPolicy(strict, Audited(), Attributes())
                .SelectDynamic(new List<string> { "Id" }));

            _out.WriteLine($"strict   : {Shape(error)}");
            _out.WriteLine($"  audit  : {Recorded(strict)}");

            Assert.Equal(
                PolicyErrorCode.TransformRequiresMaterialization,
                Assert.IsType<PolicyException>(error).ErrorCode);

            // The caller is told nothing; the audit keeps the field, which is what an audited refusal
            // is for.
            Assert.NotEmpty(strict.PendingAuditEvents);
            Assert.All(strict.PendingAuditEvents, e => Assert.NotEqual("*", e.FieldPath));
        }

        /// <summary>
        /// An ambiguous name now reaches the audit as the caller spelled it, where before round 5 it
        /// was refused with the same spelling. Recorded for contrast: the field it stood for is in
        /// the trace, never in the audit.
        /// </summary>
        [Fact]
        public void An_ambiguous_name_is_audited_as_the_caller_wrote_it()
        {
            DwPolicyContext context = Caller();

            Exception? error = Catch(() => Array.Empty<Sx6Colliding>().AsQueryable()
                .ApplyPolicy(context, Audited(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "Salary" } }));

            _out.WriteLine($"ambiguous : {Shape(error)}");
            _out.WriteLine($"  audit   : {Recorded(context)}");

            Assert.IsType<PolicyException>(error);
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "Salary");
        }

        // =========================================================================================
        // FINDING candidate. The three new predicates read options.DryRun alone, where every other
        // decision in the library reads options.DryRun || context.DryRun.
        // =========================================================================================

        /// <summary>
        /// The rule the whole posture is built on: a dry run is the union of the global switch and
        /// the per-context one, so one canary subject can run unenforced.
        /// </summary>
        [Fact]
        public void A_context_dry_run_stops_the_gate_refusing()
        {
            DwPolicyContext canary = Caller(dryRun: true);

            Exception? error = Catch(() => Array.Empty<Sx6Sealed>().AsQueryable()
                .ApplyPolicy(canary, Audited(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "Secret" } }));

            _out.WriteLine($"canary, denied field : {Shape(error)}");

            Assert.Null(error);
        }

        /// <summary>
        /// The same canary meets <c>AmbiguousGroupKey</c>, <c>MissingHashSalt</c> and
        /// <c>TransformRequiresMaterialization</c>. All three still refuse, and all three hide the
        /// field although the tier's own rule for a dry run is to name it.
        /// </summary>
        [Fact]
        public void A_context_dry_run_is_a_dry_run_for_the_three_new_predicates()
        {
            List<string> observed = new();

            // (a) AmbiguousGroupKey.
            DwPolicyContext one = Caller(dryRun: true);

            Exception? group = Catch(() => Colliding().AsQueryable()
                .ApplyPolicy(one, Audited(), Attributes()).ToList(ByBand()));

            observed.Add($"context dry run, group key  : {Shape(group)}");

            // (b) MissingHashSalt.
            DwPolicyContext two = Caller(dryRun: true);

            Exception? salt = Catch(() => new[] { new Sx6Hashed { Id = 1, NationalId = "A" } }.AsQueryable()
                .ApplyPolicy(two, Audited(), Attributes()).ToList(new Filter()));

            observed.Add($"context dry run, salt       : {Shape(salt)}");

            // (c) TransformRequiresMaterialization.
            DwPolicyContext three = Caller(dryRun: true);

            Exception? materialize = Catch(() => Array.Empty<Sx6Banded>().AsQueryable()
                .ApplyPolicy(three, Audited(), Attributes())
                .SelectDynamic(new List<string> { "Id" }));

            observed.Add($"context dry run, transform  : {Shape(materialize)}");

            // The contrast: the same three under a GLOBAL dry run, which the fix does honour.
            Exception? groupGlobal = Catch(() => Colliding().AsQueryable()
                .ApplyPolicy(Caller(), Audited(dryRun: true), Attributes()).ToList(ByBand()));

            Exception? saltGlobal = Catch(() => new[] { new Sx6Hashed { Id = 1, NationalId = "A" } }.AsQueryable()
                .ApplyPolicy(Caller(), Audited(dryRun: true), Attributes()).ToList(new Filter()));

            Exception? materializeGlobal = Catch(() => Array.Empty<Sx6Banded>().AsQueryable()
                .ApplyPolicy(Caller(), Audited(dryRun: true), Attributes())
                .SelectDynamic(new List<string> { "Id" }));

            observed.Add($"global  dry run, group key  : {Shape(groupGlobal)}");
            observed.Add($"global  dry run, salt       : {Shape(saltGlobal)}");
            observed.Add($"global  dry run, transform  : {Shape(materializeGlobal)}");

            foreach (string line in observed)
            {
                _out.WriteLine(line);
            }

            // A global dry run names the field; the per-context one does not.
            Assert.Equal("Payroll", Assert.IsType<PolicyException>(groupGlobal).FieldPath);
            Assert.Equal("NationalId", Assert.IsType<PolicyException>(saltGlobal).FieldPath);
            Assert.Equal("Payroll", Assert.IsType<PolicyException>(materializeGlobal).FieldPath);

            // A dry run is either switch, the posture's or the caller's, as it is everywhere else.
            Assert.Equal("Payroll", Assert.IsType<PolicyException>(group).FieldPath);
            Assert.Equal("NationalId", Assert.IsType<PolicyException>(salt).FieldPath);
            Assert.Equal("Payroll", Assert.IsType<PolicyException>(materialize).FieldPath);
        }

        // =========================================================================================
        // The composite group key, after the raw NUL and unit separator became escapes.
        // =========================================================================================

        /// <summary>A transformer that hands back exactly what it was given.</summary>
        public sealed class Sx6Echo : IValueTransformer
        {
            public object? Transform(object? value, DwTransformContext context) => value;
        }

        /// <summary>Two grouping keys, both transformed, so both join the composite.</summary>
        public class Sx6Pair
        {
            public int Id { get; set; }

            [DwMutate(typeof(Sx6Echo))]
            [DwNoOrder]
            public string A { get; set; } = string.Empty;

            [DwMutate(typeof(Sx6Echo))]
            [DwNoOrder]
            public string B { get; set; } = string.Empty;
        }

        private static Summary ByPair() => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "A", "B" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
                }
            }
        };

        /// <summary>Two genuinely distinct key pairs still group and answer.</summary>
        [Fact]
        public void Distinct_composite_keys_are_not_a_collision()
        {
            Sx6Pair[] rows =
            {
                new() { Id = 1, A = "x", B = "y" },
                new() { Id = 2, A = "p", B = "q" }
            };

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), Audited(), Attributes()).ToList(ByPair()));

            _out.WriteLine($"distinct : {Shape(error)}");

            Assert.Null(error);
        }

        /// <summary>
        /// The separator is a value a caller can put in a column. Two distinct key pairs whose parts
        /// straddle it build one composite, and the summary is refused for a collision that is not
        /// one. Unchanged by round 5 — the escape is the same character — and recorded so the
        /// property is on the record.
        /// </summary>
        [Fact]
        public void A_value_holding_the_separator_invents_a_collision()
        {
            // Built from its code point rather than written as a literal: a raw unit separator
            // in a source file is what made ResultTransformer.cs read as binary to grep.
            string Separator = ((char)0x1F).ToString();

            Sx6Pair[] rows =
            {
                new() { Id = 1, A = "x" + Separator + "y", B = "z" },
                new() { Id = 2, A = "x", B = "y" + Separator + "z" }
            };

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), Audited(), Attributes()).ToList(ByPair()));

            _out.WriteLine($"straddling separator : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, refusal.ErrorCode);
        }

        /// <summary>
        /// The null stand-in is a value a caller can put in a column too: a group whose key is null
        /// and a group whose key is a lone NUL build the same composite.
        /// </summary>
        [Fact]
        public void A_value_holding_the_null_stand_in_invents_a_collision()
        {
            string Nul = ((char)0x00).ToString();

            Sx6Pair[] rows =
            {
                new() { Id = 1, A = null!, B = "z" },
                new() { Id = 2, A = Nul, B = "z" }
            };

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), Audited(), Attributes()).ToList(ByPair()));

            _out.WriteLine($"null vs NUL : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, refusal.ErrorCode);
        }

        /// <summary>
        /// The direction that would matter: two rows that really do share a key are still caught.
        /// A missed collision would return a summary with duplicate keys whose counts do not add up.
        /// </summary>
        [Fact]
        public void A_real_collision_is_still_caught()
        {
            Exception? error = Catch(() => Colliding().AsQueryable()
                .ApplyPolicy(Caller(), Audited(), Attributes()).ToList(ByBand()));

            _out.WriteLine($"real collision : {Shape(error)}");

            Assert.Equal(
                PolicyErrorCode.AmbiguousGroupKey,
                Assert.IsType<PolicyException>(error).ErrorCode);
        }
    }
}
