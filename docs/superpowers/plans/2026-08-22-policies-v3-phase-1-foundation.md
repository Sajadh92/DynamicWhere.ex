# Policies v3.0 — Phase 1: Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the policy resolution core — context, resolved policy model, attributes, the attribute provider, and the six-level precedence resolver — with the full precedence matrix exhaustively tested against a fake provider, before any enforcement or storage exists.

**Architecture:** Static attributes and dynamic rules both compile into `PolicyFragment` objects carrying a precedence level. `PolicyResolver` merges fragments into an immutable `FieldPolicy`, which is the only type that enforcement code (Phase 2 onward) will ever see. A test-only fake provider supplies fragments at every precedence level, so the riskiest logic in the feature is proven here rather than after a real store exists.

**Tech Stack:** .NET 6 (library, `net6.0`), xUnit on net8.0, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. No new package references in this phase.

**Branch:** `feat/policies-v3`

---

## Scope boundary for this phase

In scope: enums, context, fragment and policy models, attributes, provider abstraction, attribute provider (including nested paths), the resolver, options, exceptions.

Out of scope, deliberately: `ApplyPolicy()`, any `IQueryable` interaction, any masking, any store, `DenyByDefault` mode and `[DwExpose]`, `[DwOperators]`, `[DwForceWhere]`, `[DwRequireWhere]`, `[DwAlias]`. Those arrive in Phases 2–5.

**Do not modify** `Builder.cs`, `Validator.cs`, `Converter.cs`, or `Normalizer.cs`. Nothing in this phase touches them.

---

## File structure

**Create in `DynamicWhere.ex/Policies/`:**

| File | Responsibility |
|---|---|
| `Enums/PolicyFeature.cs` | Flags enum naming the six query features a policy can speak to |
| `Enums/PolicyEffect.cs` | Allow / Mask / Deny, ordered by authority |
| `Enums/PolicyLevel.cs` | The six precedence levels, ordered most-authoritative-first |
| `Enums/DwSubjectKind.cs` | Global / Tenant / Role / User / Custom |
| `Enums/DwTier.cs` | Convenience / Strict |
| `Context/DwSubject.cs` | One (kind, identity) pair |
| `Context/DwPolicyContext.cs` | Who is asking, plus ambient values and flags |
| `DTOs/PolicySource.cs` | Provenance of one fragment, for trace and explain |
| `DTOs/PolicyFragment.cs` | One contribution to one field's policy from one source |
| `DTOs/FieldPolicy.cs` | The immutable resolved answer for one field |
| `Attributes/DwPolicyAttribute.cs` | Shared base carrying `Overridable` |
| `Attributes/DwDenyAttribute.cs` | The composable primitive |
| `Attributes/DwDenySugar.cs` | The six named subclasses |
| `Attributes/DwEntityAttribute.cs` | Class-level `RequirePolicy` |
| `Config/DwCaps.cs` | Numeric limits |
| `Config/DwPolicyOptions.cs` | Tier, caps, dry-run |
| `Resolution/IDwPolicyProvider.cs` | Fragment source abstraction |
| `Resolution/AttributePolicyProvider.cs` | Reflection provider, cached |
| `Resolution/PolicyResolver.cs` | Merge fragments into a `FieldPolicy` |

**Create in `DynamicWhere.ex/Exceptions/`:**

| File | Responsibility |
|---|---|
| `PolicyException.cs` | `LogicException` subclass carrying structured policy data |

**Modify:**

- `DynamicWhere.ex/Exceptions/ErrorCode.cs` — add policy error codes

**Create in `DynamicWhere.Tests/Policies/`:**

| File | Responsibility |
|---|---|
| `FakePolicyProvider.cs` | Test double emitting fragments at any level |
| `SecuredModels.cs` | Decorated types used by attribute provider tests |
| `PolicyResolutionTests.cs` | Resolver behaviour, one region per rule |
| `AttributeProviderTests.cs` | Reflection provider behaviour |
| `PolicyPrecedenceMatrixTests.cs` | The exhaustive grid |

---

## Task 1: PolicyFeature enum

**Files:**
- Create: `DynamicWhere.ex/Policies/Enums/PolicyFeature.cs`
- Test: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

Note the design spec listed `All = 31`. `Segment` was subsequently given its own flag, so `All` is now 63. The spec snippet itself is corrected in Task 18, where every Phase 1 documentation change is batched — do not edit the spec from this task.

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyResolver"/> and the models it merges. No database is involved;
/// every test constructs fragments directly or through <see cref="FakePolicyProvider"/>.
/// </summary>
public class PolicyResolutionTests
{
    [Fact]
    public void PolicyFeature_All_covers_every_individual_flag()
    {
        PolicyFeature all = PolicyFeature.All;

        Assert.True(all.HasFlag(PolicyFeature.Where));
        Assert.True(all.HasFlag(PolicyFeature.Select));
        Assert.True(all.HasFlag(PolicyFeature.Order));
        Assert.True(all.HasFlag(PolicyFeature.Group));
        Assert.True(all.HasFlag(PolicyFeature.Aggregate));
        Assert.True(all.HasFlag(PolicyFeature.Segment));
        Assert.Equal(63, (int)all);
    }

