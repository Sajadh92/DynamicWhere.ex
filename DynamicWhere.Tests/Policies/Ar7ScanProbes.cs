using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Validation;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- the exact shapes that make the startup scan's alias check ambiguous -----------------

    /// <summary>Two members of one name in one class, which C# allows and the core folds together.</summary>
    public class Ar7TwoCases
    {
        public int Id { get; set; }

        public string Info { get; set; } = string.Empty;

        public string INFO { get; set; } = string.Empty;

        [DwAlias("info")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A base and a derived member of the same name, hidden with <c>new</c>.</summary>
    public class Ar7NewBase
    {
        public int Id { get; set; }

        public string Tag { get; set; } = string.Empty;
    }

    public class Ar7NewDerived : Ar7NewBase
    {
        public new string Tag { get; set; } = string.Empty;

        [DwAlias("tag")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A base and a derived member differing only in case.</summary>
    public class Ar7CaseBase
    {
        public int Id { get; set; }

        public string Tag { get; set; } = string.Empty;
    }

    public class Ar7CaseDerived : Ar7CaseBase
    {
        public string tag { get; set; } = string.Empty;

        [DwAlias("TAG")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A sound model carrying a real error, to show what the abort costs.</summary>
    public class Ar7AlsoBroken
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAlias("Name")]
        public string Nickname { get; set; } = string.Empty;
    }

    public sealed class Ar7ScanProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7ScanProbes(ITestOutputHelper output) => _out = output;

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

        private string Scan(params Type[] types)
        {
            PolicyModelReport? report = null;

            Exception? error = Catch(() => report = PolicyModelValidator.Inspect(types));

            if (error is not null)
            {
                return $"RAISED {error.GetType().Name}: {error.Message}";
            }

            return report!.Errors.Count == 0 && report.Warnings.Count == 0
                ? "clean"
                : string.Join(" | ", report.Errors.Concat(report.Warnings));
        }

        /// <summary>CANDIDATE. Two members of one name in one class abort the whole scan.</summary>
        [Fact]
        public void Two_members_of_one_name_in_one_class()
        {
            string outcome = Scan(typeof(Ar7TwoCases));

            _out.WriteLine("Ar7TwoCases   : " + outcome);

            // The scan reports; it never throws. Asking the type for one property by name with
            // IgnoreCase raised AmbiguousMatchException here, because two members whose names differ
            // only in case are two matches.
            Assert.DoesNotContain("RAISED", outcome, StringComparison.Ordinal);
        }

        /// <summary>A plain <c>new</c> shadow of the same name: reflection prefers the derived one.</summary>
        [Fact]
        public void A_new_shadow_of_the_same_name_is_fine()
        {
            string outcome = Scan(typeof(Ar7NewDerived));

            _out.WriteLine("Ar7NewDerived : " + outcome);

            Assert.DoesNotContain("RAISED", outcome, StringComparison.Ordinal);
        }

        /// <summary>CANDIDATE. A base and derived member differing only in case.</summary>
        [Fact]
        public void A_base_and_derived_member_differing_in_case()
        {
            string outcome = Scan(typeof(Ar7CaseDerived));

            _out.WriteLine("Ar7CaseDerived: " + outcome);

            // The same across a base and a derived declaration.
            Assert.DoesNotContain("RAISED", outcome, StringComparison.Ordinal);
        }

        /// <summary>
        /// The cost of the abort: one unscannable type takes every other type's errors with it, so a
        /// startup gate that was going to refuse the deployment instead dies on a reflection call.
        /// </summary>
        [Fact]
        public void One_unscannable_type_hides_every_other_types_errors()
        {
            string alone = Scan(typeof(Ar7AlsoBroken));
            string together = Scan(typeof(Ar7TwoCases), typeof(Ar7AlsoBroken));

            _out.WriteLine("alone    : " + alone);
            _out.WriteLine("together : " + together);

            Assert.Contains("alias 'Name'", alone, StringComparison.Ordinal);

            // Scanned beside it, the real error is still reported: no type can take another type's
            // errors down with it, because the scan no longer raises anything.
            Assert.Contains("alias 'Name'", together, StringComparison.Ordinal);
        }

        /// <summary>The same through the throwing façade a deployment actually calls.</summary>
        [Fact]
        public void ValidateModel_reports_rather_than_raising_a_reflection_error()
        {
            Exception? error = Catch(
                () => DwPolicy.ValidateModel(new DwPolicyOptions(), new[] { typeof(Ar7TwoCases) }));

            _out.WriteLine($"ValidateModel : {error?.GetType().Name ?? "-"} {error?.Message.Split('\n')[0]}");

            // The documented startup gate refuses the deployment with the model's errors, rather
            // than raising a reflection error out of a method that returns a report.
            Assert.IsType<InvalidOperationException>(error);
        }
    }
}
