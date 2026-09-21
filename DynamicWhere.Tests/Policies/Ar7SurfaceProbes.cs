using System.Reflection;
using System.Text.Json;
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
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- alias / rename models -------------------------------------------------------------

    /// <summary>A base declaring a member the derived type re-declares in another case.</summary>
    public class Ar7ShadowBase
    {
        public int Id { get; set; }

        [DwDenied]
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>
    /// The derived type declares <c>secret</c>, differing from the base's <c>Secret</c> only in
    /// case, and puts an alias spelled the same on a third member.
    /// </summary>
    public class Ar7Shadow : Ar7ShadowBase
    {
        public string secret { get; set; } = string.Empty;

        [DwAlias("secret")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>An alias spelled exactly like another member of the same type.</summary>
    public class Ar7AliasShadowsMember
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAlias("Name")]
        public string Nickname { get; set; } = string.Empty;
    }

    /// <summary>An alias spelled like another member but in a different case.</summary>
    public class Ar7AliasShadowsCase
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAlias("name")]
        public string Nickname { get; set; } = string.Empty;
    }

    /// <summary>An alias on a denied field, to see what a rename can carry out.</summary>
    public class Ar7AliasOnDenied
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAlias("code")]
        [DwNoSelect]
        public string Secret { get; set; } = string.Empty;
    }

    public sealed class Ar7SurfaceProbes
    {
        private readonly ITestOutputHelper _out;

        public Ar7SurfaceProbes(ITestOutputHelper output) => _out = output;

        private static DwPolicyContext Caller()
            => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, AuditRefusals = true, Caps = { MinGroupSize = 1 } };

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

        // =========================================================================================
        // AuditPath must not reach a caller.
        // =========================================================================================

        /// <summary>
        /// <c>AuditPath</c> is internal; nothing public on the exception exposes it and a serializer
        /// cannot see it.
        /// </summary>
        [Fact]
        public void AuditPath_is_not_on_the_public_surface_and_does_not_serialize()
        {
            PropertyInfo[] published = typeof(PolicyException)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            _out.WriteLine("public properties: " + string.Join(", ", published.Select(p => p.Name)));

            Assert.DoesNotContain(published, p => p.Name == "AuditPath");

            PolicyException refusal =
                new(PolicyErrorCode.FieldDeniedForSelect, "*", PolicyFeature.Select, DwTier.Strict)
                {
                    AuditPath = "NationalId",
                    SourceOrigin = null
                };

            string json = JsonSerializer.Serialize(refusal);
            string text = refusal.ToString();
            string message = refusal.Message;

            _out.WriteLine("json    : " + json);
            _out.WriteLine("message : " + message);

            Assert.DoesNotContain("NationalId", json, StringComparison.Ordinal);
            Assert.DoesNotContain("NationalId", message, StringComparison.Ordinal);
            Assert.DoesNotContain("NationalId", text, StringComparison.Ordinal);
        }

        /// <summary>Every strict refusal reachable from a guarded read, and what it names.</summary>
        [Fact]
        public void Strict_refusals_name_no_field_the_caller_did_not_write()
        {
            Ar7AliasOnDenied[] rows = { new() { Id = 1, Name = "a", Secret = "s" } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() => rows.AsQueryable()
                .ApplyPolicy(context, Posture(), Attributes())
                .ToList(new Filter { Selects = new List<string> { "Id", "code" } }));

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            _out.WriteLine($"code={refusal.ErrorCode} path='{refusal.FieldPath}' rule={refusal.RuleId ?? "-"} origin={refusal.SourceOrigin ?? "-"}");
            _out.WriteLine("audit : " + string.Join("; ", context.PendingAuditEvents.Select(e => e.FieldPath)));

            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);

            // The audit keeps the canonical path, not the alias the caller wrote.
            Assert.Contains(context.PendingAuditEvents, e => e.FieldPath == "Secret");
        }

        // =========================================================================================
        // The inbound and outbound alias rules.
        // =========================================================================================

        /// <summary>The startup scan reports an alias spelled exactly like another member.</summary>
        [Fact]
        public void The_scan_reports_an_alias_spelled_like_a_member()
        {
            PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(Ar7AliasShadowsMember) });

            foreach (string error in report.Errors)
            {
                _out.WriteLine("error   : " + error);
            }

            Assert.Contains(report.Errors, e => e.Contains("'Name'", StringComparison.Ordinal));
        }

        /// <summary>And one differing only in case, which is how the core compares names.</summary>
        [Fact]
        public void The_scan_reports_an_alias_spelled_like_a_member_in_another_case()
        {
            PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(Ar7AliasShadowsCase) });

            foreach (string error in report.Errors)
            {
                _out.WriteLine("error   : " + error);
            }

            Assert.Contains(report.Errors, e => e.Contains("'name'", StringComparison.Ordinal));
        }

        /// <summary>
        /// CANDIDATE. The scan asks the type for the shadowed member with IgnoreCase, and a type
        /// that declares two members whose names differ only in case has two matches.
        /// </summary>
        [Fact]
        public void The_scan_survives_a_type_with_two_members_of_one_name()
        {
            Exception? error = Catch(() =>
            {
                PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(Ar7Shadow) });

                foreach (string message in report.Errors)
                {
                    _out.WriteLine("error   : " + message);
                }

                foreach (string message in report.Warnings)
                {
                    _out.WriteLine("warning : " + message);
                }
            });

            _out.WriteLine($"raised  : {error?.GetType().Name ?? "-"} {error?.Message}");

            // It does: the scan walks the type's own properties for a name that matches the alias,
            // rather than asking reflection for one property by a name two members answer to, which
            // raised AmbiguousMatchException out of a method that returns a report. Ar7ScanProbes
            // reduces that to its two smallest shapes.
            Assert.Null(error);
        }

        /// <summary>
        /// A query on the same type: the alias 'secret' could mean the aliased member or either
        /// real member, and the answer must not hand the denied value out under the alias.
        /// </summary>
        [Fact]
        public void A_shadowing_alias_cannot_carry_a_denied_value_out()
        {
            Ar7Shadow[] rows = { new() { Id = 1, Secret = "DENIED", secret = "lower", Label = "L" } };

            DwPolicyContext context = Caller();

            Exception? error = Catch(() =>
            {
                var got = rows.AsQueryable()
                    .ApplyPolicy(context, Posture(DwTier.Convenience), Attributes())
                    .ToListDynamic(new Filter()).Data;

                foreach (object row in got)
                {
                    _out.WriteLine("row : " + JsonSerializer.Serialize(row));
                }
            });

            _out.WriteLine($"raised : {error?.GetType().Name ?? "-"} {error?.Message}");

            // The denied value does not leave. What does leave is a column literally named 'secret'
            // holding Label's value, while both real members of that name are left out — the shape
            // the startup scan is meant to report and cannot, because it aborts on this very type.
            Assert.Null(error);
        }

        /// <summary>
        /// The outbound rename must not put a column under a name the row already carries, and must
        /// not carry a denied value out under the alias.
        /// </summary>
        [Fact]
        public void A_rename_does_not_land_on_a_name_the_row_already_carries()
        {
            Ar7AliasShadowsCase[] rows = { new() { Id = 1, Name = "real", Nickname = "nick" } };

            // The inbound half: naming the colliding name at all is refused.
            Exception? named = Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), Posture(DwTier.Convenience), Attributes())
                .ToListDynamic(new Filter { Selects = new List<string> { "Id", "Name" } }));

            _out.WriteLine("naming the collision : "
                           + ((named as PolicyException)?.ErrorCode.ToString() ?? named?.GetType().Name ?? "OK"));

            Assert.Equal(PolicyErrorCode.AmbiguousFieldName, Assert.IsType<PolicyException>(named).ErrorCode);

            // The outbound half: a row carrying 'Name' must not have 'Nickname' renamed onto it.
            var got = rows.AsQueryable()
                .ApplyPolicy(Caller(), Posture(DwTier.Convenience), Attributes())
                .ToListDynamic(new Filter { Selects = new List<string> { "Id", "Nickname" } })
                .Data;

            foreach (object row in got)
            {
                _out.WriteLine("row : " + JsonSerializer.Serialize(row));
            }

            IDictionary<string, object?> columns = (IDictionary<string, object?>)got[0];

            _out.WriteLine("columns : " + string.Join(", ", columns.Keys));

            Assert.Contains(columns, c => Equals(c.Value, "nick"));
        }

        /// <summary>The alias of a deny-select field must not appear in the result at all.</summary>
        [Fact]
        public void A_deny_select_field_does_not_reach_the_caller_under_its_alias()
        {
            Ar7AliasOnDenied[] rows = { new() { Id = 1, Name = "a", Secret = "SECRET" } };

            DwPolicyContext context = Caller();

            var got = rows.AsQueryable()
                .ApplyPolicy(context, Posture(DwTier.Convenience), Attributes())
                .ToListDynamic(new Filter()).Data;

            foreach (object row in got)
            {
                _out.WriteLine("row : " + JsonSerializer.Serialize(row));
            }

            Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(got), StringComparison.Ordinal);
        }

        /// <summary>The unguarded shape of a blank grouping key, for parity with the guarded one.</summary>
        [Fact]
        public void A_blank_group_key_fails_alike_guarded_and_not()
        {
            Ar7AliasShadowsCase[] rows = { new() { Id = 1, Name = "a", Nickname = "n" } };

            Summary Blank() => new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "  " },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
                    }
                }
            };

            Exception? unguarded = Catch(() => rows.AsQueryable().ToList(Blank()));

            Exception? guarded = Catch(() => rows.AsQueryable()
                .ApplyPolicy(Caller(), Posture(DwTier.Convenience), Attributes())
                .ToList(Blank()));

            _out.WriteLine($"unguarded : {unguarded?.GetType().Name} {unguarded?.Message.Split('\n')[0]}");
            _out.WriteLine($"guarded   : {guarded?.GetType().Name} {guarded?.Message.Split('\n')[0]}");

            Assert.Equal(unguarded?.GetType(), guarded?.GetType());
        }
    }
}
