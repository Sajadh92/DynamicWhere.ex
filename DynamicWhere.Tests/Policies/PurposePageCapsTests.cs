using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Page caps a declared purpose runs under. A search and an export read the same rows under the same
/// field policy, and the export was held to the search's page: it stopped at the first page, or walked
/// the rest one page at a time, a statement and a count per page and no snapshot of the whole.
/// </summary>
public class PurposePageCapsTests
{
    private static DwPolicyContext Caller(string? purpose = null) =>
        new DwPolicyContext { Purpose = purpose }.WithSubject(DwSubjectKind.User, "u1");

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    /// <summary>A deployment's screens at 1000 and 100, and three purposes of its own.</summary>
    private static void Deployment(DwCaps caps)
    {
        caps.MaxPageSize = 1000;
        caps.DefaultPageSize = 100;
        caps.Purposes["excel"] = new DwPageCaps { MaxPageSize = 10_000, DefaultPageSize = 10_000 };
        caps.Purposes["audit-logs"] = new DwPageCaps { MaxPageSize = 5000 };
        caps.Purposes["autocomplete"] = new DwPageCaps { MaxPageSize = 20 };
    }

    private static Filter Sanitize(Filter filter, string? purpose, Action<DwCaps>? configure = null, DwTier tier = DwTier.Strict)
    {
        DwPolicyOptions options = new() { Tier = tier };

        (configure ?? Deployment)(options.Caps);

        return FilterSanitizer.Sanitize<SecuredEmployee>(
            filter, Resolver(), Caller(purpose), options, new PolicyTrace(tier, dryRun: false));
    }

    private static Filter Paged(int size) => new() { Page = new PageBy { PageNumber = 1, PageSize = size } };

    // ---- which caps a read runs under ------------------------------------------------------------

    [Fact]
    public void An_unpaged_read_under_a_purpose_is_given_the_purpose_default_page()
    {
        Filter result = Sanitize(new Filter(), "excel");

        Assert.Equal(1, result.Page!.PageNumber);
        Assert.Equal(10_000, result.Page.PageSize);
    }