    [Fact]
    public void PolicyFeature_flags_are_distinct_powers_of_two()
    {
        int[] singles =
        {
            (int)PolicyFeature.Where, (int)PolicyFeature.Select, (int)PolicyFeature.Order,
            (int)PolicyFeature.Group, (int)PolicyFeature.Aggregate, (int)PolicyFeature.Segment
        };

        Assert.Equal(singles.Length, singles.Distinct().Count());
        Assert.All(singles, v => Assert.Equal(0, v & (v - 1)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL to compile — `The type or namespace name 'Policies' does not exist in the namespace 'DynamicWhere.ex'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Enums/PolicyFeature.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// Names the query features a policy can speak to. Combined as flags so one fragment can
/// address several features at once, for example <c>Select | Order</c>.
/// </summary>
[Flags]
public enum PolicyFeature
{
    /// <summary>No feature. Used as an accumulator seed, never stored on a fragment.</summary>
    None = 0,

    /// <summary>Filtering, through <c>Condition</c> and <c>ConditionGroup</c>.</summary>
    Where = 1,

    /// <summary>Projection, through <c>Filter.Selects</c>.</summary>
    Select = 2,

    /// <summary>Sorting, through <c>Filter.Orders</c>.</summary>
    Order = 4,

    /// <summary>Grouping, through <c>Summary.GroupBy</c>.</summary>
    Group = 8,

    /// <summary>Aggregation, through <c>Summary.AggregateBy</c>.</summary>
    Aggregate = 16,

    /// <summary>
    /// Participation in a set operation. Held separately from <see cref="Where"/> and
    /// <see cref="Select"/> because Union, Intersect, and Except form their own disclosure
    /// channel: set membership can reconstruct a field that was never projected.
    /// </summary>
    Segment = 32,

    /// <summary>Every feature.</summary>
    All = Where | Select | Order | Group | Aggregate | Segment
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 2 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Enums/PolicyFeature.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): add PolicyFeature flags enum"
```

---

## Task 2: Effect, level, subject-kind, and tier enums

**Files:**
- Create: `DynamicWhere.ex/Policies/Enums/PolicyEffect.cs`
- Create: `DynamicWhere.ex/Policies/Enums/PolicyLevel.cs`
- Create: `DynamicWhere.ex/Policies/Enums/DwSubjectKind.cs`
- Create: `DynamicWhere.ex/Policies/Enums/DwTier.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void PolicyEffect_orders_by_authority_so_the_highest_value_wins_a_tie()
    {
        Assert.True(PolicyEffect.Deny > PolicyEffect.Mask);
        Assert.True(PolicyEffect.Mask > PolicyEffect.Allow);
    }

    [Fact]
    public void PolicyLevel_orders_most_authoritative_first()
    {
        Assert.True(PolicyLevel.SealedAttribute < PolicyLevel.DynamicUser);
        Assert.True(PolicyLevel.DynamicUser < PolicyLevel.DynamicRole);
        Assert.True(PolicyLevel.DynamicRole < PolicyLevel.DynamicTenant);
        Assert.True(PolicyLevel.DynamicTenant < PolicyLevel.DynamicGlobal);
        Assert.True(PolicyLevel.DynamicGlobal < PolicyLevel.OverridableAttribute);
    }
```

Add `using DynamicWhere.ex.Policies.Enums;` if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL to compile — `The name 'PolicyEffect' does not exist in the current context`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Enums/PolicyEffect.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What a fragment does to a feature. Values ascend by authority, so when two fragments tie on
/// level, wildcard specificity, and priority, the greater value wins.
/// </summary>
public enum PolicyEffect
{
    /// <summary>The feature is permitted.</summary>
    Allow = 0,

    /// <summary>The feature is permitted, but the value is transformed on output.</summary>
    Mask = 1,

    /// <summary>The feature is refused.</summary>
    Deny = 2
}
```

Create `DynamicWhere.ex/Policies/Enums/PolicyLevel.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// Precedence levels, ordered most authoritative first. The lowest-numbered level that supplies
/// any fragment for a given field and feature decides that feature; levels below it are ignored
/// entirely rather than merged.
/// </summary>
public enum PolicyLevel
{
    /// <summary>
    /// A compile-time attribute with <c>Overridable = false</c>. Absolute — no runtime rule can
    /// loosen or replace it.
    /// </summary>
    SealedAttribute = 1,

    /// <summary>A runtime rule targeting one user.</summary>
    DynamicUser = 2,

    /// <summary>A runtime rule targeting a role.</summary>
    DynamicRole = 3,

    /// <summary>A runtime rule targeting a tenant.</summary>
    DynamicTenant = 4,

    /// <summary>A runtime rule with no subject, applying to everyone.</summary>
    DynamicGlobal = 5,

    /// <summary>
    /// A compile-time attribute with <c>Overridable = true</c>. Acts as a default that any
    /// runtime rule may replace.
    /// </summary>
    OverridableAttribute = 6
}
```

Create `DynamicWhere.ex/Policies/Enums/DwSubjectKind.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The kind of principal a subject identifies. Maps onto the dynamic precedence levels.
/// </summary>
public enum DwSubjectKind
{
    /// <summary>Applies to every caller. Carries no identity.</summary>
    Global = 0,

    /// <summary>A tenant or organisation.</summary>
    Tenant = 1,

    /// <summary>A role or group.</summary>
    Role = 2,

    /// <summary>A single user.</summary>
    User = 3,

    /// <summary>A caller-defined dimension, resolved at the tenant level.</summary>
    Custom = 4
}
```

Create `DynamicWhere.ex/Policies/Enums/DwTier.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The enforcement posture, fixed once at startup.
/// </summary>
public enum DwTier
{
    /// <summary>
    /// The caller is the application's own front end. A blocked sort or projection is dropped
    /// quietly; a blocked filter still throws, because dropping a filter widens the result set.
    /// </summary>
    Convenience = 0,

    /// <summary>
    /// The caller is hostile or semi-trusted. Every blocked request throws.
    /// </summary>
    Strict = 1
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 4 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Enums/ DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): add effect, level, subject-kind, and tier enums"
```

---

## Task 3: DwSubject and DwPolicyContext

**Files:**
- Create: `DynamicWhere.ex/Policies/Context/DwSubject.cs`
- Create: `DynamicWhere.ex/Policies/Context/DwPolicyContext.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyContextTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyContextTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="DwPolicyContext"/> construction, subject lookup, and ambient value access.
/// </summary>
public class PolicyContextTests
{
    [Fact]
    public void Context_exposes_the_subjects_it_was_built_with()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.User, "123")
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Auditor");

        Assert.Equal(3, ctx.Subjects.Count);
        Assert.Contains(ctx.Subjects, s => s.Kind == DwSubjectKind.User && s.Identity == "123");
        Assert.Equal(2, ctx.Subjects.Count(s => s.Kind == DwSubjectKind.Role));
    }

    [Fact]
    public void Identities_returns_every_identity_for_one_kind()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Auditor");

        string[] roles = ctx.Identities(DwSubjectKind.Role).OrderBy(r => r, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Auditor", "Manager" }, roles);
    }

    [Fact]
    public void Ambient_values_round_trip()
    {
        DwPolicyContext ctx = new DwPolicyContext().WithValue("TenantId", 5);

        Assert.True(ctx.TryGetValue("TenantId", out object? value));
        Assert.Equal(5, value);
        Assert.False(ctx.TryGetValue("Missing", out _));
    }

    [Fact]
    public void Subject_identity_may_not_be_blank_for_an_identified_kind()
    {
        DwPolicyContext ctx = new DwPolicyContext();

        Assert.Throws<ArgumentException>(() => ctx.WithSubject(DwSubjectKind.Role, "  "));
    }

    [Fact]
    public void Duplicate_subjects_are_ignored_rather_than_stored_twice()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Manager");

        Assert.Single(ctx.Subjects);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyContextTests"`
Expected: FAIL to compile — `The type or namespace name 'Context' does not exist in the namespace 'DynamicWhere.ex.Policies'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Context/DwSubject.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// One principal dimension of the caller — a role they hold, the tenant they belong to, or their
/// own user identity. A caller is described by several of these at once.
/// </summary>
public sealed class DwSubject : IEquatable<DwSubject>
{
    /// <summary>
    /// Initializes a subject.
    /// </summary>
    /// <param name="kind">The dimension this subject describes.</param>
    /// <param name="identity">
    /// The identity within that dimension. Ignored and stored as an empty string when
    /// <paramref name="kind"/> is <see cref="DwSubjectKind.Global"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="identity"/> is null or whitespace for any kind other than
    /// <see cref="DwSubjectKind.Global"/>.
    /// </exception>
    public DwSubject(DwSubjectKind kind, string identity)
    {
        if (kind != DwSubjectKind.Global && string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException($"A subject of kind '{kind}' requires an identity.", nameof(identity));
        }

        Kind = kind;
        Identity = kind == DwSubjectKind.Global ? string.Empty : identity.Trim();
    }

    /// <summary>The dimension this subject describes.</summary>
    public DwSubjectKind Kind { get; }

    /// <summary>The identity within that dimension, or an empty string for Global.</summary>
    public string Identity { get; }

    /// <inheritdoc />
    public bool Equals(DwSubject? other) =>
        other is not null && other.Kind == Kind &&
        string.Equals(other.Identity, Identity, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DwSubject);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Identity);

    /// <inheritdoc />
    public override string ToString() =>
        Kind == DwSubjectKind.Global ? "Global" : $"{Kind}:{Identity}";
}
```

Create `DynamicWhere.ex/Policies/Context/DwPolicyContext.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// Describes who is asking, and carries the ambient values a policy may need. Framework-agnostic
/// by design: the core library has no dependency on ASP.NET Core, and an adapter from
/// <c>ClaimsPrincipal</c> ships separately.
/// </summary>
/// <remarks>
/// Build one per request through an asynchronous factory so that any user-level rules are
/// preloaded; every downstream query then resolves policy synchronously against in-memory state.
/// </remarks>
public sealed class DwPolicyContext
{
    private readonly List<DwSubject> _subjects = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>The principal dimensions describing the caller.</summary>
    public IReadOnlyList<DwSubject> Subjects => _subjects;

    /// <summary>
    /// When true, no policy decision throws or drops anything. Every decision is still recorded,
    /// which lets one canary subject run unenforced while everyone else is enforced.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// An optional declared purpose for the query, for purpose-bound rules.
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// Adds a subject. Adding the same kind and identity twice is a no-op.
    /// </summary>
    /// <returns>This context, for chaining.</returns>
    public DwPolicyContext WithSubject(DwSubjectKind kind, string identity)
    {
        DwSubject subject = new(kind, identity);

        if (!_subjects.Contains(subject))
        {
            _subjects.Add(subject);
        }

        return this;
    }

    /// <summary>
    /// Sets an ambient value, replacing any existing value under the same key.
    /// </summary>
    /// <returns>This context, for chaining.</returns>
    public DwPolicyContext WithValue(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("An ambient value requires a key.", nameof(key));
        }

        _values[key] = value;

        return this;
    }

    /// <summary>
    /// Returns every identity held for one kind. Empty when the caller holds none.
    /// </summary>
    public IEnumerable<string> Identities(DwSubjectKind kind) =>
        _subjects.Where(s => s.Kind == kind).Select(s => s.Identity);

    /// <summary>
    /// Looks up an ambient value.
    /// </summary>
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyContextTests"`
Expected: PASS, 5 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Context/ DynamicWhere.Tests/Policies/PolicyContextTests.cs
git commit -m "feat(policies): add DwSubject and DwPolicyContext"
```

---

## Task 4: PolicySource and PolicyFragment

**Files:**
- Create: `DynamicWhere.ex/Policies/DTOs/PolicySource.cs`
- Create: `DynamicWhere.ex/Policies/DTOs/PolicyFragment.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyFragmentTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyFragmentTests.cs`:

```csharp
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyFragment"/> construction and its field-path matching, which decides
/// whether a wildcard rule applies to a given field.
/// </summary>
public class PolicyFragmentTests
{
    private static PolicyFragment Frag(string path, PolicyFeature features, PolicyEffect effect) =>
        new(path, features, effect, PolicyLevel.DynamicRole, PolicySource.FromRule("r1", "Role:Manager"));

    [Fact]
    public void Exact_path_matches_only_itself()
    {
        PolicyFragment fragment = Frag("Salary", PolicyFeature.Select, PolicyEffect.Deny);

        Assert.True(fragment.Matches("Salary"));
        Assert.False(fragment.Matches("Name"));
        Assert.False(fragment.IsWildcard);
    }

    [Fact]
    public void Path_matching_is_case_insensitive()
    {
        PolicyFragment fragment = Frag("Salary", PolicyFeature.Select, PolicyEffect.Deny);

        Assert.True(fragment.Matches("salary"));
        Assert.True(fragment.Matches("SALARY"));
    }

    [Fact]
    public void Wildcard_matches_every_path()
    {
        PolicyFragment fragment = Frag("*", PolicyFeature.All, PolicyEffect.Deny);

        Assert.True(fragment.IsWildcard);
        Assert.True(fragment.Matches("Salary"));
        Assert.True(fragment.Matches("ContactInfo.Email"));
    }

    [Fact]
    public void Fragment_reports_whether_it_speaks_to_a_feature()
    {
        PolicyFragment fragment =
            Frag("Salary", PolicyFeature.Select | PolicyFeature.Order, PolicyEffect.Deny);

        Assert.True(fragment.Covers(PolicyFeature.Select));
        Assert.True(fragment.Covers(PolicyFeature.Order));
        Assert.False(fragment.Covers(PolicyFeature.Where));
    }

    [Fact]
    public void Fragment_requires_a_field_path()
    {
        Assert.Throws<ArgumentException>(() =>
            new PolicyFragment(" ", PolicyFeature.All, PolicyEffect.Deny,
                PolicyLevel.DynamicGlobal, PolicySource.FromRule("r1", "Global")));
    }

    [Fact]
    public void Attribute_source_records_the_attribute_name_and_whether_it_was_sealed()
    {
        PolicySource source = PolicySource.FromAttribute("DwDeniedAttribute", isSealed: true);

        Assert.Equal("DwDeniedAttribute", source.Origin);
        Assert.True(source.IsSealed);
        Assert.Null(source.RuleId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyFragmentTests"`
Expected: FAIL to compile — `The type or namespace name 'DTOs' does not exist in the namespace 'DynamicWhere.ex.Policies'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/DTOs/PolicySource.cs`:

```csharp
namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Where a fragment came from. Carried through resolution into the trace and the explain endpoint,
/// so an operator can see not only what was decided but which attribute or rule decided it.
/// </summary>
public sealed class PolicySource
{
    private PolicySource(string origin, string? ruleId, string? subject, bool isSealed)
    {
        Origin = origin;
        RuleId = ruleId;
        Subject = subject;
        IsSealed = isSealed;
    }

    /// <summary>The attribute type name, or a description of the rule's subject.</summary>
    public string Origin { get; }

    /// <summary>The identifier of the runtime rule, or null when the source is an attribute.</summary>
    public string? RuleId { get; }

    /// <summary>The subject the rule targeted, or null when the source is an attribute.</summary>
    public string? Subject { get; }

    /// <summary>True when the source is a compile-time attribute that runtime rules cannot override.</summary>
    public bool IsSealed { get; }

    /// <summary>Creates a source describing a compile-time attribute.</summary>
    public static PolicySource FromAttribute(string attributeName, bool isSealed) =>
        new(attributeName, ruleId: null, subject: null, isSealed);

    /// <summary>Creates a source describing a runtime rule.</summary>
    public static PolicySource FromRule(string ruleId, string subject) =>
        new($"Rule {ruleId}", ruleId, subject, isSealed: false);

    /// <inheritdoc />
    public override string ToString() =>
        RuleId is null ? $"{Origin}{(IsSealed ? " (sealed)" : string.Empty)}" : $"{Origin} [{Subject}]";
}
```

Create `DynamicWhere.ex/Policies/DTOs/PolicyFragment.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// One contribution to one field's policy, from one source, at one precedence level. Attributes
/// and runtime rules both compile into fragments, so the resolver never learns where a policy
/// came from — only how authoritative it is.
/// </summary>
public sealed class PolicyFragment
{
    /// <summary>The path token meaning "every field of this type".</summary>
    public const string Wildcard = "*";

    /// <summary>
    /// Initializes a fragment.
    /// </summary>
    /// <param name="fieldPath">A field path, or <see cref="Wildcard"/>.</param>
    /// <param name="features">The features this fragment speaks to.</param>
    /// <param name="effect">What it does to those features.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="source">Where it came from, for tracing.</param>
    /// <param name="priority">Tiebreak within a level. Higher wins.</param>
    /// <param name="payload">Strategy detail, unused until masking arrives.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    public PolicyFragment(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        PolicySource source,
        int priority = 0,
        object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A fragment requires a field path.", nameof(fieldPath));
        }

        FieldPath = fieldPath.Trim();
        Features = features;
        Effect = effect;
        Level = level;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Priority = priority;
        Payload = payload;
    }

    /// <summary>The field path this fragment addresses, or <see cref="Wildcard"/>.</summary>
    public string FieldPath { get; }

    /// <summary>The features this fragment speaks to.</summary>
    public PolicyFeature Features { get; }

    /// <summary>What this fragment does to those features.</summary>
    public PolicyEffect Effect { get; }

    /// <summary>How authoritative this fragment is.</summary>
    public PolicyLevel Level { get; }

    /// <summary>Where this fragment came from.</summary>
    public PolicySource Source { get; }

    /// <summary>Tiebreak within a level. Higher wins.</summary>
    public int Priority { get; }

    /// <summary>Strategy detail carried opaquely through resolution.</summary>
    public object? Payload { get; }

    /// <summary>True when this fragment addresses every field.</summary>
    public bool IsWildcard => FieldPath == Wildcard;

    /// <summary>
    /// True when this fragment addresses the given path. Comparison is case-insensitive because
    /// field paths arrive from JSON written by hand.
    /// </summary>
    public bool Matches(string fieldPath) =>
        IsWildcard || string.Equals(FieldPath, fieldPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when this fragment speaks to the given feature.</summary>
    public bool Covers(PolicyFeature feature) => (Features & feature) == feature;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyFragmentTests"`
Expected: PASS, 6 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/DTOs/ DynamicWhere.Tests/Policies/PolicyFragmentTests.cs
git commit -m "feat(policies): add PolicySource and PolicyFragment"
```

---

## Task 5: FieldPolicy

**Files:**
- Create: `DynamicWhere.ex/Policies/DTOs/FieldPolicy.cs`
- Create: `DynamicWhere.Tests/Policies/FieldPolicyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/FieldPolicyTests.cs`:

```csharp
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="FieldPolicy"/>, the immutable answer the resolver produces and the only
/// policy type enforcement code sees.
/// </summary>
public class FieldPolicyTests
{
    [Fact]
    public void A_policy_built_from_effects_reports_each_feature()
    {
        Dictionary<PolicyFeature, PolicyEffect> effects = new()
        {
            [PolicyFeature.Where] = PolicyEffect.Allow,
            [PolicyFeature.Select] = PolicyEffect.Deny,
            [PolicyFeature.Order] = PolicyEffect.Mask
        };

        FieldPolicy policy = new("Salary", effects, Array.Empty<PolicySource>(), isSealed: false);

        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Order));
        Assert.Equal(PolicyEffect.Mask, policy.EffectFor(PolicyFeature.Order));
    }

    [Fact]
    public void A_feature_with_no_recorded_effect_is_allowed()
    {
        FieldPolicy policy = new(
            "Name",
            new Dictionary<PolicyFeature, PolicyEffect>(),
            Array.Empty<PolicySource>(),
            isSealed: false);

        Assert.True(policy.Allows(PolicyFeature.Group));
        Assert.Equal(PolicyEffect.Allow, policy.EffectFor(PolicyFeature.Group));
    }

    [Fact]
    public void Masked_reports_only_features_whose_effect_is_mask()
    {
        Dictionary<PolicyFeature, PolicyEffect> effects = new()
        {
            [PolicyFeature.Select] = PolicyEffect.Mask,
            [PolicyFeature.Where] = PolicyEffect.Allow
        };

        FieldPolicy policy = new("Salary", effects, Array.Empty<PolicySource>(), isSealed: false);

        Assert.True(policy.IsMasked(PolicyFeature.Select));
        Assert.False(policy.IsMasked(PolicyFeature.Where));
    }

    [Fact]
    public void Sources_are_exposed_for_tracing()
    {
        PolicySource[] sources = { PolicySource.FromAttribute("DwDeniedAttribute", isSealed: true) };

        FieldPolicy policy = new(
            "NationalId",
            new Dictionary<PolicyFeature, PolicyEffect> { [PolicyFeature.Select] = PolicyEffect.Deny },
            sources,
            isSealed: true);

        Assert.True(policy.IsSealed);
        Assert.Single(policy.Sources);
        Assert.Equal("DwDeniedAttribute", policy.Sources[0].Origin);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~FieldPolicyTests"`
Expected: FAIL to compile — `The name 'FieldPolicy' does not exist in the current context`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/DTOs/FieldPolicy.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// The resolved policy for one field of one type, for one caller. Immutable, and the only policy
/// type the enforcement code sees — attributes and runtime rules have already been merged away by
/// the time one of these exists.
/// </summary>
public sealed class FieldPolicy
{
    private readonly IReadOnlyDictionary<PolicyFeature, PolicyEffect> _effects;

    /// <summary>
    /// Initializes a resolved policy.
    /// </summary>
    /// <param name="fieldPath">The field this policy governs.</param>
    /// <param name="effects">The effect decided for each feature. A feature absent here is allowed.</param>
    /// <param name="sources">Every fragment source that contributed, for tracing.</param>
    /// <param name="isSealed">True when a compile-time attribute decided at least one feature absolutely.</param>
    public FieldPolicy(
        string fieldPath,
        IReadOnlyDictionary<PolicyFeature, PolicyEffect> effects,
        IReadOnlyList<PolicySource> sources,
        bool isSealed)
    {
        FieldPath = fieldPath;
        _effects = effects;
        Sources = sources;
        IsSealed = isSealed;
    }

    /// <summary>The field this policy governs.</summary>
    public string FieldPath { get; }

    /// <summary>Every fragment source that contributed to this decision.</summary>
    public IReadOnlyList<PolicySource> Sources { get; }

    /// <summary>True when a compile-time attribute decided at least one feature absolutely.</summary>
    public bool IsSealed { get; }

    /// <summary>
    /// The effect decided for a feature. Features with no fragment resolve to
    /// <see cref="PolicyEffect.Allow"/>.
    /// </summary>
    public PolicyEffect EffectFor(PolicyFeature feature) =>
        _effects.TryGetValue(feature, out PolicyEffect effect) ? effect : PolicyEffect.Allow;

    /// <summary>
    /// True when the feature may proceed. A masked feature still proceeds — the value is
    /// transformed on output, not withheld from the query.
    /// </summary>
    public bool Allows(PolicyFeature feature) => EffectFor(feature) != PolicyEffect.Deny;

    /// <summary>True when the feature proceeds but its output value is transformed.</summary>
    public bool IsMasked(PolicyFeature feature) => EffectFor(feature) == PolicyEffect.Mask;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~FieldPolicyTests"`
Expected: PASS, 4 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/DTOs/FieldPolicy.cs DynamicWhere.Tests/Policies/FieldPolicyTests.cs
git commit -m "feat(policies): add resolved FieldPolicy model"
```

---

## Task 6: Attributes

**Files:**
- Create: `DynamicWhere.ex/Policies/Attributes/DwPolicyAttribute.cs`
- Create: `DynamicWhere.ex/Policies/Attributes/DwDenyAttribute.cs`
- Create: `DynamicWhere.ex/Policies/Attributes/DwDenySugar.cs`
- Create: `DynamicWhere.ex/Policies/Attributes/DwEntityAttribute.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyAttributeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyAttributeTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the attribute surface: the composable primitive, the six named subclasses, and the
/// sealed-by-default rule that keeps a runtime rule from loosening a compile-time denial.
/// </summary>
public class PolicyAttributeTests
{
    [Fact]
    public void Deny_is_sealed_unless_explicitly_made_overridable()
    {
        DwDenyAttribute sealedByDefault = new(PolicyFeature.Select);
        DwDenyAttribute opened = new(PolicyFeature.Select) { Overridable = true };

        Assert.False(sealedByDefault.Overridable);
        Assert.True(opened.Overridable);
    }

    [Theory]
    [InlineData(typeof(DwDeniedAttribute), PolicyFeature.All)]
    [InlineData(typeof(DwNoWhereAttribute), PolicyFeature.Where)]
    [InlineData(typeof(DwNoSelectAttribute), PolicyFeature.Select)]
    [InlineData(typeof(DwNoOrderAttribute), PolicyFeature.Order)]
    [InlineData(typeof(DwNoGroupAttribute), PolicyFeature.Group)]
    [InlineData(typeof(DwNoAggregateAttribute), PolicyFeature.Aggregate)]
    public void Each_sugar_attribute_denies_exactly_its_feature(Type attributeType, PolicyFeature expected)
    {
        DwDenyAttribute attribute = (DwDenyAttribute)Activator.CreateInstance(attributeType)!;

        Assert.Equal(expected, attribute.Features);
    }

    [Fact]
    public void Attributes_may_be_stacked_on_one_property()
    {
        AttributeUsageAttribute usage = typeof(DwPolicyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: true)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.AllowMultiple);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
    }

    [Fact]
    public void Entity_attribute_defaults_to_not_requiring_a_policy()
    {
        DwEntityAttribute relaxed = new();
        DwEntityAttribute guarded = new() { RequirePolicy = true };

        Assert.False(relaxed.RequirePolicy);
        Assert.True(guarded.RequirePolicy);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyAttributeTests"`
Expected: FAIL to compile — `The type or namespace name 'Attributes' does not exist in the namespace 'DynamicWhere.ex.Policies'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Attributes/DwPolicyAttribute.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Base for every field-level policy attribute.
/// </summary>
/// <remarks>
/// Attributes are sealed by default: a runtime rule may make a field's policy stricter but never
/// looser, unless the attribute author opts in with <see cref="Overridable"/>. That keeps a
/// compromised or misconfigured policy store from granting access the source code denies.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public abstract class DwPolicyAttribute : Attribute
{
    /// <summary>
    /// When true, a runtime rule may replace this attribute's decision. Defaults to false, which
    /// makes the decision absolute.
    /// </summary>
    public bool Overridable { get; set; }
}
```

Create `DynamicWhere.ex/Policies/Attributes/DwDenyAttribute.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Refuses one or more query features for the decorated member. The composable primitive behind
/// the named attributes such as <see cref="DwDeniedAttribute"/> and <see cref="DwNoWhereAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// [DwDeny(PolicyFeature.Select | PolicyFeature.Order)]
/// public string InternalNotes { get; set; }
/// </code>
/// </example>
public class DwDenyAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="features">The features to refuse.</param>
    public DwDenyAttribute(PolicyFeature features) => Features = features;

    /// <summary>The features this attribute refuses.</summary>
    public PolicyFeature Features { get; }
}
```

Create `DynamicWhere.ex/Policies/Attributes/DwDenySugar.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>Refuses every query feature. The common case for a field that is simply off-limits.</summary>
public sealed class DwDeniedAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwDeniedAttribute() : base(PolicyFeature.All) { }
}

/// <summary>Refuses filtering on the decorated member.</summary>
public sealed class DwNoWhereAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoWhereAttribute() : base(PolicyFeature.Where) { }
}

/// <summary>Refuses projecting the decorated member.</summary>
public sealed class DwNoSelectAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoSelectAttribute() : base(PolicyFeature.Select) { }
}

/// <summary>Refuses sorting by the decorated member.</summary>
public sealed class DwNoOrderAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoOrderAttribute() : base(PolicyFeature.Order) { }
}

/// <summary>Refuses grouping by the decorated member.</summary>
public sealed class DwNoGroupAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoGroupAttribute() : base(PolicyFeature.Group) { }
}

/// <summary>Refuses aggregating the decorated member.</summary>
public sealed class DwNoAggregateAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoAggregateAttribute() : base(PolicyFeature.Aggregate) { }
}
```

Create `DynamicWhere.ex/Policies/Attributes/DwEntityAttribute.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Declares type-level policy behaviour.
/// </summary>
/// <remarks>
/// <see cref="RequirePolicy"/> closes the hole in an opt-in model: without it, every field policy
/// on a type is bypassed simply by not applying a policy context to the query. Enforcement of this
/// flag lands with the guarded query handle.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DwEntityAttribute : Attribute
{
    /// <summary>
    /// When true, querying this type without a policy context throws instead of returning rows.
    /// </summary>
    public bool RequirePolicy { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyAttributeTests"`
Expected: PASS, 9 tests (4 facts plus 6 theory cases, minus the shared fact count — xUnit reports 9)

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Attributes/ DynamicWhere.Tests/Policies/PolicyAttributeTests.cs
git commit -m "feat(policies): add policy attributes and the sealed-by-default base"
```

---

## Task 7: Provider abstraction and the fake provider

**Files:**
- Create: `DynamicWhere.ex/Policies/Resolution/IDwPolicyProvider.cs`
- Create: `DynamicWhere.Tests/Policies/FakePolicyProvider.cs`

The fake exists so the six-level precedence matrix can be tested now, before any real store is
written. Phase 5 swaps in the real store provider against this same interface.

- [ ] **Step 1: Write the failing test**

Add to `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void Fake_provider_returns_the_fragments_it_was_given()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        IReadOnlyList<PolicyFragment> fragments =
            provider.GetFragments(typeof(object), new DwPolicyContext());

        Assert.Single(fragments);
        Assert.Equal("Salary", fragments[0].FieldPath);
        Assert.Equal(PolicyLevel.DynamicRole, fragments[0].Level);
    }
```

Add these usings to the top of the file:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL to compile — `The type or namespace name 'Resolution' does not exist in the namespace 'DynamicWhere.ex.Policies'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Resolution/IDwPolicyProvider.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Supplies policy fragments for a type and a caller. Implemented once over reflection for
/// compile-time attributes and once over the policy store for runtime rules; the resolver merges
/// whatever it is given without knowing which is which.
/// </summary>
/// <remarks>
/// Implementations are called on the query path and must not perform I/O. A store-backed provider
/// reads an in-memory snapshot that a background refresh keeps current.
/// </remarks>
public interface IDwPolicyProvider
{
    /// <summary>
    /// Returns every fragment this provider has for the given type and caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="context">The caller.</param>
    IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context);
}
```

Create `DynamicWhere.Tests/Policies/FakePolicyProvider.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A provider that returns exactly the fragments a test hands it. Lets the precedence matrix be
/// exercised at every level before a real policy store exists, and keeps resolver tests free of
/// reflection and storage concerns.
/// </summary>
internal sealed class FakePolicyProvider : IDwPolicyProvider
{
    private readonly List<PolicyFragment> _fragments = new();

    /// <summary>
    /// Adds a fragment.
    /// </summary>
    /// <param name="fieldPath">A field path, or <c>"*"</c> for every field.</param>
    /// <param name="features">The features the fragment speaks to.</param>
    /// <param name="effect">What it does to them.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="priority">Tiebreak within the level. Higher wins.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider Add(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        int priority = 0)
    {
        PolicySource source = level is PolicyLevel.SealedAttribute or PolicyLevel.OverridableAttribute
            ? PolicySource.FromAttribute($"Fake{effect}Attribute", level == PolicyLevel.SealedAttribute)
            : PolicySource.FromRule($"{level}-{_fragments.Count}", level.ToString());

        _fragments.Add(new PolicyFragment(fieldPath, features, effect, level, source, priority));

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) => _fragments;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 5 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/IDwPolicyProvider.cs DynamicWhere.Tests/Policies/FakePolicyProvider.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): add provider abstraction and test fake"
```

---

## Task 7A: DTO consistency pass

Added during execution. Tasks 4 and 5 each landed byte-exact to plan, and each surfaced a small
inconsistency in the DTO layer that only became visible once the neighbouring types existed. They
are collected here rather than patched piecemeal, and run after Task 7 so the whole DTO surface is
present at once.

None of these is a defect in the commit that introduced it. All four are cheap now and awkward
later, because Phase 2 onward treats `FieldPolicy` as trusted and `PolicyFragment` as the thing
providers emit.

**Files:**
- Modify: `DynamicWhere.ex/Policies/DTOs/FieldPolicy.cs`
- Modify: `DynamicWhere.ex/Policies/DTOs/PolicySource.cs`
- Modify: `DynamicWhere.ex/Policies/DTOs/PolicyFragment.cs`
- Modify: `DynamicWhere.Tests/Policies/FieldPolicyTests.cs`

- [ ] **Item 1 — `FieldPolicy` claims immutability it does not enforce.**
  It stores the supplied `IReadOnlyDictionary` and `IReadOnlyList` by reference, so a caller
  retaining the original `Dictionary` can mutate a resolved policy after construction. Do **not**
  add a defensive copy: the resolver allocates both collections per field per query, and copying
  would double that on the hot path for a caller that does not exist. Instead make the contract
  honest — document on the constructor that the instance takes ownership of both collections and
  that callers must not retain or mutate them afterwards.

- [ ] **Item 2 — `FieldPolicy` validates nothing while `PolicyFragment` validates its path and source.**
  Bring `FieldPolicy` up to the same standard: reject a null or blank `fieldPath`, and reject null
  `effects` or `sources`.

- [ ] **Item 3 — `PolicySource.FromAttribute` and `FromRule` accept blank strings.**
  A blank `ruleId` yields the origin string `"Rule "`, which reaches the explain endpoint and the
  trace. Reject blank arguments in both factories.

- [ ] **Item 4 — `PolicyFragment`'s constructor throws `ArgumentNullException` for a null source but documents only `ArgumentException`.**
  Add the missing `<exception>` tag. Note this is narrower than the `WithValue` case reviewed and
  declined in Tasks 2–3: that one was an undocumented throw with no sibling tag, matching repo
  precedent in `CacheReflection.Configure`. This one has a tag that is actively incomplete.

Each item needs a test proving the new guard, written and seen to fail first.

**Commit:** `refactor(policies): make DTO validation and ownership contracts consistent`

---

## Task 8: Resolver — one level, one feature

**Files:**
- Create: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    private static PolicyResolver Resolver(params IDwPolicyProvider[] providers) => new(providers);

    [Fact]
    public void A_single_deny_fragment_denies_that_feature_and_leaves_others_alone()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Order));
    }

    [Fact]
    public void A_field_with_no_fragments_allows_everything()
    {
        FieldPolicy policy = Resolver(new FakePolicyProvider())
            .Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Select));
        Assert.Empty(policy.Sources);
    }

    [Fact]
    public void A_multi_feature_fragment_applies_to_every_feature_it_names()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select | PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Order));
        Assert.True(policy.Allows(PolicyFeature.Group));
    }

    [Fact]
    public void Fragments_for_other_fields_are_ignored()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_padded_field_path_still_matches_its_fragments()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "  Salary  ", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.Equal("Salary", policy.FieldPath);
    }

    [Fact]
    public void A_blank_field_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Resolver(new FakePolicyProvider()).Resolve(typeof(object), "  ", new DwPolicyContext()));
    }
```

The last two tests were added during execution, after Task 7A found that `PolicyFragment` trims its stored
path while `FieldPolicy` did not. An untrimmed lookup would fail to match a fragment targeting the same
field, and a `Deny` fragment that fails to match is access granted — the same fail-open shape as the
subject-casing bug, on a different axis. Normalizing once at the resolver boundary closes it in one place
rather than trimming in two DTOs and hoping they stay in sync.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL to compile — `The name 'PolicyResolver' does not exist in the current context`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Merges the fragments supplied by every provider into one immutable <see cref="FieldPolicy"/>.
/// </summary>
/// <remarks>
/// Each feature is decided independently. For one feature, the most authoritative level that
/// supplies any fragment decides it outright; less authoritative levels are discarded rather than
/// merged, which is what makes a sealed attribute absolute and lets a role rule replace an
/// overridable attribute default.
/// </remarks>
public sealed class PolicyResolver
{
    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
        PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
    };

    private readonly IReadOnlyList<IDwPolicyProvider> _providers;

    /// <summary>
    /// Initializes the resolver.
    /// </summary>
    /// <param name="providers">The fragment sources, in any order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    public PolicyResolver(IEnumerable<IDwPolicyProvider> providers) =>
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));

    /// <summary>
    /// Resolves the policy for one field of one type, for one caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="fieldPath">The field path, as it appears after alias resolution.</param>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    public FieldPolicy Resolve(Type entityType, string fieldPath, DwPolicyContext context)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A policy lookup requires a field path.", nameof(fieldPath));
        }

        // Normalize once, here, because this is where paths enter the policy system. PolicyFragment
        // trims its own path at construction, so an untrimmed lookup would fail to match a fragment
        // that targets the same field — and a Deny fragment that fails to match is access granted.
        string path = fieldPath.Trim();

        List<PolicyFragment> candidates = new();

        foreach (IDwPolicyProvider provider in _providers)
        {
            foreach (PolicyFragment fragment in provider.GetFragments(entityType, context))
            {
                if (fragment.Matches(path))
                {
                    candidates.Add(fragment);
                }
            }
        }

        Dictionary<PolicyFeature, PolicyEffect> effects = new();
        List<PolicySource> sources = new();
        bool isSealed = false;

        foreach (PolicyFeature feature in Features)
        {
            PolicyFragment? winner = Decide(candidates, feature);

            if (winner is null)
            {
                continue;
            }

            effects[feature] = winner.Effect;

            if (!sources.Contains(winner.Source))
            {
                sources.Add(winner.Source);
            }

            isSealed |= winner.Level == PolicyLevel.SealedAttribute;
        }

        return new FieldPolicy(path, effects, sources, isSealed);
    }

    /// <summary>
    /// Picks the single fragment that decides one feature, or null when none speaks to it.
    /// </summary>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        List<PolicyFragment> speaking = candidates.Where(f => f.Covers(feature)).ToList();

        return speaking.Count == 0 ? null : speaking[0];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 9 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): add PolicyResolver with per-feature resolution"
```

---

## Task 9: Resolver — level precedence

`Decide` currently returns the first speaking fragment. This task makes it honour level.

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_role_rule_replaces_an_overridable_attribute_default()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.OverridableAttribute)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_user_rule_beats_a_role_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicUser);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_global_rule_loses_to_a_tenant_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicTenant);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Levels_are_decided_per_feature_not_once_for_the_whole_field()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicUser)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.OverridableAttribute)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Select));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL — `A_role_rule_replaces_an_overridable_attribute_default` asserts True but gets False, because `Decide` still returns the first fragment added

- [ ] **Step 3: Write minimal implementation**

Replace the `Decide` method in `PolicyResolver.cs` with:

```csharp
    /// <summary>
    /// Picks the single fragment that decides one feature, or null when none speaks to it.
    /// </summary>
    /// <remarks>
    /// The most authoritative level that supplies any fragment wins outright. Levels below it are
    /// discarded, not merged — that is what makes a sealed attribute absolute.
    /// </remarks>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        List<PolicyFragment> speaking = candidates.Where(f => f.Covers(feature)).ToList();

