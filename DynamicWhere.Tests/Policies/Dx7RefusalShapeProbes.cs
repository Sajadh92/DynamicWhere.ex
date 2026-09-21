using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
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
    // Round 7, documentation review of 3.3.0 at 893cadc.
    //
    // Claim 2: "The four refusals that report FieldPath "*" under Strict: AmbiguousGroupKey,
    // MissingHashSalt, MissingTokenVault carry no SourceOrigin; TransformRequiresMaterialization
    // keeps an origin naming the method, and its list is every transformed column, not every masked
    // one. An ambiguous name is refused as an unknown name is. All four honour a dry run declared by
    // either switch, and all four still record the real field through AuditRefusals."
    // =============================================================================================

    /// <summary>A generalized column a caller names only by its alias.</summary>
    public class Dx7Banded
    {
        public int Id { get; set; }

        [DwAlias("band")]
        [DwGeneralize(GeneralizeMode.Round, Step = 100)]
        [DwNoOrder]
        public decimal Payroll { get; set; }
    }

    /// <summary>Hashed, so the missing-salt refusal is reachable.</summary>
    public class Dx7Hashed
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Hash)]
        [DwNoOrder]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>Tokenized, so the missing-vault refusal is reachable.</summary>
    public class Dx7Tokenized
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Tokenize)]
        [DwNoOrder]
        public string NationalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// One column of each transform kind, so the materialization refusal's list can be read for
    /// what it actually contains.
    /// </summary>
    public class Dx7EveryTransform
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Full)]
        [DwNoOrder]
        public string Masked { get; set; } = string.Empty;

        [DwGeneralize(GeneralizeMode.Round, Step = 10)]
        [DwNoOrder]
        public decimal Generalized { get; set; }

        [DwTruncate(3)]
        [DwNoOrder]
        public string Truncated { get; set; } = string.Empty;

        [DwFormat("0.00")]
        [DwNoOrder]
        public decimal Formatted { get; set; }

        /// <summary>Nothing at all, so the list is not simply "every member".</summary>
        public string Plain { get; set; } = string.Empty;
    }

    /// <summary>Two members answering to one public name.</summary>
    public class Dx7Ambiguous
    {
        public int Id { get; set; }

        [DwAlias("label")]
        public string First { get; set; } = string.Empty;

        [DwAlias("label")]
        public string Second { get; set; } = string.Empty;
    }

    public sealed class Dx7RefusalShapeProbes
    {
        private readonly ITestOutputHelper _out;

        public Dx7RefusalShapeProbes(ITestOutputHelper output) => _out = output;

        // ---- harness ---------------------------------------------------------------------------

        private static DwPolicyContext Caller(bool dryRun = false)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            context.DryRun = dryRun;

            return context;
        }

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(
            DwTier tier = DwTier.Strict, bool dryRun = false, bool auditRefusals = false) =>
            new()
            {
                Tier = tier,
                DryRun = dryRun,
                AuditRefusals = auditRefusals,
                Caps = { MinGroupSize = 1 }
            };

        private static PolicyException Refusal(Action run)
        {
            try
            {
                run();
            }
            catch (PolicyException refusal)
            {
                return refusal;
            }

            throw new Xunit.Sdk.XunitException("the request was not refused");
        }

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

        private static string Shape(PolicyException r) =>
            $"{r.ErrorCode}|path={r.FieldPath}|feature={r.Feature}|origin={r.SourceOrigin ?? "-"}";

        private static string Recorded(DwPolicyContext context) =>
            context.PendingAuditEvents.Count == 0
                ? "(nothing)"
                : string.Join(
                    "; ",
                    context.PendingAuditEvents.Select(e => $"{e.FieldPath}:{e.Feature}:{e.ErrorCode?.ToString() ?? "-"}"));

        // ---- the four requests -------------------------------------------------------------------

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

        private static void GroupKeyCollision(DwPolicyContext context, DwPolicyOptions options) =>
            new[]
                {
                    new Dx7Banded { Id = 1, Payroll = 100m },
                    new Dx7Banded { Id = 2, Payroll = 149m }
                }
                .AsQueryable()
                .ApplyPolicy(context, options, Attributes())
                .ToList(ByBand());

        private static void HashWithNoSalt(DwPolicyContext context, DwPolicyOptions options) =>
            new[] { new Dx7Hashed { Id = 1, NationalId = "x" } }
                .AsQueryable()
                .ApplyPolicy(context, options, Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "NationalId" } });

        private static void TokenizeWithNoVault(DwPolicyContext context, DwPolicyOptions options) =>
            new[] { new Dx7Tokenized { Id = 1, NationalId = "x" } }
                .AsQueryable()
                .ApplyPolicy(context, options, Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "NationalId" } });

        private static void Unmaterialized(DwPolicyContext context, DwPolicyOptions options) =>
            new[] { new Dx7EveryTransform { Id = 1 } }
                .AsQueryable()
                .ApplyPolicy(context, options, Attributes())
                .SelectDynamic(new List<string> { "Id" });

        private static readonly (string Name, PolicyErrorCode Code, Action<DwPolicyContext, DwPolicyOptions> Run)[] Four =
        {
            ("AmbiguousGroupKey", PolicyErrorCode.AmbiguousGroupKey, GroupKeyCollision),
            ("MissingHashSalt", PolicyErrorCode.MissingHashSalt, HashWithNoSalt),
            ("MissingTokenVault", PolicyErrorCode.MissingTokenVault, TokenizeWithNoVault),
            ("TransformRequiresMaterialization", PolicyErrorCode.TransformRequiresMaterialization, Unmaterialized)
        };

        // =========================================================================================
        // Under Strict: "*", and an origin only on the transform refusal.
        // =========================================================================================

        [Fact]
        public void All_four_report_star_under_strict_and_only_one_keeps_an_origin()
        {
            foreach ((string name, PolicyErrorCode code, Action<DwPolicyContext, DwPolicyOptions> run) in Four)
            {
                DwPolicyContext context = Caller();
                PolicyException refusal = Refusal(() => run(context, Options()));

                _out.WriteLine($"[strict] {name}: {Shape(refusal)}");

                Assert.Equal(code, refusal.ErrorCode);
                Assert.Equal("*", refusal.FieldPath);

                if (code == PolicyErrorCode.TransformRequiresMaterialization)
                {
                    // Kept, and it names the method and what to call instead, never a field.
                    Assert.NotNull(refusal.SourceOrigin);
                    Assert.Contains(nameof(PolicyQueryable<Dx7EveryTransform>.SelectDynamic), refusal.SourceOrigin!);
                    Assert.Contains("ToListDynamic", refusal.SourceOrigin!);
                    Assert.DoesNotContain("Masked", refusal.SourceOrigin!);
                }
                else
                {
                    Assert.Null(refusal.SourceOrigin);
                }
            }
        }

        /// <summary>
        /// The transform refusal's list is every <em>transformed</em> column, not every masked one.
        /// </summary>
        /// <remarks>
        /// Read on the convenience tier, where the list is the caller-facing path, and again from
        /// the strict refusal's audit path, where it is what reaches a sink.
        /// </remarks>
        [Fact]
        public void The_transform_refusal_lists_every_transformed_column_not_every_masked_one()
        {
            DwPolicyContext lenient = Caller();
            PolicyException open = Refusal(() => Unmaterialized(lenient, Options(DwTier.Convenience)));

            _out.WriteLine($"[convenience] path = {open.FieldPath}");

            foreach (string column in new[] { "Masked", "Generalized", "Truncated", "Formatted" })
            {
                Assert.Contains(column, open.FieldPath);
            }

            // A column with no transform at all is not in the list.
            Assert.DoesNotContain("Plain", open.FieldPath);

            DwPolicyContext strict = Caller();

            Assert.Throws<PolicyException>(() => Unmaterialized(strict, Options(auditRefusals: true)));

            _out.WriteLine($"[strict, audited] {Recorded(strict)}");

            string audited = Assert.Single(strict.PendingAuditEvents).FieldPath;

            foreach (string column in new[] { "Masked", "Generalized", "Truncated", "Formatted" })
            {
                Assert.Contains(column, audited);
            }
        }

        // =========================================================================================
        // A dry run, declared by either switch, names the field again.
        // =========================================================================================

        [Fact]
        public void All_four_honour_a_dry_run_declared_by_either_switch()
        {
            foreach ((string name, PolicyErrorCode code, Action<DwPolicyContext, DwPolicyOptions> run) in Four)
            {
                foreach ((string leg, DwPolicyContext context, DwPolicyOptions options) in new[]
                {
                    ("posture switch", Caller(), Options(dryRun: true)),
                    ("caller switch", Caller(dryRun: true), Options())
                })
                {
                    PolicyException refusal = Refusal(() => run(context, options));

                    _out.WriteLine($"[dry:{leg}] {name}: {Shape(refusal)}");

                    Assert.Equal(code, refusal.ErrorCode);

                    // Named again, exactly as the convenience tier names it.
                    Assert.NotEqual("*", refusal.FieldPath);
                    Assert.NotEmpty(refusal.FieldPath);
                }
            }
        }

        // =========================================================================================
        // AuditRefusals keeps the real field for all four.
        // =========================================================================================

        [Fact]
        public void All_four_record_the_real_field_through_audit_refusals()
        {
            (string Name, string Expected)[] expected =
            {
                ("AmbiguousGroupKey", "Payroll"),
                ("MissingHashSalt", "NationalId"),
                ("MissingTokenVault", "NationalId"),
                ("TransformRequiresMaterialization", "Masked")
            };

            for (int i = 0; i < Four.Length; i++)
            {
                DwPolicyContext context = Caller();

                Assert.Throws<PolicyException>(() => Four[i].Run(context, Options(auditRefusals: true)));

                _out.WriteLine($"[audited] {Four[i].Name}: {Recorded(context)}");

                string path = Assert.Single(context.PendingAuditEvents).FieldPath;

                Assert.NotEqual("*", path);
                Assert.Contains(expected[i].Expected, path);
            }
        }

        // =========================================================================================
        // An ambiguous name is refused as an unknown name is.
        // =========================================================================================

        [Fact]
        public void An_ambiguous_name_is_refused_as_an_unknown_name_is_under_strict()
        {
            static Filter Named(string field) => new()
            {
                Selects = new List<string> { "Id" },
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 0, Field = field, DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { "x" }
                        }
                    }
                }
            };

            static PolicyException Ask(DwPolicyContext context, DwPolicyOptions options, string field) =>
                Refusal(() => Array.Empty<Dx7Ambiguous>().AsQueryable()
                    .ApplyPolicy(context, options, Attributes())
                    .ToList(Named(field)));

            PolicyException ambiguous = Ask(Caller(), Options(), "label");
            PolicyException unknown = Ask(Caller(), Options(), "NoSuchColumn");

            _out.WriteLine($"[strict] ambiguous : {Shape(ambiguous)}");
            _out.WriteLine($"[strict] unknown   : {Shape(unknown)}");

            // The same refusal, indistinguishable at the caller.
            Assert.Equal(unknown.ErrorCode, ambiguous.ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, ambiguous.ErrorCode);
            Assert.Equal("*", ambiguous.FieldPath);
            Assert.Null(ambiguous.SourceOrigin);
            Assert.Null(ambiguous.RuleId);

            // Convenience still says what it is.
            PolicyException open = Ask(Caller(), Options(DwTier.Convenience), "label");

            _out.WriteLine($"[convenience] ambiguous : {Shape(open)}");

            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, open.ErrorCode);

            // And a dry run, either switch, says what it is too.
            foreach ((string leg, DwPolicyContext context, DwPolicyOptions options) in new[]
            {
                ("posture switch", Caller(), Options(dryRun: true)),
                ("caller switch", Caller(dryRun: true), Options())
            })
            {
                PolicyException dry = Ask(context, options, "label");

                _out.WriteLine($"[dry:{leg}] ambiguous : {Shape(dry)}");

                Assert.Equal(PolicyErrorCode.AmbiguousFieldName, dry.ErrorCode);
            }
        }

        /// <summary>What an ambiguous name records, once it is refused as an unknown name is.</summary>
        [Fact]
        public void An_ambiguous_name_records_the_name_the_caller_wrote()
        {
            DwPolicyContext context = Caller();

            Assert.Throws<PolicyException>(() => Array.Empty<Dx7Ambiguous>().AsQueryable()
                .ApplyPolicy(context, Options(auditRefusals: true), Attributes())
                .ToList(new Filter
                {
                    Selects = new List<string> { "Id" },
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Sort = 0, Field = "label", DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "x" }
                            }
                        }
                    }
                }));

            _out.WriteLine($"[audited] ambiguous : {Recorded(context)}");

            // Recorded as the caller sent it, as a name matching nothing is — not as either of the
            // two canonical paths it could have meant.
            Assert.Equal("label", Assert.Single(context.PendingAuditEvents).FieldPath);
        }

        /// <summary>The trace keeps the ambiguity for the operator, as every document promises.</summary>
        [Fact]
        public void The_trace_keeps_the_ambiguity_under_strict()
        {
            PolicyQueryable<Dx7Ambiguous> guarded = Array.Empty<Dx7Ambiguous>()
                .AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes());

            Exception? error = Catch(() => guarded.ToList(new Filter
            {
                Selects = new List<string> { "Id" },
                Orders = new List<OrderBy> { new() { Sort = 0, Field = "label", Direction = Direction.Ascending } }
            }));

            Assert.IsType<PolicyException>(error);

            string reasons = string.Join(
                " | ",
                guarded.LastTrace!.Decisions.Select(d => $"{d.FieldPath}:{d.Action}:{d.Reason}"));

            _out.WriteLine($"trace: {reasons}");

            Assert.Contains("First", reasons);
            Assert.Contains("Second", reasons);
        }
    }
}
