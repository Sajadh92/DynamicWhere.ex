# Phase 9 — Release preparation

**Branch:** `feat/policies-v3`
**Roadmap:** [2026-08-22-policies-v3-roadmap.md](2026-08-22-policies-v3-roadmap.md)
**Spec:** [2026-08-22-dynamicwhere-policies-design.md](../specs/2026-08-22-dynamicwhere-policies-design.md)
**Written:** 2026-09-08, against the code as it exists after Phase 8.

Phases 1–8 built the feature. This one stops the surface moving and makes the merge safe. Nothing
new is designed here; everything below is either paperwork the code has outrun, or a gap the earlier
phases deliberately left to the end.

**Merging is the release.** Phase 9 ends *on the branch*. See the roadmap's "Release mechanics".

---

## Reconciliation against the code, before execution

The roadmap's Phase 9 line and the three "What Phase 9 inherits" blocks were written across four
phases. Read against the tree on 2026-09-08, four of them have moved:

1. **"Version bump across the six tracked files"** is now **four csproj files plus five prose
   files**. `check-version.ps1` guards `README.md`, `DynamicWhere.ex/DOC.md`,
   `OfficialWebsite/lib/nav.ts`, `OfficialWebsite/package.json`, and
   `OfficialWebsite/app/docs/installation/page.tsx` — nine files, eleven occurrences.
2. **"Benchmarks within the 5 percent budget"** presumes a benchmark project. **There is none in
   the repo.** Design §8.6 describes one that was never built. Settled below.
3. **Design §8.3 calls the API project "SQL Server".** It is **PostgreSQL** (Npgsql 8.0.11). A
   seventh spec correction.
4. **The EF Core 6.0.22 leg does not exist in `ci.yml` at all.** The roadmap says the leg "covers
   four projects now", which reads as though a three-project leg were already there. Nothing is.

One piece of luck worth recording: **`Employee` is a `DbSet` that none of the nine existing test
controllers query.** They touch `Products` (154), `Orders` (22) and `Customers` (11) only. So
`Employee` takes the full attribute range — including `[DwEntity(RequirePolicy = true)]`, which
throws on an unguarded query — without disturbing a single existing endpoint. §8.3's "most important
test" is written against Employee and Salary, so the spec and the demo database already agree.

---

## Decisions settled before implementation

**1. Benchmarks are built, and CI is not gated on them.**
A `DynamicWhere.Benchmarks` BenchmarkDotNet project measures guarded against unguarded, mutator
cache hit and miss, resolution cost and sanitize cost. It is run once, and the real numbers go into
the roadmap's Phase 9 outcome. It is **not** wired into `ci.yml`: a benchmark gate on a shared
runner fails for noise, and this is the workflow that publishes to NuGet. Design §8.6's "CI fails on
regression beyond threshold" is therefore recorded as **not implemented**, deliberately, rather than
quietly skipped.

**2. The API project gets the whole of §8.3, backed by the real EF Core store.**
`9_PolicyTestController.cs` and `__PolicyAdminController.cs`, `Employee` decorated with the full
attribute range, the AspNetCore admin surface mounted, and `DwPolicyDbContext` on its own migration
against the same PostgreSQL. This is the only place all four packages run together against a real
database, and it is the last opportunity to find that out before the packages are published.

**3. The docs site gets nine pages**, matching the depth the cache feature already sets on the same
site. K-anonymity gets its own page: it is the one control a reader cannot infer from the API, and
folding it into an overview is how it goes unread.

**4. `DwCaps.MinGroupSize` keeps its default of 1 — off.**
Not really a choice: any other default silently changes the result of every guarded grouping query
for anyone upgrading. It is documented loudly instead, on its own page and in the release notes.

**5. The version is 3.0.0.** Major, for the size of the new surface and the three new packages —
not for a break. The existing v2 API is additive-only on this branch: 17,471 lines added, 5 deleted,
and the four protected query files untouched.

---

## Tasks

### 1. Version bump — 2.1.5 → 3.0.0

Four `<Version>` elements (core, Redis, EntityFrameworkCore, AspNetCore) and the eleven prose
occurrences `check-version.ps1` matches. `README.md:239` also carries a "Version 2.1.5 highlights"
heading that the script does not match but which is wrong the moment the package version moves.

**Gate:** `~/.dotnet/tools/pwsh ./build/check-version.ps1` passes.

