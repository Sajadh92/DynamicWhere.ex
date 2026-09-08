using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Transformation end to end, against a real database.
/// </summary>
/// <remarks>
/// The pipeline suite proves the chain computes the right value. This one proves the right value
/// reaches the caller and the wrong one reaches the database: filtering and sorting still run on
/// real values in SQL, and what comes back is transformed.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class TransformQueryTests : IDisposable
{
    private readonly PolicyContext _db;

    public TransformQueryTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    /// <summary>
    /// Reads one scalar straight out of the database, bypassing EF entirely.
    /// </summary>
    /// <remarks>
    /// Deliberately ADO rather than the EF helper for the same job: that API arrived after EF Core
    /// 6, and these are the assertions that prove a transform never reached the stored value.
    /// Skipping them on the 6.0.22 floor leg would drop exactly the checks the leg exists to run.
    /// </remarks>
    private T Stored<T>(string sql)
    {
        var connection = _db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        object? value = command.ExecuteScalar();

        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience) =>
        new() { Tier = tier, HashSalt = "pepper" };

    private static PolicyResolver Attributes() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private PolicyQueryable<Person> Guarded(DwTier tier = DwTier.Convenience) =>
        _db.People.ApplyPolicy(Caller(), Options(tier), Attributes());

    // -------------------------------------------------------------------- values on the way out

    [Fact]
    public void A_masked_column_comes_back_masked()
    {
        Person ada = Guarded().ToList(new Filter()).Data.Single(p => p.Name == "Ada");

        Assert.Equal("********2345", ada.NationalId);
        Assert.Equal("a**@e******.com", ada.Email);
    }

    [Fact]
    public void A_generalized_number_stays_a_number()
    {
        // The reason generalization exists: a decimal cannot be star-masked and stay a decimal, so
        // the member could not hold the result.
        List<Person> people = Guarded().ToList(new Filter()).Data;

        Assert.Equal(120000m, people.Single(p => p.Name == "Ada").Salary);
        Assert.Equal(70000m, people.Single(p => p.Name == "Cy").Salary);
    }

    [Fact]
    public void A_replacement_reaches_the_caller_and_discards_the_real_value()
    {
        Assert.All(Guarded().ToList(new Filter()).Data, p => Assert.Equal("N/A", p.Notes));
    }

    [Fact]
    public void A_transform_through_a_reference_navigation_is_applied()
    {
        // The output-side counterpart of the nested-path defects the input side kept producing.
        Filter filter = new() { Selects = new List<string> { "Name", "Badge.Serial" } };

        List<Person> people = Guarded().ToList(filter).Data;

        Assert.All(
            people.Where(p => p.Name is "Ada" or "Bo"),
            p => Assert.Equal("********", p.Badge!.Serial));

        // Cy carries no badge. An empty value has nothing to hide, so it masks to nothing rather
        // than to a run of stars that would imply a serial exists.
        Assert.Equal(string.Empty, people.Single(p => p.Name == "Cy").Badge!.Serial);
    }

    [Fact]
    public void A_dynamic_projection_is_transformed_too()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "NationalId" } };

        FilterResult<dynamic> result = Guarded().ToListDynamic(filter);

        Assert.All(result.Data, row => Assert.StartsWith("****", (string)row.NationalId, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the database is untouched

    [Fact]
    public void The_database_still_holds_the_real_value()
    {
        Guarded().ToList(new Filter());

        string stored = Stored<string>("SELECT NationalId FROM People WHERE Id = 1");

        Assert.Equal("AAA-111-2345", stored);
    }

    [Fact]
    public void Filtering_runs_against_the_real_value_not_the_transformed_one()
    {
        // The point of transforming after materialization: a support agent can filter and sort on
        // real values without ever being shown one.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Salary", DataType = DataType.Number,
                        Operator = Operator.GreaterThan, Values = { 120000 }
                    }
                }
            }
        };

        Person only = Assert.Single(Guarded().ToList(filter).Data);

        Assert.Equal("Bo", only.Name);
        Assert.Equal(120000m, only.Salary);
    }

    // ----------------------------------------------------------------- the composable surface

    [Fact]
    public void A_composed_chain_transforms_at_the_terminal_call()
    {
        // The reason the composable methods return the handle: the chain never leaves the guard, so
        // the terminal call still sees the rows and can transform them.
        FilterResult<Person> result = Guarded()
            .Where(new Condition
            {
                Field = "Department", DataType = DataType.Text,
                Operator = Operator.Equal, Values = { "Engineering" }
            })
            .ToList(new Filter());

        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, p => Assert.StartsWith("****", p.NationalId, StringComparison.Ordinal));
    }

    [Fact]
    public void Leaving_the_guarded_path_is_explicit_and_returns_real_values()
    {
        // Named so a reviewer can find every place masking was stepped around by searching one word.
        List<Person> raw = Guarded().AsUnguardedQueryable().ToList();

        Assert.Equal("AAA-111-2345", raw.Single(p => p.Name == "Ada").NationalId);
    }

    [Fact]
    public void A_dynamic_composable_is_refused_on_a_transformed_type()
    {
        // It hands back a query the caller runs, so nothing would mask the rows. It cannot return
        // the handle either, because what it yields is no longer a sequence of Person.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Guarded().SelectDynamic(new List<string> { "Name" }));

        Assert.Equal(PolicyErrorCode.TransformRequiresMaterialization, error.ErrorCode);
    }

    [Fact]
    public void An_untransformed_type_keeps_its_dynamic_composables()
    {
        // The refusal is about transforms, not about the method. Office carries none.
        Assert.NotNull(
            _db.Offices.ApplyPolicy(Caller(), Options(), Attributes())
                .SelectDynamic(new List<string> { "City" }));
    }

    // ------------------------------------------------------------------------------ the trace

    [Fact]
    public void Every_transformed_field_is_recorded()
    {
        PolicyTraceAssertions(Guarded().ToList(new Filter()).Policy!);

        static void PolicyTraceAssertions(ex.Policies.DTOs.PolicyTrace trace)
        {
            Assert.Contains(
                trace.Decisions,
                d => d.FieldPath == "NationalId" && d.Action == PolicyAction.Masked);
            Assert.Contains(
                trace.Decisions,
                d => d.FieldPath == "Salary" && d.Action == PolicyAction.Generalized);
            Assert.Contains(
                trace.Decisions,
                d => d.FieldPath == "Notes" && d.Action == PolicyAction.Defaulted);
        }
    }

    // --------------------------------------------------------------------------------- summary

    [Fact]
    public void A_summary_transforms_its_aggregates()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Age", Alias = "Oldest", Aggregator = Aggregator.Maximum }
                }
            }
        };

        SummaryResult result = _db.People
            .ApplyPolicy(Caller(), Options(), Attributes())
            .ToList(summary);

        // The oldest in Engineering is 41 on real values, and 40 once rounded on the way out.
        // Rounding before grouping would have picked the wrong row.
        dynamic engineering = result.Data.Single(r => (string)r.Department == "Engineering");

        Assert.Equal(40, (int)engineering.Oldest);
    }

    [Fact]
    public void A_summary_whose_keys_collide_after_transformation_is_refused()
    {
        // 118000 and 121000 are distinct groups in SQL and both round to 120000 on the way out.
        // Merging them would invent a figure the database never computed.
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "Salary" } }
        };

        PolicyException error = Assert.Throws<PolicyException>(
            () => _db.People.ApplyPolicy(Caller(), Options(), Attributes()).ToList(summary));

        Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, error.ErrorCode);
    }

    // ------------------------------------------------------------- the tracking-corruption test

    [Fact]
    public void A_transformed_value_can_never_be_written_back_to_the_database()
    {
        // Design section 8.3, and the most important test in the suite. If AsNoTracking ever
        // regresses, EF records the transform as a pending modification and the next save anywhere
        // in the same unit of work persists the mask as the real value. Silent, irreversible, and
        // nothing else catches it.
        FilterResult<Person> masked = Guarded().ToList(new Filter());

        Assert.All(masked.Data, person => Assert.Equal(EntityState.Detached, _db.Entry(person).State));

        // Something unrelated changes in the same context, and is saved.
        Office office = _db.Offices.Single(o => o.Id == 1);

        office.Capacity = 41;

        _db.SaveChanges();

        string stored = Stored<string>("SELECT NationalId FROM People WHERE Id = 1");

        decimal salary = Stored<decimal>("SELECT Salary FROM People WHERE Id = 1");

        Assert.Equal("AAA-111-2345", stored);
        Assert.Equal(118000m, salary);

        office.Capacity = 40;

        _db.SaveChanges();
    }
}
