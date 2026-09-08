using BenchmarkDotNet.Attributes;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Benchmarks;

/// <summary>
/// The performance budget from design section 6.5, measured rather than asserted.
/// </summary>
/// <remarks>
/// Deliberately not wired into CI. A benchmark gate on a shared runner fails for noise, and the
/// workflow it would gate is the one that publishes to NuGet — a release blocked by a neighbour's
/// build is a worse outcome than a regression caught on the next manual run. Run it by hand:
/// <code>dotnet run -c Release --project DynamicWhere.Benchmarks</code>
/// <para>
/// The comparison that matters is <see cref="Guarded"/> against <see cref="Unguarded"/>. Everything
/// else exists to say which part of the gap is which when the gap moves.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class PolicyBenchmarks
{
    private List<Person> _people = null!;
    private IQueryable<Person> _source = null!;
    private List<GatedPerson> _gated = null!;
    private IQueryable<GatedPerson> _gatedSource = null!;
    private DwPolicyContext _caller = null!;
    private DwPolicyOptions _options = null!;
    private PolicyResolver _resolver = null!;
    private Filter _filter = null!;
    private Filter _gatedFilter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _people = new List<Person>(RowCount);

        for (int i = 0; i < RowCount; i++)
        {
            _people.Add(new Person
            {
                Id = i,
                Name = $"person-{i}",
                Email = $"person{i}@example.com",
                NationalId = $"AAA-{i:D6}",
                Department = i % 4 == 0 ? "Engineering" : "Support",
                Salary = 50_000m + (i % 50) * 1_000m
            });
        }

        _source = _people.AsQueryable();

        _gated = new List<GatedPerson>(RowCount);

        for (int i = 0; i < RowCount; i++)
        {
            _gated.Add(new GatedPerson
            {
                Id = i,
                Name = $"person-{i}",
                Email = $"person{i}@example.com",
                NationalId = $"AAA-{i:D6}",
                Department = i % 4 == 0 ? "Engineering" : "Support",
                Salary = 50_000m + (i % 50) * 1_000m
            });
        }

        _gatedSource = _gated.AsQueryable();

        _options = new DwPolicyOptions { Tier = DwTier.Convenience, HashSalt = "bench" };
        _resolver = new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() });
        _caller = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        _gatedFilter = new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                {
                    new Condition { Sort = 1, Field = "Department", DataType = DataType.Text, Operator = Operator.Equal, Values = { "Engineering" } },
                    new Condition { Sort = 2, Field = "Salary", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "40000" } },
                    new Condition { Sort = 3, Field = "Name", DataType = DataType.Text, Operator = Operator.Contains, Values = { "person" } },
                    new Condition { Sort = 4, Field = "Id", DataType = DataType.Number, Operator = Operator.LessThan, Values = { "1000000" } },
                    new Condition { Sort = 5, Field = "Email", DataType = DataType.Text, Operator = Operator.Contains, Values = { "example" } }
                }
            }
        };

        _filter = new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                {
                    new Condition { Sort = 1, Field = "Department", DataType = DataType.Text, Operator = Operator.Equal, Values = { "Engineering" } },
                    new Condition { Sort = 2, Field = "Salary", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "40000" } },
                    new Condition { Sort = 3, Field = "Name", DataType = DataType.Text, Operator = Operator.Contains, Values = { "person" } },
                    new Condition { Sort = 4, Field = "Id", DataType = DataType.Number, Operator = Operator.LessThan, Values = { "1000000" } },
                    new Condition { Sort = 5, Field = "Email", DataType = DataType.Text, Operator = Operator.Contains, Values = { "example" } }
                }
            }
        };
    }

    /// <summary>How many rows each end-to-end run materializes and transforms.</summary>
    /// <remarks>
    /// Two sizes because the overhead is not one number. Resolution and sanitization are paid once
    /// per query; the transform walk is paid per row, so the percentage a guard costs falls as the
    /// result grows and the budget is only meaningful against a stated size.
    /// </remarks>
    [Params(100, 10_000)]
    public int RowCount { get; set; }

    /// <summary>The baseline: the same query with no policy at all.</summary>
    [Benchmark(Baseline = true)]
    public int Unguarded() => _source.ToList(_filter).Data?.Count ?? 0;

    /// <summary>The same query, gated, injected and transformed.</summary>
    [Benchmark]
    public int Guarded() =>
        _source.ApplyPolicy(_caller, _options, _resolver).ToList(_filter).Data?.Count ?? 0;

    /// <summary>The same query, gated but with nothing to transform.</summary>
    /// <remarks>
    /// The measurement that separates the two costs. Gating and injection are paid once per query;
    /// the transform walk is paid per row, and it has to clone what it touches because the values are
    /// changed after materialization rather than in SQL. Without this line the end-to-end number
    /// cannot say which half moved.
    /// </remarks>
    [Benchmark]
    public int GuardedGatingOnly() =>
        _gatedSource.ApplyPolicy(_caller, _options, _resolver).ToList(_gatedFilter).Data?.Count ?? 0;

    /// <summary>Resolving one field's policy, with the attribute fragments already cached.</summary>
    [Benchmark]
    public FieldPolicy ResolveField() =>
        _resolver.Resolve(typeof(Person), nameof(Person.Salary), _caller);

    /// <summary>Sanitizing the five-condition filter, without executing it.</summary>
    /// <remarks>
    /// Through the simulator rather than the sanitizer directly: <c>FilterSanitizer</c> is internal,
    /// and <c>PolicySimulator</c> is the public way to ask what a clause would become without
    /// running it. It adds a trace and a probe context over the same call.
    /// </remarks>
    [Benchmark]
    public PolicySimulation<Filter> Sanitize() =>
        PolicySimulator.Simulate<Person>(_filter, _caller, _options, _resolver);
}

/// <summary>The same shape with a denial and no transform, to price gating on its own.</summary>
public class GatedPerson
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [DwDenied]
    public string NationalId { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public decimal Salary { get; set; }
}

/// <summary>The benchmark subject: a few fields, one masked, one generalized, one denied.</summary>
public class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DwMask(MaskStrategy.Email)]
    [DwNoOrder]
    public string Email { get; set; } = string.Empty;

    [DwDenied]
    public string NationalId { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    [DwGeneralize(GeneralizeMode.Round, Step = 5000)]
    [DwNoOrder]
    public decimal Salary { get; set; }
}