### 2. Release notes

`PackageReleaseNotes` in all four csproj files. The core package's notes carry the feature summary;
the three satellites point at it and name what they add.

### 3. Seven spec corrections

Recorded against `docs/superpowers/specs/2026-08-22-dynamicwhere-policies-design.md`, each as a
dated footnote rather than a silent edit — the spec is the record of what was decided, and rewriting
it to match the code loses the fact that the code moved.

| § | Correction |
|---|---|
| 3.4 | The claims adapter cannot be `DwPolicyContext.FromClaims(principal)`; C# has no way to add a static to a type in another assembly. It ships as `DwClaimsAdapter.CreateContextAsync` plus an extension method |
| 5.1 | The rule shape is short by four fields |
| 5.2 | The store contract is missing `LoadNarrowAsync` |
| 5.7 | The sealed-field sentence needs a footnote — the endpoint list itself is right |
| 7 | "Five disclosure channels" are seven mitigations |
| 7.2 | The mitigation covers every transform, not masks alone |
| 8.3 | The API project is PostgreSQL, not SQL Server |

### 4. EF Core 6.0.22 CI leg

A second job in `ci.yml` building the four packable projects against the 6.0.22 floor. Design §8.8's
reason stands: the test project runs EF Core 8 on SQLite while the library floor is 6.0.22, and the
expression-tree APIs the compiled mutator and predicate injection depend on differ between 6 and 8.
Without the leg the floor claimed in four csproj files is untested.

### 5. Benchmarks

`DynamicWhere.Benchmarks`, BenchmarkDotNet, net8.0, referencing the core package only. Four
benchmarks from design §6.5: guarded vs unguarded end to end, compiled mutator cache hit, resolution
with the broad zone cached, sanitize of a five-condition filter. Numbers recorded in the roadmap.

### 6. API policy surface — design §8.3

- Decorate `Employee` with the full attribute range, `[DwEntity(RequirePolicy = true)]` included.
- `9_PolicyTestController.cs` — gating, operator restriction, injection, all eight mask strategies,
  the other five transforms, k-anonymity, trace, both tiers, cost, audit, and **the blocking
  `AsNoTracking` test**: mask a query, assert every returned entity is `EntityState.Detached`,
  modify an unrelated entity in the *same* `DbContext`, `SaveChanges()`, re-read `Salary` with raw
  SQL, assert the original value is intact. If `AsNoTracking` ever regresses the production failure
  mode is silent permanent data destruction, and nothing else in the suite catches it.
- `__PolicyAdminController.cs` — the AspNetCore admin surface, named authorization policies rather
  than `AllowAnonymousAccess`, so the demo shows the shape that refuses to mount without one.
- `DwPolicyDbContext` on its own migration; `Program.cs` wiring.

### 7. README policy section

### 8. `DOC.md` policy reference

### 9. Docs site — nine pages plus nav

Overview · Attributes · Precedence · Transforms and masking · Dynamic store · Store providers ·
Admin API · Security and k-anonymity · Configuration.

### 10. Gates

- `~/.dotnet/tools/pwsh ./build/check-version.ps1`
- `dotnet build DynamicWhere.ex.sln -c Release` — zero warnings
- Full suite green, Docker up (colima), including the Redis and PostgreSQL conformance legs
- `OfficialWebsite` builds
- The four protected files untouched:

```bash
git diff --name-only master...HEAD -- DynamicWhere.ex/Source/Builder.cs DynamicWhere.ex/Source/Validator.cs DynamicWhere.ex/Source/Converter.cs DynamicWhere.ex/Source/Normalizer.cs
```

---

## Standing rules that apply here

- **Never edit** `Builder.cs`, `Validator.cs`, `Converter.cs`, `Normalizer.cs`.
- **Do not merge or push.** Phase 9 ends on the branch.
- **A new carrier on a shipped type is a serializer that has not heard about it.** Phase 9 adds no
  carriers, but the API controllers serialize `PolicyTrace` and `PolicyExplanation` through
  System.Text.Json for the first time outside a test — check what comes out.
- **The recurring bug shape is fail-open.** Twenty-six across eight phases, none found by a test
  that already existed. The documentation pass is a reading pass: a doc comment and its code
  disagreeing has been the tell twice.
- Stage with explicit pathspecs; never `git add -A`.
