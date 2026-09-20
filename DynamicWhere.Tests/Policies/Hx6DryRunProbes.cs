using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// Which dry run each refusal honours. The library has two — the global switch and the caller's
    /// own, whose union is what every other refusal calls "a dry run" — and the documents say only
    /// "a dry run" for all of them.
    /// </summary>
    public class Hx6DryRunReachProbe
    {
        private readonly ITestOutputHelper _out;

        public Hx6DryRunReachProbe(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Canary() =>
            new DwPolicyContext { DryRun = true }.WithSubject(DwSubjectKind.User, "u1");

        /// <summary>
        /// The audit cap (breaking point 33) honours the caller's own dry run, because it asks the
        /// gate. The four of point 34 ask the options directly.
        /// </summary>
        [Fact]
        public void A_context_dry_run_is_honoured_by_the_audit_cap_and_by_the_four()
        {
            DwPolicyOptions capped = new() { Tier = DwTier.Strict };

            capped.Caps.MaxAuditEvents = 1;
            capped.Freeze();

            Filter request = new()
            {
                Selects = new List<string> { "Id", "NationalId", "Code" },
                Orders = new List<OrderBy> { new() { Field = "NationalId", Direction = Direction.Ascending } }
            };

            PolicyException cap = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Audited { Id = 1, Code = "c", NationalId = "A-1" } }.AsQueryable(),
                        capped,
                        Canary())
                    .ToList(request));

            _out.WriteLine($"audit cap, context dry run  -> {cap.ErrorCode} \"{cap.FieldPath}\" origin={Hx6.Origin(cap)}");

            PolicyException transform = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Staffer { Id = 1, Note = "abcdef" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict, salt: new string('s', 16)),
                        Canary())
                    .SelectDynamic(new List<string> { "Id" }));

            PolicyException salt = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Secret { Id = 1, Hashed = "AAA-111" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict),
                        Canary())
                    .ToList(new Filter()));

            _out.WriteLine($"TRM,       context dry run  -> {transform.ErrorCode} \"{transform.FieldPath}\"");
            _out.WriteLine($"salt,      context dry run  -> {salt.ErrorCode} \"{salt.FieldPath}\"");

            // A canary subject previews the posture without meeting the refusals it hides, which is
            // what a dry run is for — and a dry run is either switch, the posture's or the caller's.
            Assert.Equal(PolicyErrorCode.CapExceeded, cap.ErrorCode);
            Assert.NotEqual("*", transform.FieldPath);
            Assert.NotEqual("*", salt.FieldPath);
        }

        /// <summary>
        /// The global switch is the one the four read, and there they do name the field, which is
        /// the half of "a dry run is unchanged" that holds.
        /// </summary>
        [Fact]
        public void The_global_dry_run_still_names_the_field_for_all_four()
        {
            PolicyException transform = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Staffer { Id = 1, Note = "abcdef" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict, dryRun: true, salt: new string('s', 16)))
                    .SelectDynamic(new List<string> { "Id" }));

            PolicyException salt = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Secret { Id = 1, Hashed = "AAA-111" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict, dryRun: true))
                    .ToList(new Filter()));

            PolicyException vault = Assert.Throws<PolicyException>(
                () => Hx6.Guard(
                        new[] { new Hx6Tokenized { Id = 1, Ticket = "T-1" } }.AsQueryable(),
                        Hx6.Options(DwTier.Strict, dryRun: true))
                    .ToList(new Filter()));

            _out.WriteLine($"TRM,   global dry run -> \"{transform.FieldPath}\"");
            _out.WriteLine($"salt,  global dry run -> \"{salt.FieldPath}\"");
            _out.WriteLine($"vault, global dry run -> \"{vault.FieldPath}\"");

            Assert.Equal("Note, Salary, NationalId", transform.FieldPath);
            Assert.Equal("Hashed", salt.FieldPath);
            Assert.Equal("Ticket", vault.FieldPath);
        }
    }
}