        if (speaking.Count == 0)
        {
            return null;
        }

        PolicyLevel best = speaking.Min(f => f.Level);

        return speaking.First(f => f.Level == best);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 13 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): honour precedence level when resolving a feature"
```

---

## Task 10: Resolver — exact field beats wildcard

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void An_exact_field_rule_beats_a_wildcard_rule_at_the_same_level()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_wildcard_still_applies_to_fields_with_no_exact_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_wildcard_at_a_higher_level_still_beats_an_exact_rule_at_a_lower_one()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicUser)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL — `An_exact_field_rule_beats_a_wildcard_rule_at_the_same_level` asserts True but gets False, because the wildcard fragment was added first and both sit at the same level

- [ ] **Step 3: Write minimal implementation**

Replace the `Decide` method in `PolicyResolver.cs` with:

```csharp
    /// <summary>
    /// Picks the single fragment that decides one feature, or null when none speaks to it.
    /// </summary>
    /// <remarks>
    /// Level is compared first: the most authoritative level that supplies any fragment wins
    /// outright, and levels below it are discarded rather than merged. Within that level a rule
    /// naming the field beats a wildcard, so a broad denial can be relaxed field by field without
    /// deleting it.
    /// </remarks>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        List<PolicyFragment> speaking = candidates.Where(f => f.Covers(feature)).ToList();

        if (speaking.Count == 0)
        {
            return null;
        }

        PolicyLevel best = speaking.Min(f => f.Level);

        List<PolicyFragment> atLevel = speaking.Where(f => f.Level == best).ToList();

        List<PolicyFragment> exact = atLevel.Where(f => !f.IsWildcard).ToList();

        if (exact.Count > 0)
        {
            atLevel = exact;
        }

        return atLevel[0];
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 16 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): prefer exact field rules over wildcards within a level"
```

---

## Task 11: Resolver — priority then effect tiebreak

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void Higher_priority_wins_within_a_level()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole, priority: 1)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole, priority: 10);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Deny_beats_allow_when_two_roles_tie_on_priority()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Deny_beats_mask_and_mask_beats_allow_on_a_three_way_tie()
    {
        FakePolicyProvider maskOverAllow = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Mask, PolicyLevel.DynamicRole);

        FieldPolicy masked = Resolver(maskOverAllow)
            .Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Mask, masked.EffectFor(PolicyFeature.Select));

        FakePolicyProvider denyOverMask = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Mask, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy denied = Resolver(denyOverMask)
            .Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Deny, denied.EffectFor(PolicyFeature.Select));
    }

    [Fact]
    public void Priority_is_compared_before_effect()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole, priority: 1)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole, priority: 5);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: FAIL — `Higher_priority_wins_within_a_level` asserts True but gets False, because `Decide` returns the first fragment at the level regardless of priority

- [ ] **Step 3: Write minimal implementation**

Replace the final `return atLevel[0];` in `Decide` with:

```csharp
        int topPriority = atLevel.Max(f => f.Priority);

        List<PolicyFragment> contenders = atLevel.Where(f => f.Priority == topPriority).ToList();

        PolicyEffect strongest = contenders.Max(f => f.Effect);

        return contenders.First(f => f.Effect == strongest);
