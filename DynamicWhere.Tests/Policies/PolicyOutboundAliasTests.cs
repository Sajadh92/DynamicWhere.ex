using System.Collections.Generic;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The outbound half of <c>[DwAlias]</c>: a caller who filters by a public name gets that name back
/// on the rows, wherever the shape is one this library generates.
/// </summary>
/// <remarks>
/// Phase 3 shipped the alias as inbound rewriting and deferred this to Phase 4, which deferred it
/// here on the reasoning that renaming a column means re-projecting rather than mutating a value.
/// It does — so it happens after materialization, on the surfaces whose rows are generated types
/// rather than the caller's own <c>T</c>.
/// <para>
/// <c>FilterResult&lt;T&gt;</c> is untouched and cannot be otherwise: <c>T</c>'s property names
/// belong to <c>T</c>.
/// </para>
/// </remarks>
public class PolicyOutboundAliasTests
{
    private static DwPolicyOptions Frozen()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        return options;
    }

    private static PolicyQueryable<T> Query<T>(params IDwPolicyProvider[] extra) where T : class
    {
        List<IDwPolicyProvider> providers = new() { new AttributePolicyProvider() };

        providers.AddRange(extra);

        return Rows<T>().AsQueryable().ApplyPolicy(
            new DwPolicyContext(), Frozen(), new PolicyResolver(providers));
    }

    private static T[] Rows<T>() where T : class
    {
        if (typeof(T) == typeof(AliasedCustomer))
        {
            return new[]
            {
                (T)(object)new AliasedCustomer { Id = 1, Name = "Ada" },
                (T)(object)new AliasedCustomer { Id = 2, Name = "Grace" }
            };
        }

        return new[]
        {
            (T)(object)new PlainProduct { Id = 1, Name = "Widget" }
        };
    }

    private static IDictionary<string, object?> Columns(object row) =>
        (IDictionary<string, object?>)row;

    // ---- the rename ----------------------------------------------------------------------------

    [Fact]
    public void A_dynamic_row_carries_the_alias_the_caller_filtered_by()
    {
        FilterResult<dynamic> result = Query<AliasedCustomer>()
            .ToListDynamic(new Filter { Selects = new List<string> { "customer_name" } });

        IDictionary<string, object?> row = Columns((object)result.Data![0]);

        Assert.True(row.ContainsKey("customer_name"));
        Assert.False(row.ContainsKey("Name"));
        Assert.Equal("Ada", row["customer_name"]);
    }

    /// <summary>
    /// The asynchronous terminal renames too, proved against a real database because an in-memory
    /// queryable has no asynchronous provider to run through.
    /// </summary>
    [Fact]
    public async Task The_asynchronous_dynamic_surface_renames_too()
    {
        using PolicyContext db = new PolicyFixture().CreateContext();

        FakePolicyProvider rules = new FakePolicyProvider()
            .AddAlias("Name", "staff_name", PolicyLevel.DynamicRole);

        PolicyQueryable<Staff> query = db.Staff.ApplyPolicy(
            new DwPolicyContext(),
            Frozen(),
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider(), rules }));

        FilterResult<dynamic> result =
            await query.ToListAsyncDynamic(new Filter { Selects = new List<string> { "staff_name" } });

        Assert.True(Columns((object)result.Data![0]).ContainsKey("staff_name"));
    }

    /// <summary>
    /// A field the caller named by its internal path still comes back under the alias. The alias is
    /// the public vocabulary — which spelling was used going in does not change what the field is
    /// called coming out, or two callers would get two shapes from one query.
    /// </summary>
    [Fact]
    public void The_alias_is_used_however_the_caller_spelled_it()
    {
        FilterResult<dynamic> result = Query<AliasedCustomer>()
            .ToListDynamic(new Filter { Selects = new List<string> { "Name" } });

        Assert.True(Columns((object)result.Data![0]).ContainsKey("customer_name"));
    }

    [Fact]
    public void An_unaliased_column_keeps_its_own_name()
    {
        FilterResult<dynamic> result = Query<AliasedCustomer>()
            .ToListDynamic(new Filter { Selects = new List<string> { "Id", "customer_name" } });

        IDictionary<string, object?> row = Columns((object)result.Data![0]);

        Assert.True(row.ContainsKey("Id"));
        Assert.True(row.ContainsKey("customer_name"));
    }

    /// <summary>
    /// Nothing aliased, nothing rebuilt. A caller with no aliases gets exactly the rows the
    /// pipeline produced, so the guarded shape is unchanged for everyone who does not use the
    /// feature.
    /// </summary>
    [Fact]
    public void A_type_with_no_aliases_returns_the_pipeline_rows_untouched()
    {
        FilterResult<dynamic> result = Query<PlainProduct>()
            .ToListDynamic(new Filter { Selects = new List<string> { "Id", "Name" } });

        Assert.IsNotType<System.Dynamic.ExpandoObject>(result.Data![0]);
    }

    /// <summary>
    /// The vocabulary is per caller, which is what makes this worth doing at all: a rule may name a
    /// field for one role and leave it alone for another.
    /// </summary>
    [Fact]
    public void A_rule_can_rename_a_column_for_one_caller()
    {
        FakePolicyProvider rules = new FakePolicyProvider()
            .AddAlias("Name", "product_title", PolicyLevel.DynamicRole);

        FilterResult<dynamic> renamed = Query<PlainProduct>(rules)
            .ToListDynamic(new Filter { Selects = new List<string> { "Name" } });

        Assert.True(Columns((object)renamed.Data![0]).ContainsKey("product_title"));

        FilterResult<dynamic> plain = Query<PlainProduct>()
            .ToListDynamic(new Filter { Selects = new List<string> { "Name" } });

        Assert.False(plain.Data![0] is System.Dynamic.ExpandoObject);
    }

    /// <summary>
    /// The whole-entity projection renames as well. A caller who named no fields still gets the
    /// vocabulary they were told to use.
    /// </summary>
    [Fact]
    public void A_projection_the_caller_did_not_write_is_renamed()
    {
        FilterResult<dynamic> result = Query<AliasedCustomer>().ToListDynamic(new Filter());

        Assert.True(Columns((object)result.Data![0]).ContainsKey("customer_name"));
    }

    [Fact]
    public void Every_row_is_renamed_not_only_the_first()
    {
        FilterResult<dynamic> result = Query<AliasedCustomer>()
            .ToListDynamic(new Filter { Selects = new List<string> { "customer_name" } });

        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, row => Assert.True(Columns((object)row).ContainsKey("customer_name")));
    }

    /// <summary>
    /// A summary's group key is a generated column too, so it follows the same vocabulary. Renaming
    /// happens after the collision check, which reads the real column names.
    /// </summary>
    [Fact]
    public void A_summary_key_is_renamed()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "customer_name" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
                }
            }
        };

        SummaryResult result = Query<AliasedCustomer>().ToList(summary);

        IDictionary<string, object?> row = Columns((object)result.Data[0]);

        Assert.True(row.ContainsKey("customer_name"));
        Assert.True(row.ContainsKey("n"));
    }

    /// <summary>
    /// The typed surface is left alone, and is documented as such: a <c>FilterResult&lt;T&gt;</c>
    /// holds instances of the caller's own type, whose property names are not this library's to
    /// change.
    /// </summary>
    [Fact]
    public void The_typed_surface_is_not_renamed()
    {
        FilterResult<AliasedCustomer> result = Query<AliasedCustomer>().ToList(new Filter());

        Assert.Equal("Ada", result.Data![0].Name);
    }
}