    [Fact]
    public void A_read_declaring_no_purpose_keeps_the_deployment_caps()
    {
        Assert.Equal(100, Sanitize(new Filter(), null).Page!.PageSize);

        PolicyException refusal = Assert.Throws<PolicyException>(() => Sanitize(Paged(1001), null));

        Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
        Assert.Contains("MaxPageSize cap (1000)", refusal.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purpose_raises_the_largest_page_a_read_may_ask_for_and_no_further()
    {
        Assert.Equal(10_000, Sanitize(Paged(10_000), "excel").Page!.PageSize);

        PolicyException refusal = Assert.Throws<PolicyException>(() => Sanitize(Paged(10_001), "excel"));

        Assert.Equal(PolicyErrorCode.CapExceeded, refusal.ErrorCode);
        Assert.Contains("MaxPageSize cap (10000)", refusal.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purpose_nobody_declared_runs_under_the_deployment_caps()
    {
        Assert.Equal(100, Sanitize(new Filter(), "pdf").Page!.PageSize);
        Assert.Throws<PolicyException>(() => Sanitize(Paged(1001), "pdf"));
    }

    [Fact]
    public void A_value_a_purpose_leaves_out_is_the_deployment_one()
    {
        // audit-logs names a maximum only, so its default page is the deployment's.
        Assert.Equal(100, Sanitize(new Filter(), "audit-logs").Page!.PageSize);
        Assert.Equal(5000, Sanitize(Paged(5000), "audit-logs").Page!.PageSize);
        Assert.Throws<PolicyException>(() => Sanitize(Paged(5001), "audit-logs"));
    }

    [Fact]
    public void A_purpose_can_lower_the_caps_and_its_default_is_bounded_by_its_own_maximum()
    {
        // The deployment's default of 100 is above autocomplete's maximum, so it is applied as 20.
        Assert.Equal(20, Sanitize(new Filter(), "autocomplete").Page!.PageSize);
        Assert.Throws<PolicyException>(() => Sanitize(Paged(21), "autocomplete"));
    }

    [Fact]
    public void A_purpose_is_matched_without_regard_to_letter_case_or_outer_spaces()
    {
        Assert.Equal(10_000, Sanitize(new Filter(), "  EXCEL ").Page!.PageSize);
    }

    [Fact]
    public void Several_names_may_share_one_set_of_caps()
    {
        DwPageCaps export = new() { MaxPageSize = 10_000, DefaultPageSize = 10_000 };

        void Shared(DwCaps caps)
        {
            caps.DefaultPageSize = 100;

            foreach (string name in new[] { "excel", "csv", "pdf" })
            {
                caps.Purposes[name] = export;
            }
        }

        foreach (string name in new[] { "excel", "csv", "pdf" })
        {
            Assert.Equal(10_000, Sanitize(new Filter(), name, Shared).Page!.PageSize);
        }
    }

    [Fact]
    public void A_purpose_page_is_a_page_the_caller_can_still_choose_within()
    {
        Filter result = Sanitize(new Filter { Page = new PageBy { PageNumber = 3, PageSize = 250 } }, "excel");

        Assert.Equal(3, result.Page!.PageNumber);
        Assert.Equal(250, result.Page.PageSize);
    }

    [Fact]
    public void The_convenience_tier_refuses_past_a_purpose_maximum_as_well()
    {
        Assert.Throws<PolicyException>(() => Sanitize(Paged(10_001), "excel", tier: DwTier.Convenience));
    }

    [Fact]
    public void A_summary_and_a_segment_follow_the_purpose_too()
    {
        DwPolicyOptions options = new() { Tier = DwTier.Strict };

        Deployment(options.Caps);

        Summary summary = FilterSanitizer.Sanitize<SecuredEmployee>(
            new Summary { GroupBy = new GroupBy { Fields = { "Name" } } },
            Resolver(), Caller("excel"), options, new PolicyTrace(DwTier.Strict, dryRun: false));

        Assert.Equal(10_000, summary.Page!.PageSize);

        Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<SecuredEmployee>(
            new Segment { Page = new PageBy { PageNumber = 1, PageSize = 10_001 } },
            Resolver(), Caller("excel"), options, new PolicyTrace(DwTier.Strict, dryRun: false)));

        Segment segment = FilterSanitizer.Sanitize<SecuredEmployee>(
            new Segment { Page = new PageBy { PageNumber = 1, PageSize = 10_000 } },
            Resolver(), Caller("excel"), options, new PolicyTrace(DwTier.Strict, dryRun: false));

        Assert.Equal(10_000, segment.Page!.PageSize);
    }

    // ---- end to end ------------------------------------------------------------------------------

    private static List<SecuredEmployee> Staff(int count) =>
        Enumerable.Range(1, count).Select(i => new SecuredEmployee { Id = i, Name = $"e{i}" }).ToList();

    private static PolicyQueryable<SecuredEmployee> Guard(IEnumerable<SecuredEmployee> rows, string? purpose)
    {
        DwPolicyOptions options = new() { Tier = DwTier.Strict };

        Deployment(options.Caps);

        return rows.AsQueryable().ApplyPolicy(Caller(purpose), options, Resolver());
    }

    [Fact]
    public void An_export_reads_every_match_in_one_read_where_a_search_reads_one_page()
    {
        List<SecuredEmployee> staff = Staff(150);

        FilterResult<SecuredEmployee> search = Guard(staff, null).ToList(new Filter());
        FilterResult<SecuredEmployee> export = Guard(staff, "excel").ToList(new Filter());

        Assert.Equal(100, search.Data.Count);
        Assert.Equal(150, search.TotalCount);
        Assert.Equal(150, export.Data.Count);
        Assert.Equal(150, export.TotalCount);
    }

    [Fact]
    public void The_composable_page_follows_the_purpose()
    {
        List<SecuredEmployee> staff = Staff(3);

        Assert.Throws<PolicyException>(() => Guard(staff, null).Page(new PageBy { PageNumber = 1, PageSize = 5000 }));

        Guard(staff, "excel").Page(new PageBy { PageNumber = 1, PageSize = 5000 });
    }

    // ---- what a purpose may hold -----------------------------------------------------------------

    [Fact]
    public void A_purpose_page_cap_below_one_is_refused_and_null_takes_the_deployment_one()
    {
        DwPageCaps caps = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => caps.MaxPageSize = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => caps.DefaultPageSize = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => caps.DefaultPageSize = -1);

        caps.MaxPageSize = 5;
        caps.MaxPageSize = null;

        Assert.Null(caps.MaxPageSize);
    }

    [Fact]
    public void A_purpose_needs_a_name()
    {
        DwCaps caps = new();

        Assert.Throws<ArgumentException>(() => caps.Purposes[" "] = new DwPageCaps());
        Assert.Throws<ArgumentException>(() => caps.Purposes.Add("", new DwPageCaps()));
        Assert.Throws<ArgumentNullException>(() => caps.Purposes["excel"] = null!);
        Assert.False(caps.Purposes.TryGetValue(" ", out _));
        Assert.False(caps.Purposes.ContainsKey(null!));
    }

    [Fact]
    public void A_name_is_stored_trimmed_and_read_back_whatever_its_case()
    {
        DwCaps caps = new();

        caps.Purposes[" Excel "] = new DwPageCaps { MaxPageSize = 7 };

        Assert.Equal("Excel", Assert.Single(caps.Purposes.Keys));
        Assert.Equal(7, caps.Purposes["excel"].MaxPageSize);
    }

    [Fact]
    public void Purpose_caps_freeze_with_the_posture()
    {
        DwPolicyOptions options = new();
        DwPageCaps excel = new() { MaxPageSize = 10_000 };

        options.Caps.Purposes["excel"] = excel;
        options.Freeze();

        Assert.True(options.Caps.Purposes.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => options.Caps.Purposes["csv"] = new DwPageCaps());
        Assert.Throws<InvalidOperationException>(() => options.Caps.Purposes.Remove("excel"));
        Assert.Throws<InvalidOperationException>(() => options.Caps.Purposes.Clear());
        Assert.Throws<InvalidOperationException>(() => excel.MaxPageSize = 1);
        Assert.Throws<InvalidOperationException>(() => excel.DefaultPageSize = 1);
    }

    // ---- configuration ---------------------------------------------------------------------------

    private static IConfiguration Section(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value))
            .Build();

    [Fact]
    public void Purposes_bind_from_configuration_by_name()
    {
        DwPolicyOptions options = new DwPolicyOptions().Bind(Section(
            ("Caps:MaxPageSize", "1000"),
            ("Caps:DefaultPageSize", "100"),
            ("Caps:Purposes:excel:MaxPageSize", "10000"),
            ("Caps:Purposes:excel:DefaultPageSize", "10000"),
            ("Caps:Purposes:audit-logs:MaxPageSize", "5000")));

        Assert.Equal(2, options.Caps.Purposes.Count);
        Assert.Equal(10_000, options.Caps.Purposes["excel"].MaxPageSize);
        Assert.Equal(10_000, options.Caps.Purposes["EXCEL"].DefaultPageSize);
        Assert.Equal(5000, options.Caps.Purposes["audit-logs"].MaxPageSize);
        Assert.Null(options.Caps.Purposes["audit-logs"].DefaultPageSize);
    }

    [Theory]
    [InlineData("Caps:Purposes:excel:MaxPagSize")]
    [InlineData("Caps:Purposes:excel:MaxConditions")]
    public void A_key_a_purpose_does_not_have_refuses_to_start(string key)
    {
        // A misspelt cap, and a cap other than the two page caps, which no purpose replaces.
        Assert.Throws<InvalidOperationException>(() => new DwPolicyOptions().Bind(Section((key, "5"))));
    }

    [Fact]
    public void A_purpose_cap_configured_below_one_refuses_to_start()
    {
        Assert.Throws<InvalidOperationException>(() => new DwPolicyOptions().Bind(Section(("Caps:Purposes:excel:MaxPageSize", "0"))));
    }

    // ---- the purpose itself ----------------------------------------------------------------------

    [Fact]
    public void A_context_stores_its_purpose_trimmed_and_a_blank_one_as_none()
    {
        Assert.Equal("excel", new DwPolicyContext { Purpose = "  excel " }.Purpose);
        Assert.Null(new DwPolicyContext { Purpose = "   " }.Purpose);
        Assert.Null(new DwPolicyContext { Purpose = null }.Purpose);
    }

    [Fact]
    public void A_purpose_bound_rule_and_a_purpose_cap_read_the_same_purpose()
    {
        PolicyRule rule = new(
            DwSubjectKind.Global, null, typeof(SecuredEmployee).FullName!, "Name", PolicyFeature.Select,
            PolicyEffect.Deny, purpose: "excel");

        Assert.True(rule.MatchesPurpose(new DwPolicyContext { Purpose = " Excel " }));
    }
}