```

And update the `Decide` XML remarks to:

```csharp
    /// <remarks>
    /// Comparison runs in four passes. Level first: the most authoritative level that supplies any
    /// fragment wins outright, and levels below it are discarded rather than merged. Then
    /// specificity, so a rule naming the field beats a wildcard. Then priority, highest first.
    /// Whatever still ties is settled by effect, where Deny beats Mask and Mask beats Allow — which
    /// is what makes a caller holding two roles fall to the stricter of them.
    /// </remarks>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 20 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "feat(policies): settle ties by priority then by strongest effect"
```

---

## Task 12: Resolver — sealed attributes are absolute

**Files:**
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

No implementation change is expected — `PolicyLevel.SealedAttribute` is already the lowest value,
so the level pass handles this. These tests pin the guarantee so a later refactor cannot quietly
break it.

- [ ] **Step 1: Write the failing test**

Add to `PolicyResolutionTests.cs`, inside the class:

```csharp
    [Fact]
    public void No_runtime_rule_can_loosen_a_sealed_attribute()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("NationalId", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("NationalId", PolicyFeature.All, PolicyEffect.Allow, PolicyLevel.DynamicUser, priority: 999);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "NationalId", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.IsSealed);
    }

    [Fact]
    public void A_sealed_attribute_on_one_feature_leaves_other_features_open_to_rules()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("Salary", PolicyFeature.Order, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.OverridableAttribute);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Order));
        Assert.True(policy.IsSealed);
    }

    [Fact]
    public void A_policy_decided_only_by_rules_is_not_marked_sealed()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.IsSealed);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyResolutionTests"`
