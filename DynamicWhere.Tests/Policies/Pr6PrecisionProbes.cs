using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- models -----------------------------------------------------------------------------------------

    /// <summary>A row of a generated summary, whose two key columns the probe sets by hand.</summary>
    public class Pr6KeyRow
    {
        public string? K1 { get; set; }

        public string? K2 { get; set; }

        public int N { get; set; }
    }

    /// <summary>A transformer that returns what it was given, so the composite key is the probe's own text.</summary>
    public sealed class Pr6Identity : IValueTransformer
    {
        public object? Transform(object? value, DwTransformContext context) => value;
    }

    /// <summary>Aliases that resolve, and aliases that collide.</summary>
    public class Pr6Aliased
    {
        public int Id { get; set; }

        /// <summary>A name nothing else answers to: it must still resolve.</summary>
        [DwAlias("Handle")]
        public string Login { get; set; } = string.Empty;

        /// <summary>An alias spelled exactly like its own path: one candidate, not two.</summary>
        [DwAlias("Same")]
        public string Same { get; set; } = string.Empty;

        /// <summary>An alias that is also a real property of the type.</summary>
        [DwAlias("Code")]
        public string Serial { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        /// <summary>Two members sharing one alias.</summary>
        [DwAlias("Ref")]
        public string RefA { get; set; } = string.Empty;

        [DwAlias("Ref")]
        public string RefB { get; set; } = string.Empty;
    }

    /// <summary>One alias on a type that appears inside its own navigation graph.</summary>
    public class Pr6Node
    {
        public int Id { get; set; }

        [DwAlias("Tag")]
        public string Label { get; set; } = string.Empty;

        public Pr6Node? Parent { get; set; }

        public List<Pr6Node> Children { get; set; } = new();
    }

    /// <summary>A type whose values are transformed, which is what makes a query unmaterializable.</summary>
    public class Pr6Masked
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string Secret { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Full)]
        public string Other { get; set; } = string.Empty;
    }

    /// <summary>
    /// Round 6: the four refusals the fifth review changed, and the two literals it rewrote.
    /// </summary>
    /// <remarks>
    /// Every probe prints its answer and every probe runs on 3.2.0 unchanged, so the two outputs are
    /// diffed rather than asserted against a remembered expectation.
    /// </remarks>
    public sealed class Pr6PrecisionProbes
    {
        private readonly ITestOutputHelper _out;

        public Pr6PrecisionProbes(ITestOutputHelper output) => _out = output;

        // Built from code points, never from an escape, so nothing between here and the file can
        // decode one into the character it names.
        private static readonly string Nul = ((char)0).ToString();

        private static readonly string Unit = ((char)31).ToString();

        private static DwPolicyContext Caller(bool dryRun = false) =>
            new DwPolicyContext { DryRun = dryRun }.WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict, bool dryRun = false) =>
            new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

        private void Line(string probe, string answer) => _out.WriteLine($"{probe,-58} {answer}");

        // =========================================================================================
        // Pr6-A. The composite grouping key. Two escapes replaced a raw NUL and a raw unit
        //        separator: every collision and every non-collision below pins both to the exact
        //        code point, because each case turns on one of them.
        // =========================================================================================

        private static TypePolicy KeyPolicy() => new(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<ForcedPredicate>(),
            new Dictionary<string, IReadOnlyList<Operator>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ValueTransform>(StringComparer.OrdinalIgnoreCase)
            {
                ["K1"] = new ValueTransform(mutate: new MutateStage(typeof(Pr6Identity))),
                ["K2"] = new ValueTransform(mutate: new MutateStage(typeof(Pr6Identity)))
            });

        /// <summary>Runs the summary transformer over rows whose key columns the probe chose.</summary>
        private static string Keys(
            (string? K1, string? K2)[] rows,
            DwTier tier = DwTier.Strict,
            bool optionsDryRun = false,
            bool contextDryRun = false)
        {
            SummaryResult result = new();

            foreach ((string? k1, string? k2) in rows)
            {
                result.Data.Add(new Pr6KeyRow { K1 = k1, K2 = k2, N = 1 });
            }

            Summary summary = new()
            {
                GroupBy = new GroupBy { Fields = { "K1", "K2" } }
            };

            DwPolicyOptions options = Options(tier, optionsDryRun);

            try
            {
                ResultTransformer.Summary(
                    result, summary, KeyPolicy(), Caller(contextDryRun), options,
                    new PolicyTrace(tier, optionsDryRun || contextDryRun));

                return "OK";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath}|origin=" +
                       (refusal.SourceOrigin is null ? "null" : "set") + ")";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        [Fact]
        public void Pr6_A_The_composite_key_is_the_same_bytes_it_always_was()
        {
            Line("A01 two plain keys that differ", Keys(new (string?, string?)[] { ("a", "b"), ("c", "d") }));
            Line("A02 two plain keys that match", Keys(new (string?, string?)[] { ("a", "b"), ("a", "b") }));
            Line("A03 one row only", Keys(new (string?, string?)[] { ("a", "b") }));
            Line("A04 no rows", Keys(Array.Empty<(string?, string?)>()));

            // The separator. These collide if and only if the joiner is exactly U+001F.
            Line("A05 separator straddled by a value",
                Keys(new (string?, string?)[] { ("a" + Unit + "b", "c"), ("a", "b" + Unit + "c") }));
            Line("A06 separator at both ends",
                Keys(new (string?, string?)[] { (Unit, string.Empty), (string.Empty, Unit) }));
            Line("A07 a value that is only separators",
                Keys(new (string?, string?)[] { (Unit + Unit, "x"), (Unit, Unit + "x") }));

            // The null sentinel. These collide if and only if the sentinel is exactly one U+0000.
            Line("A08 null against a literal NUL", Keys(new (string?, string?)[] { (null, "c"), (Nul, "c") }));
            Line("A09 null on both keys against NUL on both",
                Keys(new (string?, string?)[] { (null, null), (Nul, Nul) }));
            Line("A10 empty string is not the null sentinel",
                Keys(new (string?, string?)[] { (string.Empty, "c"), (null, "c") }));
            Line("A11 empty string is not a literal NUL",
                Keys(new (string?, string?)[] { (string.Empty, "c"), (Nul, "c") }));
            Line("A12 two nulls in the same column",
                Keys(new (string?, string?)[] { (null, "c"), (null, "d") }));

            // Both characters inside the values at once.
            Line("A13 NUL and separator inside the values",
                Keys(new (string?, string?)[] { (Nul + Unit, "y"), (Nul, Unit + "y") }));
            Line("A14 NUL and separator, not colliding",
                Keys(new (string?, string?)[] { (Nul + Unit, "y"), (Unit + Nul, "y") }));

            // Detection order: the third row is the one that repeats.
            Line("A15 collision only between rows two and three",
                Keys(new (string?, string?)[] { ("a", "b"), ("c", "d"), ("c", "d") }));
            Line("A16 three rows, none repeating",
                Keys(new (string?, string?)[] { ("a", "b"), ("c", "d"), ("e", "f") }));

            // Ordinal, not case-insensitive: unchanged either way, and a change would show here.
            Line("A17 keys differing only in case", Keys(new (string?, string?)[] { ("A", "b"), ("a", "b") }));
        }

        [Fact]
        public void Pr6_A_What_the_group_key_refusal_says_in_each_posture()
        {
            (string?, string?)[] colliding = { ("a", "b"), ("a", "b") };

            Line("A18 strict", Keys(colliding));
            Line("A19 convenience", Keys(colliding, DwTier.Convenience));
            Line("A20 strict, options dry run", Keys(colliding, optionsDryRun: true));
            Line("A21 strict, CONTEXT dry run", Keys(colliding, contextDryRun: true));
            Line("A22 convenience, context dry run", Keys(colliding, DwTier.Convenience, contextDryRun: true));
        }

        // =========================================================================================
        // Pr6-B. A name matching more than one field. Under the strict tier it is no longer refused
        //        with AmbiguousFieldName; it is refused as an unknown name is. Nothing that resolved
        //        before may stop resolving, and nothing refused before may start succeeding.
        // =========================================================================================

        private static Condition On(string field) => new()
        {
            Sort = 0, Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" }
        };

        private static string Sanitize<T>(
            Filter filter,
            DwTier tier = DwTier.Strict,
            bool optionsDryRun = false,
            bool contextDryRun = false,
            PolicyTrace? trace = null)
            where T : class
        {
            trace ??= new PolicyTrace(tier, optionsDryRun || contextDryRun);

            try
            {
                Filter result = FilterSanitizer.Sanitize<T>(
                    filter, Attributes(), Caller(contextDryRun), Options(tier, optionsDryRun), trace);

                string fields = string.Join(
                    ",",
                    (result.ConditionGroup?.Conditions ?? new List<Condition>()).Select(c => c.Field));

                string selects = result.Selects is null ? "-" : string.Join(",", result.Selects);
                string orders = result.Orders is null ? "-" : string.Join(",", result.Orders.Select(o => o.Field));

                return $"OK(where={fields};select={selects};order={orders})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath}|origin=" +
                       (refusal.SourceOrigin is null ? "null" : "set") + ")";
            }
            catch (LogicException failure)
            {
                return $"LOGIC({failure.Message})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static string SanitizeSummary<T>(Summary summary, DwTier tier = DwTier.Strict) where T : class
        {
            try
            {
                Summary result = FilterSanitizer.Sanitize<T>(
                    summary, Attributes(), Caller(), Options(tier), new PolicyTrace(tier, false));

                return "OK(" + string.Join(",", result.GroupBy?.Fields ?? new List<string>()) + ")";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath})";
            }
            catch (LogicException failure)
            {
                return $"LOGIC({failure.Message})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static Filter Where(string field) =>
            new() { ConditionGroup = new ConditionGroup { Conditions = { On(field) } } };

        [Fact]
        public void Pr6_B_An_alias_that_resolved_still_resolves()
        {
            Line("B01 unique alias, where", Sanitize<Pr6Aliased>(Where("Handle")));
            Line("B02 unique alias, convenience", Sanitize<Pr6Aliased>(Where("Handle"), DwTier.Convenience));
            Line("B03 alias spelled like its own path", Sanitize<Pr6Aliased>(Where("Same")));
            Line("B04 the plain property behind an alias", Sanitize<Pr6Aliased>(Where("Login")));
            Line("B05 a property no alias touches", Sanitize<Pr6Aliased>(Where("Id")));
            Line("B06 one member reached many ways", Sanitize<Pr6Node>(Where("Tag")));
            Line("B07 one member many ways, convenience",
                Sanitize<Pr6Node>(Where("Tag"), DwTier.Convenience));
            Line("B08 the path that alias declares", Sanitize<Pr6Node>(Where("Label")));
            Line("B09 a deeper path of the same member", Sanitize<Pr6Node>(Where("Parent.Label")));

            Line("B10 unique alias, select", Sanitize<Pr6Aliased>(new Filter { Selects = new List<string> { "Handle" } }));
            Line("B11 unique alias, order",
                Sanitize<Pr6Aliased>(new Filter { Orders = new List<OrderBy> { new OrderBy { Field = "Handle" } } }));
            Line("B12 unique alias, group",
                SanitizeSummary<Pr6Aliased>(new Summary { GroupBy = new GroupBy { Fields = { "Handle" } } }));
        }

        [Fact]
        public void Pr6_B_A_name_matching_more_than_one_field_in_every_clause()
        {
            Line("B20 alias colliding with a property, where", Sanitize<Pr6Aliased>(Where("Code")));
            Line("B21 two members one alias, where", Sanitize<Pr6Aliased>(Where("Ref")));

            Line("B22 colliding name, select alone",
                Sanitize<Pr6Aliased>(new Filter { Selects = new List<string> { "Code" } }));
            Line("B23 colliding name beside a good select",
                Sanitize<Pr6Aliased>(new Filter { Selects = new List<string> { "Id", "Code" } }));
            Line("B24 colliding name, order",
                Sanitize<Pr6Aliased>(new Filter { Orders = new List<OrderBy> { new OrderBy { Field = "Code" } } }));
            Line("B25 colliding name, order beside a good one",
                Sanitize<Pr6Aliased>(new Filter
                {
                    Orders = new List<OrderBy> { new OrderBy { Field = "Id" }, new OrderBy { Field = "Code" } }
                }));
            Line("B26 colliding name, nested condition group",
                Sanitize<Pr6Aliased>(new Filter
                {
                    ConditionGroup = new ConditionGroup
                    {
                        SubConditionGroups = { new ConditionGroup { Conditions = { On("Code") } } }
                    }
                }));
            Line("B27 colliding name, grouping key",
                SanitizeSummary<Pr6Aliased>(new Summary { GroupBy = new GroupBy { Fields = { "Code" } } }));
            Line("B28 colliding name, aggregated field",
                SanitizeSummary<Pr6Aliased>(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = { "Id" },
                        AggregateBy =
                        {
                            new AggregateBy { Field = "Code", Alias = "c", Aggregator = Aggregator.Maximum }
                        }
                    }
                }));
            Line("B29 colliding name beside a legitimate one",
                Sanitize<Pr6Aliased>(new Filter
                {
                    ConditionGroup = new ConditionGroup { Conditions = { On("Handle"), On("Code") } }
                }));
        }

        [Fact]
        public void Pr6_B_The_colliding_name_in_every_posture()
        {
            Line("B30 strict", Sanitize<Pr6Aliased>(Where("Code")));
            Line("B31 convenience", Sanitize<Pr6Aliased>(Where("Code"), DwTier.Convenience));
            Line("B32 strict, options dry run", Sanitize<Pr6Aliased>(Where("Code"), optionsDryRun: true));
            Line("B33 strict, CONTEXT dry run", Sanitize<Pr6Aliased>(Where("Code"), contextDryRun: true));
            Line("B34 convenience, options dry run",
                Sanitize<Pr6Aliased>(Where("Code"), DwTier.Convenience, optionsDryRun: true));
        }

        [Fact]
        public void Pr6_B_What_the_trace_says_about_a_colliding_name()
        {
            PolicyTrace trace = new(DwTier.Strict, false);

            Line("B40 strict refusal", Sanitize<Pr6Aliased>(Where("Code"), trace: trace));

            foreach (PolicyDecision decision in trace.Decisions)
            {
                Line("B41 decision", $"{decision.FieldPath} | {decision.Feature} | {decision.Action} | {decision.Reason}");
            }

            PolicyTrace two = new(DwTier.Strict, false);

            Line("B42 two members one alias", Sanitize<Pr6Aliased>(Where("Ref"), trace: two));

            foreach (PolicyDecision decision in two.Decisions)
            {
                Line("B43 decision", $"{decision.FieldPath} | {decision.Feature} | {decision.Action} | {decision.Reason}");
            }
        }

        [Fact]
        public void Pr6_B_What_a_caller_who_catches_the_refusal_can_read()
        {
            // End to end, through the handle a host actually uses: the refusal names no field, so
            // LastTrace has to be where the operator reads which name was ambiguous and why.
            List<Pr6Aliased> rows = new() { new Pr6Aliased() };

            PolicyQueryable<Pr6Aliased> handle =
                rows.AsQueryable().ApplyPolicy(Caller(), Options(), Attributes());

            try
            {
                _ = handle.ToList(Where("Code"));

                Line("B50 through the handle", "OK");
            }
            catch (PolicyException refusal)
            {
                Line("B50 through the handle",
                    $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath})");
            }

            Line("B51 LastTrace", handle.LastTrace is null ? "null" : "present");

            foreach (PolicyDecision decision in handle.LastTrace?.Decisions ?? new List<PolicyDecision>())
            {
                Line("B52 decision", $"{decision.FieldPath} | {decision.Feature} | {decision.Action} | {decision.Reason}");
            }
        }

        // =========================================================================================
        // Pr6-C. TransformRequiresMaterialization, MissingHashSalt and MissingTokenVault: what each
        //        names, in each posture, including the per-context dry run the rest of the library
        //        treats as a dry run.
        // =========================================================================================

        private static string Unmaterialized(DwTier tier, bool optionsDryRun, bool contextDryRun)
        {
            List<Pr6Masked> rows = new() { new Pr6Masked { Id = 1 } };

            try
            {
                _ = rows.AsQueryable()
                    .ApplyPolicy(Caller(contextDryRun), Options(tier, optionsDryRun), Attributes())
                    .SelectDynamic(new List<string> { "Id" });

                return "OK";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        [Fact]
        public void Pr6_C_A_query_the_caller_materializes_over_a_transformed_type()
        {
            Line("C01 strict", Unmaterialized(DwTier.Strict, false, false));
            Line("C02 convenience", Unmaterialized(DwTier.Convenience, false, false));
            Line("C03 strict, options dry run", Unmaterialized(DwTier.Strict, true, false));
            Line("C04 strict, CONTEXT dry run", Unmaterialized(DwTier.Strict, false, true));
            Line("C05 convenience, context dry run", Unmaterialized(DwTier.Convenience, false, true));
        }

        private static string Chain(
            ValueTransform chain, DwTier tier, bool optionsDryRun, bool contextDryRun, string? salt = null)
        {
            DwPolicyOptions options = Options(tier, optionsDryRun);

            if (salt is not null)
            {
                options.HashSalt = salt;
            }

            try
            {
                object? value = TransformPipeline.Apply(
                    chain,
                    "abcdef",
                    typeof(string),
                    new DwTransformContext(new Pr6Masked(), "Secret", Caller(contextDryRun)),
                    options);

                return "OK(" + (value ?? "null") + ")";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode}|path={refusal.FieldPath})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        [Fact]
        public void Pr6_C_A_hash_with_no_salt_and_a_token_with_no_vault()
        {
            ValueTransform hash = new(mask: new MaskStage(MaskStrategy.Hash));
            ValueTransform token = new(mask: new MaskStage(MaskStrategy.Tokenize));

            Line("C10 hash, strict", Chain(hash, DwTier.Strict, false, false));
            Line("C11 hash, convenience", Chain(hash, DwTier.Convenience, false, false));
            Line("C12 hash, strict, options dry run", Chain(hash, DwTier.Strict, true, false));
            Line("C13 hash, strict, CONTEXT dry run", Chain(hash, DwTier.Strict, false, true));

            Line("C14 token, strict", Chain(token, DwTier.Strict, false, false));
            Line("C15 token, convenience", Chain(token, DwTier.Convenience, false, false));
            Line("C16 token, strict, options dry run", Chain(token, DwTier.Strict, true, false));
            Line("C17 token, strict, CONTEXT dry run", Chain(token, DwTier.Strict, false, true));

            // A salt that is configured still hashes, in every posture: the refusal is the only thing
            // that changed, and it must not have taken the success with it.
            Line("C18 hash with a salt, strict",
                Chain(hash, DwTier.Strict, false, false, "0123456789abcdefghij"));
            Line("C19 hash with a salt, convenience",
                Chain(hash, DwTier.Convenience, false, false, "0123456789abcdefghij"));
        }
    }
}
