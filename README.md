<div align="center">

# DynamicWhere.ex

**JSON-driven queries for Entity Framework Core.**

[![NuGet Version](https://img.shields.io/nuget/v/DynamicWhere.ex?style=flat-square&color=8b5cf6&label=NuGet)](https://www.nuget.org/packages/DynamicWhere.ex)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DynamicWhere.ex?style=flat-square&color=22c55e)](https://www.nuget.org/packages/DynamicWhere.ex)
[![License](https://img.shields.io/badge/license-Free%20Forever-8b5cf6?style=flat-square)](#license)
[![.NET](https://img.shields.io/badge/.NET-6%2B-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-doc.dynamicwhere.com-8b5cf6?style=flat-square)](https://doc.dynamicwhere.com)

</div>

A powerful, versatile library for dynamically composing complex **filter, sort, paginate, group, aggregate, and set-operation** (Union / Intersect / Except) expressions in Entity Framework Core applications — all driven by simple JSON objects from any front-end or API consumer.

**Full reference, JSON cookbook, and tuning guide → [doc.dynamicwhere.com](https://doc.dynamicwhere.com)**

---

## Why DynamicWhere.ex?

Stop concatenating LINQ predicates by hand. Your front-end sends one JSON shape; the back-end calls a single extension method. You get back a strongly-typed, paginated result.

- **JSON in → `IQueryable<T>` out.** No string LINQ. No manual expression trees.
- **Three composable shapes** — `Filter`, `Segment`, `Summary` — cover where, set operations, and group-by reporting.
- **Seventeen extension methods** on `IQueryable<T>` and `IEnumerable<T>`.
- **Nested navigation** through references and collections, with auto-wrapped `.Any()` lambdas where needed.
- **Heterogeneous `Condition.Values`** — pass raw numbers, booleans, strings; normalized per `DataType`.
- **Thread-safe reflection cache** with FIFO / LRU / LFU eviction and six tuned presets.
- **Free Forever.** Targets .NET 6, 7, 8, 9.

---

## Install

```bash
dotnet add package DynamicWhere.ex --version 2.1.0
```

Or via Package Manager:

```powershell
Install-Package DynamicWhere.ex -Version 2.1.0
```

Dependencies (restored automatically):

| Package | Version |
|---------|--------:|
| `Microsoft.EntityFrameworkCore` | `6.0.22` |
| `System.Linq.Dynamic.Core` | `1.6.7` |

---

## Quick Start

**Front-end / API body — pure JSON:**

```json
{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "Price",         "dataType": "Number", "operator": "GreaterThan", "values": [50] },
      { "sort": 2, "field": "Category.Name", "dataType": "Text",   "operator": "IEqual",      "values": ["electronics"] }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders":  [{ "sort": 1, "field": "Price", "direction": "Descending" }],
  "page":    { "pageNumber": 1, "pageSize": 10 }
}
```

**Back-end — one method call:**

```csharp
using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;

app.MapPost("/products/search", async (Filter filter, AppDbContext db) =>
{
    FilterResult<Product> result = await db.Products.ToListAsync(filter);
    return Results.Ok(result);
});
```

**Response shape (`FilterResult<Product>`):**

```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "id": 7, "name": "Laptop Pro", "price": 1299.99, "category": { "name": "Electronics" } }
  ],
  "queryString": null
}
```

That's the whole loop. Full walk-through in **[Quick Start](https://doc.dynamicwhere.com/docs/quick-start)**.

---

## What's inside

### Three composable shapes

| Shape | Pipeline | Use when |
|-------|----------|----------|
| **[`Filter`](https://doc.dynamicwhere.com/docs/classes/filter)** | where → order → page → select | Standard list / search / detail endpoints |
| **[`Segment`](https://doc.dynamicwhere.com/docs/classes/segment)** | set1 ∪/∩/∖ set2 ∪/∩/∖ set3 → order → page | UNION / INTERSECT / EXCEPT across multiple condition sets |
| **[`Summary`](https://doc.dynamicwhere.com/docs/classes/summary)** | where → group → having → order → page | Aggregate reporting (`GROUP BY` + `SUM` / `AVG` / `COUNT` …) |

### Seventeen extension methods

Projection, filtering, composition, and materialization on `IQueryable<T>` and `IEnumerable<T>`:

| Group | Methods |
|-------|---------|
| **Projection** | `.Select<T>(fields)` · `.SelectDynamic<T>(fields)` |
| **Filtering** | `.Where<T>(Condition)` · `.Where<T>(ConditionGroup)` |
| **Composition** | `.Order<T>` · `.Page<T>` · `.Group<T>` · `.Filter<T>` · `.FilterDynamic<T>` · `.Summary<T>` |
| **Materialization** | `.ToList<T>(Filter)` · `.ToListAsync<T>(Filter)` · `.ToListDynamic<T>(Filter)` · `.ToListAsyncDynamic<T>(Filter)` · `.ToList<T>(Summary)` · `.ToListAsync<T>(Summary)` · `.ToListAsync<T>(Segment)` |

Full signatures, validations, and return types → **[Extension Methods Reference](https://doc.dynamicwhere.com/docs/extensions)**.

### Operators & data types

Twenty-eight comparison operators across seven data types — case-sensitive **and** case-insensitive variants of every text operation:

- **Equality:** `Equal` · `IEqual` · `NotEqual` · `INotEqual`
- **Substring:** `Contains` · `IContains` · `NotContains` · `INotContains` · `StartsWith` · `IStartsWith` · `EndsWith` · `IEndsWith` (+ `Not*` and `I*` of each)
- **Set:** `In` · `IIn` · `NotIn` · `INotIn`
- **Range:** `GreaterThan` · `GreaterThanOrEqual` · `LessThan` · `LessThanOrEqual` · `Between` · `NotBetween`
- **Null:** `IsNull` · `IsNotNull`

Data types: `Text` · `Guid` · `Number` · `Boolean` · `DateTime` · `Date` · `Enum`. Full matrix → **[DataType reference](https://doc.dynamicwhere.com/docs/enums/data-type)**.

### Aggregations

`Count` · `CountDistinct` · `Sumation` · `Average` · `Minimum` · `Maximum` · `FirstOrDefault` · `LastOrDefault`. With optional `Having` post-filter referencing aggregate aliases.

### Nested navigation

Dotted paths through reference **and** collection properties, with `.Any()` lambdas inserted automatically where the path crosses a collection.

```json
{ "field": "Orders.OrderItems.ProductName", "dataType": "Text",
  "operator": "IContains", "values": ["laptop"] }
```

Becomes:

```
Orders.Any(i1 => i1.OrderItems.Any(i2 =>
    i2.ProductName != null && i2.ProductName.ToLower().Contains("laptop")))
```

---

## Reflection cache

A thread-safe `ConcurrentDictionary`-backed cache across three stores (TypeProperties · PropertyPath · CollectionElementType) eliminates reflection overhead on repeated queries. Three eviction strategies and six tuned presets:

| Preset | MaxSize | Eviction | Use case |
|--------|--------:|:--------:|----------|
| `Default` | 1000 | LRU | General purpose |
| `ForHighMemoryEnvironment()` | 5000 | LRU | Servers with ample RAM |
| `ForLowMemoryEnvironment()`  | 250  | LFU | Constrained environments |
| `ForDevelopment()` | 100 | FIFO | Testing & debugging |
| `ForHighFrequencyAccess()` | 2000 | LFU | Repeated queries on same types |
| `ForTemporalAccess()` | 1500 | LRU | Recent-access-heavy workloads |

```csharp
using DynamicWhere.ex.Optimization.Cache.Source;

CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());
CacheExpose.WarmupCache<Product>("Name", "Category.Name", "Price");
```

Full tuning guide → **[Cache & Optimization](https://doc.dynamicwhere.com/docs/cache)**.

---

## Error handling

Every validation failure throws **`LogicException`** with a structured error code. Catch at your API boundary and surface as a 400:

```csharp
try
{
    var result = await db.Products.ToListAsync(filter);
    return Results.Ok(result);
}
catch (LogicException ex)
{
    return Results.BadRequest(new { code = ex.Message });
}
```

Full code reference → **[Error Codes](https://doc.dynamicwhere.com/docs/errors)**.

---

## Documentation

The complete reference — every enum, class, extension method, validation rule, JSON example, and cache option — lives on the official site:

### **[→ doc.dynamicwhere.com](https://doc.dynamicwhere.com)**

| Section | What's there |
|---------|--------------|
| [Getting Started](https://doc.dynamicwhere.com/docs)              | Introduction, installation, quick start |
| [Enums](https://doc.dynamicwhere.com/docs/enums)                  | Every DataType, Operator, Connector, Direction, Intersection, Aggregator, Cache enum |
| [Classes](https://doc.dynamicwhere.com/docs/classes)              | Condition, ConditionGroup, ConditionSet, OrderBy, GroupBy, AggregateBy, PageBy, Filter, Segment, Summary, Result types |
| [Extension Methods](https://doc.dynamicwhere.com/docs/extensions) | All 17 methods with signatures, validations, examples |
| [Validation Rules](https://doc.dynamicwhere.com/docs/validation)  | What's checked and what throws |
| [JSON Cookbook](https://doc.dynamicwhere.com/docs/examples)       | 13 copy-pasteable end-to-end examples |
| [Cache & Optimization](https://doc.dynamicwhere.com/docs/cache)   | Architecture, stores, options, presets, monitoring |
| [Error Codes](https://doc.dynamicwhere.com/docs/errors)           | Every `LogicException` message |
| [Breaking Changes](https://doc.dynamicwhere.com/docs/breaking-changes) | Known limits and migration notes |

---

## Version 2.1.0 highlights

- **Heterogeneous `Condition.Values`** — `List<object>` with type-safe coercion. Send raw numbers and booleans without quoting. Backward-compatible with `List<string>` callers.
- **Six tuned cache presets** — pick `ForHighMemory`, `ForLowMemory`, `ForDevelopment`, `ForHighFrequencyAccess`, `ForTemporalAccess`, or the default.
- **Official documentation site** launched at `doc.dynamicwhere.com`.

See **[Breaking Changes & Known Limitations](https://doc.dynamicwhere.com/docs/breaking-changes)** for the complete migration / caveat list.

---

## Compatibility

- **.NET:** 6, 7, 8, 9
- **EF Core providers:** SQL Server, PostgreSQL (Npgsql), MySQL (Pomelo), SQLite — anything that supports `ToQueryString()` for the optional `getQueryString: true` flag.
- **Enum storage:** assumed stored as strings. Use `DataType.Number` if your column stores integers.
- **Case-insensitive operators:** emit `.ToLower()` on both sides. Works well on SQL Server's default collation; watch for case-sensitive PostgreSQL `C` locale.

---

## Links

- **Documentation:** [doc.dynamicwhere.com](https://doc.dynamicwhere.com)
- **NuGet:** [nuget.org/packages/DynamicWhere.ex](https://www.nuget.org/packages/DynamicWhere.ex)
- **Source:** [github.com/Sajadh92/DynamicWhere.ex](https://github.com/Sajadh92/DynamicWhere.ex)
- **Issues:** [github.com/Sajadh92/DynamicWhere.ex/issues](https://github.com/Sajadh92/DynamicWhere.ex/issues)

---

## License

**Free Forever** — Copyright © Sajjad H. Al-Khafaji.