Expected: PASS, 23 tests. These tests document behaviour the level pass already provides. If any
of them fails, the level comparison in `Decide` is wrong — fix `Decide`, not the test.

- [ ] **Step 3: No implementation change**

The guarantee comes from `PolicyLevel.SealedAttribute = 1` being the minimum, and from `Resolve`
setting `isSealed` when the winning fragment sits at that level.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test DynamicWhere.Tests`
Expected: PASS — all pre-existing tests plus the policy tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.Tests/Policies/PolicyResolutionTests.cs
git commit -m "test(policies): pin the sealed-attribute guarantee"
```

---

## Task 12A: Resolver hardening

Added during execution, from the Task 8 quality review. Four findings, ordered so the coverage gap
closes before anything is rewritten.

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.ex/Policies/DTOs/PolicyFragment.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

- [ ] **Item 1 — nested field paths normalize differently on each side. The important one.**

  Both `PolicyFragment`'s constructor and `PolicyResolver.Resolve` apply `String.Trim()`, which
  strips only the ends of the whole string. The canonical normalizer everywhere else in this
  library — `CacheReflection.ValidatePropertyPathInternal`, reached via `Validate<T>()` — splits on
  `.` with `RemoveEmptyEntries | TrimEntries` and rebuilds from CLR property names. Top-level paths
  coincide, dotted paths do not: a fragment stored as `"Customer. Name"` never matches a lookup of
  `"Customer.Name"`, and a lookup of `"Customer..Name"` matches no fragment at all.

  Both directions fail open, and the second is attacker-reachable if enforcement ever resolves
  before validating: the fragment is missed, then the existing pipeline rewrites the path to its
  canonical form and queries the denied field.

  Add one shared normalizer and call it from both sides, using the split-join idiom the repo already
  uses in `CacheReflection.cs`, `Converter.cs:610`, and `Converter.cs:812`:

  ```csharp
  fieldPath == Wildcard
      ? Wildcard
      : string.Join('.', fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
  ```

  Segment recasing needs no handling here — `Matches` is already `OrdinalIgnoreCase`.

  Tests: each row of the divergence table above, in both directions, plus `" * "` still matching
  every path.

