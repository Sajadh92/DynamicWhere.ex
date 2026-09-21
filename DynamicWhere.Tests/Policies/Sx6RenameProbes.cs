using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
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
    // Round 6. The outbound half of [DwAlias], and the set of names a query marks unknown.
    // =============================================================================================

    /// <summary>
    /// A type where one member's alias is another member's own column name. Inbound the spelling is
    /// ambiguous and refused; outbound both columns want to be called <c>Salary</c>.
    /// </summary>
    public class Sx6Shadowed
    {
        public int Id { get; set; }

        public string Salary { get; set; } = string.Empty;

        [DwAlias("Salary")]
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>Two members sharing one alias: the collision the startup scan does report.</summary>
    public class Sx6TwoAliases
    {
        public int Id { get; set; }

        [DwAlias("code")]
        public string First { get; set; } = string.Empty;

        [DwAlias("code")]
        public string Second { get; set; } = string.Empty;
    }

    /// <summary>A plain type with a navigation, for the unknown-name normalization probe.</summary>
    public class Sx6Leaf
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    public class Sx6Root
    {
        public int Id { get; set; }

        public string Region { get; set; } = string.Empty;

        public Sx6Leaf? Leaf { get; set; }
    }

    public sealed class Sx6RenameProbes
    {
        private readonly ITestOutputHelper _out;

        public Sx6RenameProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, Caps = { MinGroupSize = 1 } };

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

        private static List<string> ColumnsOf(object row) =>
            row is IDictionary<string, object?> expando
                ? expando.Keys.ToList()
                : row.GetType().GetProperties().Select(p => p.Name).ToList();

        private static object? Read(object row, string column) =>
            row is IDictionary<string, object?> expando && expando.TryGetValue(column, out object? value)
                ? value
                : row.GetType().GetProperty(column)?.GetValue(row);

        // =========================================================================================
        // FINDING candidate. Rename maps a column onto a name another column in the same row already
        // has, and one of the two values is lost — the outcome its own comment says it avoids.
        // =========================================================================================

        [Fact]
        public void Rename_keeps_both_columns_when_an_alias_shadows_another_columns_name()
        {
            Sx6Shadowed[] rows = { new() { Id = 1, Salary = "REAL-SALARY", Notes = "THE-NOTES" } };

            // The caller names no projection, so the whole row comes back and both columns are on
            // it. Naming them would be refused inbound: "Salary" is the ambiguous spelling.
            FilterResult<dynamic> result = rows.AsQueryable()
                .ApplyPolicy(Caller(), Options(DwTier.Convenience), Attributes())
                .ToListDynamic(new Filter());

            object row = result.Data[0];

            List<string> columns = ColumnsOf(row);

            _out.WriteLine($"columns  : {string.Join(", ", columns)}");

            foreach (string column in columns)
            {
                _out.WriteLine($"  {column} = {Read(row, column)}");
            }

            // The row held Id, Salary and Notes, and "Notes" is aliased to "Salary", which the row
            // already carries. The rename is not applied, so neither value is lost: renaming would
            // emit one column under the other's name and drop that other's value outright.
            Assert.Contains("Notes", columns);
            Assert.Contains("Salary", columns);
            Assert.Equal(3, columns.Count);

            Assert.Equal("REAL-SALARY", Read(row, "Salary"));
            Assert.Equal("THE-NOTES", Read(row, "Notes"));
        }

        /// <summary>
        /// The startup scan does not report the model. <c>CheckAlias</c> compares an alias only
        /// against other aliases, never against the type's own property names, so the collision it
        /// exists to catch at deployment is found at query time instead.
        /// </summary>
        [Fact]
        public void The_startup_scan_reports_an_alias_that_shadows_a_property()
        {
            PolicyModelReport shadowing = PolicyModelValidator.Inspect(new[] { typeof(Sx6Shadowed) });

            // The case the scan does catch, for contrast: two members sharing one alias.
            PolicyModelReport twoAliases = PolicyModelValidator.Inspect(new[] { typeof(Sx6TwoAliases) });

            _out.WriteLine($"alias shadows a property : errors={shadowing.Errors.Count}"
                + $" warnings={shadowing.Warnings.Count}");

            foreach (string message in shadowing.Errors.Concat(shadowing.Warnings))
            {
                _out.WriteLine($"  {message}");
            }

            _out.WriteLine($"two members, one alias   : errors={twoAliases.Errors.Count}");

            foreach (string message in twoAliases.Errors)
            {
                _out.WriteLine($"  {message}");
            }

            // The alias-against-alias collision is reported.
            Assert.False(twoAliases.IsValid);

            // And so is the alias-against-property collision: a generated row cannot carry one name
            // twice, so one of the two values would be the one the caller receives.
            Assert.False(shadowing.IsValid);
        }

        /// <summary>
        /// The inbound half still refuses the spelling, so the two halves disagree: one name is too
        /// ambiguous to accept and not too ambiguous to emit.
        /// </summary>
        [Fact]
        public void The_same_spelling_is_refused_inbound_and_emitted_outbound()
        {
            Exception? inbound = Catch(() => Array.Empty<Sx6Shadowed>().AsQueryable()
                .ApplyPolicy(Caller(), Options(DwTier.Convenience), Attributes())
                .ToListDynamic(new Filter { Selects = new List<string> { "Salary" } }));

            _out.WriteLine($"inbound 'Salary' : {inbound?.GetType().Name ?? "OK"}");

            PolicyException refusal = Assert.IsType<PolicyException>(inbound);

            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, refusal.ErrorCode);
        }

        // =========================================================================================
        // The set of names a query marks unknown: a name that normalizes onto a real path.
        // =========================================================================================

        /// <summary>
        /// <c>Gate.Unknown</c> collapses empty segments so a padded unknown name cannot be told from
        /// a real one by the navigation cap. The collapsed form is what is remembered, so a name
        /// that collapses onto a real path marks that path unknown for the rest of the request.
        /// </summary>
        [Fact]
        public void A_name_that_normalizes_onto_a_real_path_marks_that_path_unknown()
        {
            // Both clauses in one request: an unknown spelling first, then the real field.
            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 0, Field = "Region..", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { "x" }
                        }
                    }
                },
                Selects = new List<string> { "Id", "Region" }
            };

            Exception? together = Catch(() => Array.Empty<Sx6Root>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes()).ToList(filter));

            // The same projection on its own, to show the field is one the caller may use.
            Exception? alone = Catch(() => Array.Empty<Sx6Root>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "Region" } }));

            _out.WriteLine($"padded name + real select : {together?.GetType().Name ?? "OK"}");
            _out.WriteLine($"real select alone         : {alone?.GetType().Name ?? "OK"}");

            // The projection alone is answered, so Region is allowed.
            Assert.Null(alone);

            // RULED OUT. "Region.." canonicalizes to "Region" in the validator, so it never reaches
            // Gate.Unknown and cannot mark the real path unknown for the rest of the request.
            Assert.Null(together);
        }

        /// <summary>
        /// The same padded spelling on its own, so the shape of its refusal is on the record.
        /// </summary>
        [Fact]
        public void A_padded_name_on_its_own_is_refused_as_an_unknown_name()
        {
            Exception? error = Catch(() => Array.Empty<Sx6Root>().AsQueryable()
                .ApplyPolicy(Caller(), Options(), Attributes())
                .ToList(new Filter
                {
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Sort = 0, Field = "Region..", DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "x" }
                            }
                        }
                    }
                }));

            _out.WriteLine(error is PolicyException refusal
                ? $"padded alone : {refusal.ErrorCode}|path={refusal.FieldPath}"
                : $"padded alone : {error?.GetType().Name ?? "OK"}");

            // A real field padded with dots is a real field. Gate.Unknown's collapse only ever sees
            // names the validator already rejected.
            Assert.Null(error);
        }
    }
}