- [ ] **Item 2 — `Resolve` validates one argument of three, and the other two fail open.**

  `fieldPath` is checked; `entityType` and `context` pass straight through to
  `provider.GetFragments`. `IDwPolicyProvider` is public, and "return empty on a null argument" is a
  natural defensive style for a third-party implementation — against such a provider, a null
  `entityType` or `context` turns a sealed `Deny` into `Allow` with no exception. Throw
  `ArgumentNullException` for both, with `<exception>` tags.

- [ ] **Item 3 — three mutations survive the suite.**

  The reviewer proved the gap rather than asserting it. Replacing `_providers` with
  `_providers.Take(1)`, deleting the `isSealed` derivation, and deleting the `Sources` recording
  block each leave the whole suite green. Multi-provider merge is the resolver's reason to exist and
  no test passes more than one provider; `IsSealed` is what later phases consult to decide whether a
  runtime rule may override a compile-time one; `Sources` is asserted once, as `Assert.Empty`, which
  passes with recording removed entirely.

  Add three tests: two providers each contributing a different feature and both surviving the merge;
  a `SealedAttribute` fragment yielding `IsSealed == true` where a `DynamicRole` one yields `false`;
  and one fragment covering `Select | Order` yielding exactly one entry in `Sources`.

  **Re-run the same three mutations afterwards and confirm each now fails.** A coverage fix that
  does not kill the mutation it was written for has not fixed anything.

- [ ] **Item 4 — a misbehaving provider produces a bare `NullReferenceException`.**

  A null return from `GetFragments`, a null element in that list, or a null entry in the `providers`
  sequence all surface as an unattributed NRE from inside the resolver. Failing closed is correct;
  do not swallow them. Name the offending provider type in the message.

**Commit:** `fix(policies): normalize nested paths and close resolver coverage gaps`

---

## Task 12B: Resolver allocation

Follows 12A so the rewrite lands on top of the coverage 12A adds. Do not attempt before it.

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`

The Task 8 review measured `Decide` allocating 1784 B per `Resolve` against a necessary 543 B —
44.6 KB versus 13.6 KB for a 25-field query, 42.5 versus 13.0 MB/s of Gen0 at 1000 queries/sec. Those
figures were taken against Task 8's single-`ToList` version. Tasks 9 through 11 have since grown
`Decide` to four comparison passes with four `ToList()` calls and several `Min`/`Max` enumerations,
so the real cost is now higher than measured.

`Decide` runs once per feature, six times per `Resolve`, once per field per query. Rewrite it as a
single pass that tracks the best fragment found so far under the four comparison rules, allocating
nothing. The rules and their order do not change: level, then specificity, then priority, then
strongest effect.

This is not a reflexive objection to LINQ — the repo uses `.Where(...).ToList()` freely in
`CacheEviction`, which runs on eviction rather than per field per query. The specific argument here
is that `FieldPolicy`'s own constructor documentation refuses a defensive copy of `effects` and
`sources` on the grounds that this path is allocation-sensitive, and that copy would have cost
around 64 B. `Decide` spends twenty times that on the same path for no benefit. The stated standard
and the code disagree; make them agree.

The 22 precedence tests from Tasks 8–12 plus 12A's additions are the safety net. Every one must stay
green, unchanged. If a test needs editing to accommodate the rewrite, the rewrite is wrong.

**Commit:** `perf(policies): resolve each feature in a single allocation-free pass`

---

## Task 13: AttributePolicyProvider — direct properties

**Files:**
- Create: `DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs`
- Create: `DynamicWhere.Tests/Policies/SecuredModels.cs`
- Create: `DynamicWhere.Tests/Policies/AttributeProviderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/SecuredModels.cs`:

```csharp
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Types decorated with policy attributes, used by the attribute provider tests. Kept separate
/// from the sales fixture so the existing suite is unaffected by policy metadata.
/// </summary>
[DwEntity(RequirePolicy = true)]
internal class SecuredEmployee
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DwDenied]
    public string NationalId { get; set; } = string.Empty;

    [DwNoSelect(Overridable = true)]
    public decimal Salary { get; set; }

    [DwDeny(PolicyFeature.Order | PolicyFeature.Group)]
    public string InternalNotes { get; set; } = string.Empty;

    public SecuredContact? Contact { get; set; }
}

/// <summary>A nested reference navigation carrying its own policy attributes.</summary>
internal class SecuredContact
{
    [DwNoWhere]
    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}

/// <summary>A type with no policy attributes at all.</summary>
internal class PlainProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
```

Create `DynamicWhere.Tests/Policies/AttributeProviderTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="AttributePolicyProvider"/>: which fragments reflection produces from a
/// decorated type, and how <c>Overridable</c> maps onto a precedence level.
/// </summary>
public class AttributeProviderTests
{
    private static readonly DwPolicyContext Anyone = new();

    private static IReadOnlyList<PolicyFragment> FragmentsFor<T>() =>
        new AttributePolicyProvider().GetFragments(typeof(T), Anyone);

    [Fact]
    public void A_type_with_no_attributes_produces_no_fragments()
    {
        Assert.Empty(FragmentsFor<PlainProduct>());
    }

    [Fact]
    public void A_denied_property_produces_one_fragment_covering_every_feature()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyEffect.Deny, fragment.Effect);
    }

    [Fact]
    public void A_non_overridable_attribute_lands_at_the_sealed_level()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
        Assert.True(fragment.Source.IsSealed);
    }

    [Fact]
    public void An_overridable_attribute_lands_at_the_overridable_level()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "Salary");

        Assert.Equal(PolicyLevel.OverridableAttribute, fragment.Level);
        Assert.False(fragment.Source.IsSealed);
    }

    [Fact]
    public void A_composed_deny_carries_exactly_the_features_it_named()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "InternalNotes");

        Assert.True(fragment.Covers(PolicyFeature.Order));
        Assert.True(fragment.Covers(PolicyFeature.Group));
        Assert.False(fragment.Covers(PolicyFeature.Select));
    }

    [Fact]
    public void The_source_names_the_attribute_that_produced_the_fragment()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal("DwDeniedAttribute", fragment.Source.Origin);
    }

    [Fact]
    public void Undecorated_properties_produce_no_fragments()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredEmployee>();

        Assert.DoesNotContain(fragments, f => f.FieldPath == "Name");
        Assert.DoesNotContain(fragments, f => f.FieldPath == "Id");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~AttributeProviderTests"`
Expected: FAIL to compile — `The name 'AttributePolicyProvider' does not exist in the current context`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs`:

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Produces policy fragments by reflecting over the attributes on a type.
/// </summary>
/// <remarks>
/// The result is identical for every caller, so it is computed once per type and cached. An
/// attribute with <c>Overridable = false</c> lands at <see cref="PolicyLevel.SealedAttribute"/> and
/// nothing at runtime can replace it; one with <c>Overridable = true</c> lands at
/// <see cref="PolicyLevel.OverridableAttribute"/>, the least authoritative level, and acts only as
/// a default.
/// </remarks>
public sealed class AttributePolicyProvider : IDwPolicyProvider
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<PolicyFragment>> Cache = new();

    /// <inheritdoc />
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context)
    {
        if (entityType == null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        return Cache.GetOrAdd(entityType, Build);
    }

    /// <summary>
    /// Reflects over one type's properties, turning every policy attribute into a fragment.
    /// </summary>
    private static IReadOnlyList<PolicyFragment> Build(Type entityType)
    {
        List<PolicyFragment> fragments = new();

        foreach (PropertyInfo property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(property.Name, attribute));
            }
        }

        return fragments;
    }

    /// <summary>
    /// Converts one attribute into a fragment at the level its <c>Overridable</c> flag implies.
    /// </summary>
    private static PolicyFragment ToFragment(string fieldPath, DwDenyAttribute attribute)
    {
        PolicyLevel level = attribute.Overridable
            ? PolicyLevel.OverridableAttribute
            : PolicyLevel.SealedAttribute;

        PolicySource source = PolicySource.FromAttribute(
            attribute.GetType().Name,
            isSealed: !attribute.Overridable);

        return new PolicyFragment(fieldPath, attribute.Features, PolicyEffect.Deny, level, source);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~AttributeProviderTests"`
Expected: PASS, 7 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs DynamicWhere.Tests/Policies/SecuredModels.cs DynamicWhere.Tests/Policies/AttributeProviderTests.cs
git commit -m "feat(policies): add reflection-backed attribute policy provider"
```

---

## Task 14: AttributePolicyProvider — nested paths

Field paths arrive from `Validate<T>()` as dotted strings such as `Contact.Email`. The provider
must produce fragments under those paths, not just top-level property names.

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs`
- Modify: `DynamicWhere.Tests/Policies/AttributeProviderTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AttributeProviderTests.cs`, inside the class:

```csharp
    [Fact]
    public void Attributes_on_a_nested_reference_type_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "Contact.Email");

        Assert.True(fragment.Covers(PolicyFeature.Where));
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Undecorated_nested_properties_produce_no_fragments()
    {
        Assert.DoesNotContain(FragmentsFor<SecuredEmployee>(), f => f.FieldPath == "Contact.Phone");
    }

    [Fact]
    public void A_self_referencing_type_terminates_instead_of_recursing_forever()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredNode>();

        Assert.Contains(fragments, f => f.FieldPath == "Secret");
        Assert.Contains(fragments, f => f.FieldPath == "Next.Secret");
    }

    [Fact]
    public void Nesting_stops_at_the_configured_depth()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredNode>();

        Assert.All(fragments, f => Assert.True(f.FieldPath.Count(c => c == '.') < AttributePolicyProvider.MaxDepth));
    }
```

Add to `SecuredModels.cs`:

```csharp
/// <summary>A self-referencing type, used to prove the nested walk terminates.</summary>
internal class SecuredNode
{
    [DwDenied]
    public string Secret { get; set; } = string.Empty;

    public SecuredNode? Next { get; set; }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~AttributeProviderTests"`
Expected: FAIL to compile — `'AttributePolicyProvider' does not contain a definition for 'MaxDepth'`, and once that is added, `Attributes_on_a_nested_reference_type_produce_dotted_paths` fails with "Sequence contains no matching element"

- [ ] **Step 3: Write minimal implementation**

Replace the `Build` method in `AttributePolicyProvider.cs` and add the helpers:

```csharp
    /// <summary>
    /// How many navigation segments a generated field path may contain. Matches the default
    /// navigation-depth cap, so the provider never produces a path the sanitizer would reject.
    /// </summary>
    public const int MaxDepth = 4;

    /// <summary>
    /// Reflects over one type, turning every policy attribute into a fragment. Reference
    /// navigations and collection element types are walked so that dotted paths such as
    /// <c>Contact.Email</c> carry their own policy.
    /// </summary>
    private static IReadOnlyList<PolicyFragment> Build(Type entityType)
    {
        List<PolicyFragment> fragments = new();

        Walk(entityType, prefix: string.Empty, depth: 0, fragments);

        return fragments;
    }

    /// <summary>
    /// Adds fragments for one type, then descends into its navigations.
    /// </summary>
    /// <param name="type">The type being walked.</param>
    /// <param name="prefix">The dotted path leading to this type, empty at the root.</param>
    /// <param name="depth">How many navigations deep the walk currently is.</param>
    /// <param name="fragments">The accumulator.</param>
    /// <remarks>
    /// Depth alone terminates the walk. A visited-type guard would be cheaper on a graph with many
    /// cycles, but it would also suppress legitimate paths: a self-referencing type would never
    /// yield <c>Next.Secret</c>, even though a caller can filter on exactly that path. Bidirectional
    /// navigations are cyclic by nature, so the depth cap is doing the real work either way, and the
    /// whole walk is computed once per type and cached.
    /// </remarks>
    private static void Walk(Type type, string prefix, int depth, List<PolicyFragment> fragments)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, attribute));
            }

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is not null)
            {
                Walk(navigation, path, depth + 1, fragments);
            }
        }
    }

    /// <summary>
    /// Returns the type to descend into for a property, or null when the property is a scalar.
    /// Collections yield their element type, so <c>Orders.Total</c> resolves the same way a
    /// reference navigation does.
    /// </summary>
    private static Type? NavigationTypeOf(Type propertyType)
    {
        if (propertyType == typeof(string) || propertyType.IsPrimitive || propertyType.IsEnum)
        {
            return null;
        }

        Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying.IsPrimitive || underlying.IsEnum || underlying.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
        {
            Type? element = underlying.IsGenericType
                ? underlying.GetGenericArguments().FirstOrDefault()
                : null;

            return element is not null && element.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
                ? element
                : null;
        }

        return underlying.IsClass ? underlying : null;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~AttributeProviderTests"`
Expected: PASS, 11 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs DynamicWhere.Tests/Policies/SecuredModels.cs DynamicWhere.Tests/Policies/AttributeProviderTests.cs
git commit -m "feat(policies): walk nested navigations when reflecting attributes"
```

---

## Task 15: Options and caps

**Files:**
- Create: `DynamicWhere.ex/Policies/Config/DwCaps.cs`
- Create: `DynamicWhere.ex/Policies/Config/DwPolicyOptions.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyOptionsTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="DwPolicyOptions"/> defaults and the freeze that makes the enforcement posture
/// immutable once the application has started.
/// </summary>
public class PolicyOptionsTests
{
    [Fact]
    public void Defaults_are_the_convenience_tier_with_dry_run_off()
    {
        DwPolicyOptions options = new();

        Assert.Equal(DwTier.Convenience, options.Tier);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Default_caps_are_set_rather_than_unbounded()
    {
        DwCaps caps = new DwPolicyOptions().Caps;

        Assert.Equal(1000, caps.MaxPageSize);
        Assert.Equal(50, caps.MaxConditions);
        Assert.Equal(10, caps.MaxOrderFields);
        Assert.Equal(4, caps.MaxNavigationDepth);
    }

    [Fact]
    public void Freezing_prevents_any_later_change_to_the_posture()
    {
        DwPolicyOptions options = new() { Tier = DwTier.Strict };

        options.Freeze();

        Assert.Throws<InvalidOperationException>(() => options.Tier = DwTier.Convenience);
        Assert.Throws<InvalidOperationException>(() => options.DryRun = true);
        Assert.Equal(DwTier.Strict, options.Tier);
    }

    [Fact]
    public void Caps_are_frozen_along_with_the_options_that_hold_them()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        Assert.Throws<InvalidOperationException>(() => options.Caps.MaxPageSize = 5);
    }

    [Fact]
    public void Freezing_twice_is_harmless()
    {
        DwPolicyOptions options = new();

        options.Freeze();
        options.Freeze();

        Assert.True(options.IsFrozen);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyOptionsTests"`
Expected: FAIL to compile — `The type or namespace name 'Config' does not exist in the namespace 'DynamicWhere.ex.Policies'`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Policies/Config/DwCaps.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// Numeric limits applied to every guarded query.
/// </summary>
/// <remarks>
/// These exist independently of access control. Before this release the library accepted a filter
/// of any size and a navigation path of any depth, so a single request could generate an unbounded
/// join. The defaults below are generous enough not to disturb existing callers while removing the
/// unbounded case.
/// </remarks>
public sealed class DwCaps
{
    private bool _frozen;
    private int _maxPageSize = 1000;
    private int _maxConditions = 50;
    private int _maxOrderFields = 10;
    private int _maxNavigationDepth = 4;

    /// <summary>The largest page a caller may request.</summary>
    public int MaxPageSize
    {
        get => _maxPageSize;
        set => _maxPageSize = Set(value);
    }

    /// <summary>The most conditions one filter may contain, counted across every nested group.</summary>
    public int MaxConditions
    {
        get => _maxConditions;
        set => _maxConditions = Set(value);
    }

    /// <summary>The most fields one query may sort by.</summary>
    public int MaxOrderFields
    {
        get => _maxOrderFields;
        set => _maxOrderFields = Set(value);
    }

    /// <summary>The deepest navigation path a field may traverse.</summary>
    public int MaxNavigationDepth
    {
        get => _maxNavigationDepth;
        set => _maxNavigationDepth = Set(value);
    }

    /// <summary>Prevents any further change.</summary>
    internal void Freeze() => _frozen = true;

    /// <summary>
    /// Guards a setter against post-startup mutation and against a nonsensical limit.
    /// </summary>
    private int Set(int value)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Policy caps cannot be changed after startup.");
        }

        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A cap must be at least 1.");
        }

        return value;
    }
}
```

Create `DynamicWhere.ex/Policies/Config/DwPolicyOptions.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// The enforcement posture, fixed once at startup and read from this single instance at every
/// decision point.
/// </summary>
/// <remarks>
/// The tier is deliberately not a per-call parameter. A posture a developer can forget to pass at
/// one call site is not a posture at all, so it is set once, frozen, and consulted from here.
/// </remarks>
public sealed class DwPolicyOptions
{
    private DwTier _tier = DwTier.Convenience;
    private bool _dryRun;

    /// <summary>The enforcement tier.</summary>
    public DwTier Tier
    {
        get => _tier;
        set
        {
            Guard();
            _tier = value;
        }
    }

    /// <summary>
    /// When true, no decision throws or drops anything anywhere in the application; every decision
    /// is still recorded. Individual callers can opt into dry-run without this by setting it on
    /// their own context, which is how a single canary subject is rolled out.
    /// </summary>
    public bool DryRun
    {
        get => _dryRun;
        set
        {
            Guard();
            _dryRun = value;
        }
    }

    /// <summary>Numeric limits applied to every guarded query.</summary>
    public DwCaps Caps { get; } = new();

    /// <summary>True once <see cref="Freeze"/> has been called.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Prevents any further change. Calling it more than once is harmless.
    /// </summary>
    public void Freeze()
    {
        IsFrozen = true;
        Caps.Freeze();
    }

    /// <summary>
    /// Rejects a change made after startup.
    /// </summary>
    private void Guard()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("Policy options cannot be changed after startup.");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyOptionsTests"`
Expected: PASS, 5 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Config/ DynamicWhere.Tests/Policies/PolicyOptionsTests.cs
git commit -m "feat(policies): add frozen-at-startup options and query caps"
```

---

## Task 16: PolicyException and error codes

**Files:**
- Create: `DynamicWhere.ex/Exceptions/PolicyException.cs`
- Modify: `DynamicWhere.ex/Exceptions/ErrorCode.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyExceptionTests.cs`:

```csharp
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyException"/>, which carries structured data alongside the message so a
/// caller can react to a refusal without parsing text.
/// </summary>
public class PolicyExceptionTests
{
    [Fact]
    public void A_policy_exception_is_catchable_as_a_logic_exception()
    {
        PolicyException exception = new("FieldDeniedForWhere", "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.IsAssignableFrom<LogicException>(exception);
    }

    [Fact]
    public void Structured_data_survives_onto_the_exception()
    {
        PolicyException exception = new("FieldDeniedForWhere", "Contact.Email", PolicyFeature.Where, DwTier.Convenience)
        {
            RuleId = "a3f2",
            SourceOrigin = "DwNoWhereAttribute"
        };

        Assert.Equal("Contact.Email", exception.FieldPath);
        Assert.Equal(PolicyFeature.Where, exception.Feature);
        Assert.Equal(DwTier.Convenience, exception.Tier);
        Assert.Equal("a3f2", exception.RuleId);
        Assert.Equal("DwNoWhereAttribute", exception.SourceOrigin);
    }

    [Fact]
    public void The_message_names_the_code_the_field_and_the_feature()
    {
        PolicyException exception = new("FieldDeniedForWhere", "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.Contains("FieldDeniedForWhere", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Salary", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Where", exception.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyExceptionTests"`
Expected: FAIL to compile — `The name 'PolicyException' does not exist in the current context`

- [ ] **Step 3: Write minimal implementation**

Create `DynamicWhere.ex/Exceptions/PolicyException.cs`:

```csharp
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Exceptions;

/// <summary>
/// Thrown when a policy refuses part of a query.
/// </summary>
/// <remarks>
/// Derives from <see cref="LogicException"/> so existing catch blocks continue to work unchanged.
/// The structured properties let an API layer turn a refusal into a useful response without
/// parsing the message.
/// </remarks>
public class PolicyException : LogicException
{
    /// <summary>
    /// Initializes the exception.
    /// </summary>
    /// <param name="code">The error code, from <c>ErrorCode</c>.</param>
    /// <param name="fieldPath">The field the policy refused.</param>
    /// <param name="feature">The feature that was refused.</param>
    /// <param name="tier">The enforcement tier in force when the refusal happened.</param>
    public PolicyException(string code, string fieldPath, PolicyFeature feature, DwTier tier)
        : base($"{code}: field '{fieldPath}', feature '{feature}', tier '{tier}'.")
    {
        Code = code;
        FieldPath = fieldPath;
        Feature = feature;
        Tier = tier;
    }

    /// <summary>The error code.</summary>
    public string Code { get; }

    /// <summary>The field the policy refused.</summary>
    public string FieldPath { get; }

    /// <summary>The feature that was refused.</summary>
    public PolicyFeature Feature { get; }

    /// <summary>The enforcement tier in force.</summary>
    public DwTier Tier { get; }

    /// <summary>The identifier of the runtime rule that decided, when one did.</summary>
    public string? RuleId { get; init; }

    /// <summary>The attribute or rule description that decided.</summary>
    public string? SourceOrigin { get; init; }
}
```

Add to `DynamicWhere.ex/Exceptions/ErrorCode.cs`, before the closing brace:

```csharp
    /// <summary>
    /// Indicates that a policy refuses filtering on the requested field.
    /// </summary>
    public static string FieldDeniedForWhere => "FieldDeniedForWhere";

    /// <summary>
    /// Indicates that a policy refuses projecting the requested field.
    /// </summary>
    public static string FieldDeniedForSelect => "FieldDeniedForSelect";

    /// <summary>
    /// Indicates that a policy refuses sorting by the requested field.
    /// </summary>
    public static string FieldDeniedForOrder => "FieldDeniedForOrder";

    /// <summary>
    /// Indicates that a policy refuses grouping by the requested field.
    /// </summary>
    public static string FieldDeniedForGroup => "FieldDeniedForGroup";

    /// <summary>
    /// Indicates that a policy refuses aggregating the requested field.
    /// </summary>
    public static string FieldDeniedForAggregate => "FieldDeniedForAggregate";

    /// <summary>
    /// Indicates that a policy refuses the requested field inside a set operation.
    /// </summary>
    public static string FieldDeniedForSegment => "FieldDeniedForSegment";

    /// <summary>
    /// Indicates that every requested select field was refused, leaving no projection.
    /// </summary>
    public static string AllSelectsDenied => "AllSelectsDenied";

    /// <summary>
    /// Indicates that a query exceeded a configured cap.
    /// </summary>
    public static string CapExceeded => "CapExceeded";

    /// <summary>
    /// Indicates that a type requiring a policy context was queried without one.
    /// </summary>
    public static string PolicyRequired => "PolicyContextRequiredForThisEntity";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyExceptionTests"`
Expected: PASS, 3 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Exceptions/ DynamicWhere.Tests/Policies/PolicyExceptionTests.cs
git commit -m "feat(policies): add PolicyException and policy error codes"
```

---

## Task 17: The exhaustive precedence matrix

Tasks 8 through 12 tested precedence rule by rule. This task proves the whole grid at once, so a
combination nobody thought to write by hand cannot slip through.

**Files:**
- Create: `DynamicWhere.Tests/Policies/PolicyPrecedenceMatrixTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DynamicWhere.Tests/Policies/PolicyPrecedenceMatrixTests.cs`:

```csharp
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Exercises the full precedence grid rather than a sample of it. Precedence is where a subtle
/// error stays invisible until it leaks data, so every pair of levels is checked in both
/// directions, and every level is checked against every effect.
/// </summary>
public class PolicyPrecedenceMatrixTests
{
    private static readonly PolicyLevel[] Levels =
    {
        PolicyLevel.SealedAttribute,
        PolicyLevel.DynamicUser,
        PolicyLevel.DynamicRole,
        PolicyLevel.DynamicTenant,
        PolicyLevel.DynamicGlobal,
        PolicyLevel.OverridableAttribute
    };

    private static readonly PolicyEffect[] Effects =
    {
        PolicyEffect.Allow,
        PolicyEffect.Mask,
        PolicyEffect.Deny
    };

    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
        PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
    };

    private static FieldPolicy Resolve(FakePolicyProvider provider) =>
        new PolicyResolver(new[] { provider }).Resolve(typeof(object), "Field", new DwPolicyContext());

    public static IEnumerable<object[]> LevelPairs() =>
        from higher in Levels
        from lower in Levels
        where higher < lower
        select new object[] { higher, lower };

    public static IEnumerable<object[]> LevelsAndEffects() =>
        from level in Levels
        from effect in Effects
        select new object[] { level, effect };

    public static IEnumerable<object[]> EveryFeature() =>
        Features.Select(f => new object[] { f });

    /// <summary>
    /// For every ordered pair of levels, the more authoritative one decides — regardless of which
    /// effect each carries, and regardless of the order they were supplied in.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelPairs))]
    public void The_more_authoritative_level_always_decides(PolicyLevel higher, PolicyLevel lower)
    {
        foreach (PolicyEffect winning in Effects)
        {
            foreach (PolicyEffect losing in Effects)
            {
                FakePolicyProvider provider = new FakePolicyProvider()
                    .Add("Field", PolicyFeature.Select, losing, lower, priority: 999)
                    .Add("Field", PolicyFeature.Select, winning, higher);

                FieldPolicy policy = Resolve(provider);

                Assert.Equal(winning, policy.EffectFor(PolicyFeature.Select));
            }
        }
    }

    /// <summary>
    /// A single fragment at any level, carrying any effect, produces exactly that effect.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void A_lone_fragment_decides_its_feature(PolicyLevel level, PolicyEffect effect)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.Select, effect, level);

        Assert.Equal(effect, Resolve(provider).EffectFor(PolicyFeature.Select));
    }

    /// <summary>
    /// Every feature resolves independently — a decision on one never leaks onto another.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFeature))]
    public void Each_feature_resolves_independently(PolicyFeature feature)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", feature, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolve(provider);

        Assert.False(policy.Allows(feature));

        foreach (PolicyFeature other in Features.Where(f => f != feature))
        {
            Assert.True(policy.Allows(other));
        }
    }

    /// <summary>
    /// Within one level, ties fall to the strictest effect present. This is the multi-role case:
    /// a caller holding both a permissive and a restrictive role gets the restrictive one.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void Ties_within_a_level_fall_to_the_strictest_effect(PolicyLevel level, PolicyEffect effect)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.Select, PolicyEffect.Allow, level)
            .Add("Field", PolicyFeature.Select, effect, level);

        PolicyEffect expected = effect > PolicyEffect.Allow ? effect : PolicyEffect.Allow;

        Assert.Equal(expected, Resolve(provider).EffectFor(PolicyFeature.Select));
    }

    /// <summary>
    /// A sealed attribute is never overridden, by any level, effect, or priority.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void A_sealed_attribute_survives_every_competing_fragment(PolicyLevel level, PolicyEffect effect)
    {
        if (level == PolicyLevel.SealedAttribute)
        {
            return;
        }

        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("Field", PolicyFeature.All, effect, level, priority: int.MaxValue);

        FieldPolicy policy = Resolve(provider);

        Assert.Equal(PolicyEffect.Deny, policy.EffectFor(PolicyFeature.Select));
        Assert.True(policy.IsSealed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyPrecedenceMatrixTests"`
Expected: PASS. The matrix is a regression net over behaviour Tasks 8–12 already built. A failure
here means one of those tasks was implemented incorrectly — fix `PolicyResolver`, never the matrix.

- [ ] **Step 3: No implementation change**

If every assertion passes, the resolver is correct across the grid.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test DynamicWhere.Tests`
Expected: PASS — every pre-existing test plus all policy tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.Tests/Policies/PolicyPrecedenceMatrixTests.cs
git commit -m "test(policies): add exhaustive precedence matrix"
```

---

## Task 18: Phase gate

**Files:**
- Modify: `docs/superpowers/specs/2026-08-22-dynamicwhere-policies-design.md`

- [ ] **Step 1: Correct the spec's PolicyFeature snippet**

In §4.3, replace:

```csharp
[Flags] public enum PolicyFeature
{ None = 0, Where = 1, Select = 2, Order = 4, Group = 8, Aggregate = 16, All = 31 }
```

with:

```csharp
[Flags] public enum PolicyFeature
{ None = 0, Where = 1, Select = 2, Order = 4, Group = 8, Aggregate = 16, Segment = 32, All = 63 }
```

- [ ] **Step 2: Record the resolved open items**

In §10, replace the list with:

```markdown
- `Segment` has its own `PolicyFeature` flag (32); `All` is 63. Resolved in Phase 1.
- `MinGroupSize` is a global option with a per-field attribute override. Implemented in Phase 8.
- Tokenize is deferred to v3.1. Eight mask strategies ship in v3.0.
- The entry method is `ApplyPolicy(ctx)`; the class-level flag is `[DwEntity(RequirePolicy = true)]`.
```

- [ ] **Step 3: Verify the whole suite is green**

Run: `dotnet test`
Expected: PASS, every project

- [ ] **Step 4: Verify no forbidden file was touched**

Run: `git diff --name-only master...HEAD -- DynamicWhere.ex/Source/`
Expected: no output. Any file listed means the sandwich boundary was violated.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-08-22-dynamicwhere-policies-design.md
git commit -m "docs(policies): record Phase 1 decisions in the design spec"
```

---

## Phase 1 exit criteria

- [ ] `PolicyResolver` returns the correct `FieldPolicy` for every combination in the precedence grid
- [ ] `AttributePolicyProvider` produces fragments for direct and nested paths, terminating on cycles
- [ ] `DwPolicyOptions` freezes at startup and rejects later mutation
- [ ] `PolicyException` derives from `LogicException` and carries structured data
- [ ] `dotnet test` passes across every project
- [ ] `git diff --name-only master...HEAD -- DynamicWhere.ex/Source/` is empty
- [ ] Nothing has been pushed to master

**Next:** Phase 2 — Gating. Its plan is written against the code this phase produced.
