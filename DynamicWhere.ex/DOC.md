# DynamicWhere.ex

**Version:** 3.1.0 &nbsp;|&nbsp; **Target Framework:** .NET 6+ &nbsp;|&nbsp; **License:** MIT (Free Forever)

> A powerful and versatile library for dynamically creating complex filter, sort, paginate, group, aggregate, and set-operation expressions in Entity Framework Core applications — all driven by simple JSON objects from any front-end or API consumer.

> **Reading this with an AI agent?** This manual is written to be read a section at a time, which is
> the wrong shape for an agent. Point it at **https://doc.dynamicwhere.com/llms.txt** instead — the
> same surface, condensed into one plain-text pass, with the exact spellings and a list of the traps
> that produce silently wrong code.

---

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Enums Reference](#enums-reference)
4. [Classes Reference](#classes-reference)
5. [Extension Methods Reference](#extension-methods-reference)
6. [Validation Rules](#validation-rules)
7. [JSON Examples for Every Extension Method](#json-examples-for-every-extension-method)
8. [Field-Level Policies](#field-level-policies)
9. [Reflection Cache & Optimization](#reflection-cache--optimization)
10. [Cache Configuration Presets](#cache-configuration-presets)
11. [Error Codes Reference](#error-codes-reference)
12. [Breaking Changes & Known Limitations](#breaking-changes--known-limitations)

---

## Installation

```bash
dotnet add package DynamicWhere.ex --version 3.1.0
```

**Dependencies:**
| Package | Version |
|---------|---------|
| `Microsoft.EntityFrameworkCore` | 6.0.22 |
| `System.Linq.Dynamic.Core` | 1.6.7 |
| `Microsoft.Extensions.Configuration.Abstractions` | 6.0.0 |
| `Microsoft.Extensions.Configuration.Binder` | 6.0.0 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 6.0.0 |

The library parses every expression it builds with its own `ParsingConfig` — the parser's defaults with `AreContextKeywordsEnabled = false` — and does not read `ParsingConfig.Default`. See breaking point 15.

**Companion packages** (optional, only for [field-level policies](#field-level-policies)):

| Package | What it adds |
|---------|--------------|
| `DynamicWhere.ex.Policies.Redis` | Runtime rules held in Redis |
| `DynamicWhere.ex.Policies.EntityFrameworkCore` | Runtime rules held in any EF Core provider |
| `DynamicWhere.ex.Policies.AspNetCore` | Admin API, claims adapter, audit middleware |

All four ship at the same version and `build/check-version.ps1` refuses to let them drift.

---

## Quick Start

```csharp
using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;

// Build a filter from a front-end POST body
var filter = new Filter
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1,
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.IContains,
                Values = new List<object> { "john" }
            }
        }
    },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Descending }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 10 }
};

// Apply against EF Core DbSet
FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);
```

---

## Enums Reference

### `DataType`

Specifies the logical data type of a condition value. The library uses this to choose the correct comparison expression.

| Value | Description | Supported Operators |
|-------|-------------|---------------------|
| `Text` | String data | All text operators including case-insensitive variants (`I*`), `In`, `IsNull` |
| `Guid` | GUID as string | `Equal`, `NotEqual`, `In`, `NotIn`, `IsNull`, `IsNotNull` |
| `Number` | Numeric value (byte → decimal) | `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull` |
| `Boolean` | `true` / `false` | `Equal`, `NotEqual`, `IsNull`, `IsNotNull` |
| `DateTime` | Full timestamp. Works on `DateTime` and `DateTimeOffset` members, nullable or not | `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `IsNull`, `IsNotNull` |
| `Date` | Calendar day, compared on both sides | Same as `DateTime` (compares the day only) |
| `Enum` | An enum member, named or numbered. The column may store either | `Equal`, `NotEqual`, `In`, `NotIn`, `IsNull`, `IsNotNull`. The string operators (`Contains`, `StartsWith`, `EndsWith` and their negations) pass validation but throw `ParseException` against an enum-typed member — they work only where the mapped property is itself a `string`, which is `Text`'s job |

#### How the two date types compare

Since 3.1.0 the predicate is built from the member's own CLR type, which is what makes `DateTimeOffset` work at all — every comparison on one used to throw, and `Date` on any nullable date member threw with it.

| | What the library does |
|---|---|
| Accepted texts | **ISO 8601** extended calendar dates (`2026-09-01`, optionally `T` or a space and a time, a fraction, `Z` or an offset such as `+03:00`, `+0300` or `+03`; a lowercase `t`/`z`, a comma before the fraction, and fractions beyond seven digits are accepted too) and **year-first** dates (`2026/09/01`, `2026.09.01`), on every deployment. The other ISO 8601 forms — basic (`20260901`), week (`2026-W36-2`), ordinal (`2026-244`) and reduced precision (`2026-09`) — are `InvalidFormat`. A numeric date that leads with a day or a month — `01/09/2026`, `15.09.2026` — is refused with `AmbiguousDateFormat` whatever its numbers, so a client finds out on its first request rather than on the fifth of the month. Anything else, including `12:00` and `Sep 2026`, is `InvalidFormat`. The server's culture and calendar decide nothing |
| Declared formats | A deployment whose clients send a local form declares it once: `DwDates.Configure(o => o.Formats.Add("dd/MM/yyyy"))`, or bound from `DynamicWhere:Dates:Formats` (a list; a single value there refuses to bind). Two formats that read one text differently, or that put the day and month in opposite orders, are refused at configuration, and so is a format that is malformed, cannot read back what it writes (`hh` without `tt`), has no year, or has a day but no month. A format whose own text ISO 8601 or a year-first date already reads is refused there too — `yyyy-MM-dd`, `yyyy/M/d`, `yyyy-MM-dd HH:mm:ss` and `yyyy-MM-dd'T'HH:mm:ss'Z'`, whose quoted `'Z'` is a letter rather than a zone, so it would read `12:00` as a wall time where ISO 8601 reads an instant. Declaring one can only change what such a value means, and on a `DateTime` member, where the ISO reading converts to the host's local time, it did: off UTC the two readings differed and every such value was refused as `AmbiguousDateFormat`, on that host alone. `dd/MM/yyyy`, `dd/MM/yyyy HH:mm`, `yyyy-MM` and `dd MMM yyyy` are accepted |
| `DateOnly` member | Compared as a day under both date data types, against a `DateOnly(y, m, d)` constructor. On Npgsql, `WHERE "Day" = DATE '2026-09-01'` |
| `HAVING` | Names an alias, so the type comes from the aggregate behind it: `Minimum`, `Maximum`, `FirstOrDefault` and `LastOrDefault` carry the member's type, nullable if the member is, and the predicate is built as for that member. On Npgsql, `HAVING max(col) > TIMESTAMPTZ '…'` |
| `DateTimeOffset` member | Compared against a `DateTimeOffset` literal normalised to UTC. A value carrying no zone is read as UTC, so `Date` names the day the caller wrote. A C# `DateTime` whose `Kind` is `Local`, placed in `Values` under `DataType.DateTime`, is written with its offset and so names its own moment — see [Value Coercion](#value-coercion). On Npgsql `Date` becomes `date_trunc('day', col AT TIME ZONE 'UTC')` |
| `DateTime` member | Compared against a `DateTime` literal. A value carrying `Z` or an offset converts to the host's local time first, as it always has — send it in the convention the column stores |
| Nullable member | Guarded with `field != null` and unwrapped under that guard (`field.Value`, `field.Value.Date`). A null row therefore fails `NotEqual` and `NotBetween`, which is deliberate |
| Non-nullable member | On the entity itself, no guard at all: `IsNull` answers `false` and `IsNotNull` answers `true` — on Npgsql, `WHERE FALSE` and no predicate. Reached through a navigation (`Approval.ApprovedAt`), each navigation is guarded instead (`Approval != null && …`), and `IsNull` / `IsNotNull` test the navigation: a provider reads the member of a missing approval as NULL |

---

### `Operator`

The comparison operator applied to the condition.

| Operator | Description | Required Values |
|----------|-------------|:-:|
| `Equal` | Equality (case-sensitive for text) | 1 |
| `IEqual` | Equality (case-insensitive) | 1 |
| `NotEqual` | Inequality (case-sensitive) | 1 |
| `INotEqual` | Inequality (case-insensitive) | 1 |
| `Contains` | Text contains (case-sensitive) | 1 |
| `IContains` | Text contains (case-insensitive) | 1 |
| `NotContains` | Text not contains (case-sensitive) | 1 |
| `INotContains` | Text not contains (case-insensitive) | 1 |
| `StartsWith` | Starts with (case-sensitive) | 1 |
| `IStartsWith` | Starts with (case-insensitive) | 1 |
| `NotStartsWith` | Does not start with (case-sensitive) | 1 |
| `INotStartsWith` | Does not start with (case-insensitive) | 1 |
| `EndsWith` | Ends with (case-sensitive) | 1 |
| `IEndsWith` | Ends with (case-insensitive) | 1 |
| `NotEndsWith` | Does not end with (case-sensitive) | 1 |
| `INotEndsWith` | Does not end with (case-insensitive) | 1 |
| `In` | Value is in set (case-sensitive for text) | 1+ |
| `IIn` | Value is in set (case-insensitive) | 1+ |
| `NotIn` | Value is not in set (case-sensitive) | 1+ |
| `INotIn` | Value is not in set (case-insensitive) | 1+ |
| `GreaterThan` | Greater than | 1 |
| `GreaterThanOrEqual` | Greater than or equal | 1 |
| `LessThan` | Less than | 1 |
| `LessThanOrEqual` | Less than or equal | 1 |
| `Between` | Inclusive range | 2 |
| `NotBetween` | Outside range | 2 |
| `IsNull` | Is NULL | 0 |
| `IsNotNull` | Is NOT NULL | 0 |

---

### `Connector`

Logical connector combining conditions inside a `ConditionGroup`.

| Value | Description |
|-------|-------------|
| `And` | All conditions must be true (`&&`) |
| `Or` | At least one condition must be true (`\|\|`) |

---

### `Direction`

Sorting direction.

| Value | Description |
|-------|-------------|
| `Ascending` | Sort A → Z / 0 → 9 / oldest → newest |
| `Descending` | Sort Z → A / 9 → 0 / newest → oldest |

---

### `Intersection`

Set operation applied between `ConditionSet` results in a `Segment`. The sets are combined into one query: `Union` and `Intersect` combine the sets' conditions, and `Except` matches rows by primary key.

| Value | Description | Generated as |
|-------|-------------|--------------|
| `Union` | Combines both sets, each row once | `OR` of the sets' conditions |
| `Intersect` | Keeps only rows in both sets | `AND` of the sets' conditions |
| `Except` | Removes rows found in the second set | `NOT EXISTS` on the primary key |

A type with no primary key is combined with SQL `UNION` / `INTERSECT` / `EXCEPT` instead, which compare whole rows: identical rows collapse into one, and a column the database cannot compare (PostgreSQL `json`, SQL Server `xml`) fails the query even when it is not selected.

---

### `Aggregator`

Aggregation function applied inside a `GroupBy`.

| Value | Description | Supports Field? | Numeric Only? |
|-------|-------------|:-:|:-:|
| `Count` | Count items | Optional (when no field, counts all items in group) | No |
| `CountDistinct` | Count distinct values | Required | No |
| `Sumation` | Sum of values | Required | **Yes** |
| `Average` | Average of values | Required | **Yes** |
| `Minimum` | Minimum value | Required | No (except `Boolean`) |
| `Maximum` | Maximum value | Required | No (except `Boolean`) |
| `FirstOrDefault` | Smallest value (the field is ordered ascending, not taken in row order) | Required | No |
| `LastOrDefault` | Largest value (the field is ordered descending, not taken in row order) | Required | No |

---

### `CacheEvictionStrategy`

Cache eviction strategy for the internal reflection cache.

| Value | Description |
|-------|-------------|
| `FIFO` | First-In-First-Out. Predictable, minimal overhead. |
| `LRU` | Least Recently Used. Optimizes for temporal locality. **(Default)** |
| `LFU` | Least Frequently Used. Optimizes for access frequency patterns. |

---

### `CacheMemoryType`

Identifies the internal cache store type (used for monitoring/clearing).

| Value | Description |
|-------|-------------|
| `TypeProperties` | Cached property metadata per `Type` |
| `PropertyPath` | Cached validated & normalized property paths |
| `CollectionElementType` | Cached collection element type lookups |

---

## Classes Reference

### Core Classes

#### `Condition`

A single filter predicate.

| Property | Type | Description |
|----------|------|-------------|
| `Sort` | `int` | Evaluation order within a `ConditionGroup` (must be unique among siblings) |
| `Field` | `string?` | Property path on the entity (supports dot notation e.g. `"Order.Customer.Name"`) |
| `DataType` | `DataType` | Logical data type for value parsing |
| `Operator` | `Operator` | Comparison operator |
| `Values` | `List<object>` | Operand values (count depends on operator). Accepts raw JSON types (strings, numbers, booleans) and is normalized per `DataType`. See [Value Coercion](#value-coercion) below. |

##### Value Coercion

`Values` is `List<object>` so the front-end can send heterogeneous JSON shapes without quoting every primitive:

```json
{
  "Field": "Price",
  "DataType": "Number",
  "Operator": "Between",
  "Values": [0, 1.569]            // raw numbers — no quotes needed
}
```

```json
{
  "Field": "IsActive",
  "DataType": "Boolean",
  "Operator": "Equal",
  "Values": [false]               // raw boolean — no quotes needed
}
```

The library normalizes every element before validation/build:

| Incoming runtime type | Normalized form |
|-----------------------|-----------------|
| `string` | as-is |
| `bool` | `"true"` / `"false"` (lowercase) |
| `JsonElement` (System.Text.Json) | unwrapped by `ValueKind` (`String` → text, `Number` → raw JSON token, `True`/`False` → lowercase) |
| `DateTime` / `DateTimeOffset` / `DateOnly` | Year-first text: `2026-09-01T12:30:00`, `2026-09-01T12:30:00+03:00`, `2026-09-01`. Before 3.1.0 a `DateTime` became month-first `09/01/2026 12:30:00`. A `DateTimeOffset` keeps its offset. A `DateTime` whose `Kind` is `Local` — `DateTime.Now`, or a value Newtonsoft.Json read from text carrying an offset — is written with its offset, `2026-09-17T15:00:00+03:00`, when the condition is `DataType.DateTime` on a `DateTimeOffset` or `DateTimeOffset?` member, or on a `HAVING` alias over such a member's aggregate, so it filters on the moment it holds. Every other `DateTime` is written with no zone: under `DataType.Date`, so `DateTime.Today` compares the day it was written for rather than the UTC day its midnight falls on; against a `DateTime` member, which holds wall-clock time, or a `DateOnly` member; and when its `Kind` is `Utc` or `Unspecified`, which a `DateTimeOffset` member reads as UTC |
| numeric / other `IFormattable` | `InvariantCulture` formatting |
| anything else (`JValue`, etc.) | `value.ToString()` |
| `null` | `string.Empty` |

**Backward compatibility:** callers previously sending `["abc"]` (quoted strings) keep working unchanged — strings deserialize into the `List<object>` as string elements. C# callers that previously used `Values = new List<string> {...}` must switch to `new List<object> {...}` (or `.Cast<object>().ToList()`).

---

#### `ConditionGroup`

A logical grouping of conditions and nested sub-groups.

| Property | Type | Description |
|----------|------|-------------|
| `Sort` | `int` | Evaluation order among sibling sub-groups |
| `Connector` | `Connector` | Logical operator joining children (`And` / `Or`) |
| `Conditions` | `List<Condition>` | Flat conditions in this group |
| `SubConditionGroups` | `List<ConditionGroup>` | Nested condition groups (unlimited depth) |

---

#### `ConditionSet`

A condition set used inside a `Segment` for set operations.

| Property | Type | Description |
|----------|------|-------------|
| `Sort` | `int` | Execution order (must be unique). First set's `Intersection` is ignored. |
| `Intersection` | `Intersection?` | Set operation to apply with previous set's result. **Required for index 1+** |
| `ConditionGroup` | `ConditionGroup` | The filter for this set |

---

#### `OrderBy`

A single sort criterion.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Sort` | `int` | – | Priority order (lower = first) |
| `Field` | `string?` | – | Property path to sort by |
| `Direction` | `Direction` | `Ascending` | Sort direction |

---

#### `GroupBy`

Grouping configuration with optional aggregations.

| Property | Type | Description |
|----------|------|-------------|
| `Fields` | `List<string>` | Properties to group by (must be simple types, no collections/complex types) |
| `AggregateBy` | `List<AggregateBy>` | Aggregations to compute per group |

---

#### `AggregateBy`

A single aggregation within a `GroupBy`.

| Property | Type | Description |
|----------|------|-------------|
| `Field` | `string?` | Property to aggregate (optional for `Count`) |
| `Alias` | `string?` | Name of the result column. Must be a plain identifier — a leading letter or underscore, then letters, digits, or underscores (non-Latin letters allowed) — and must not conflict with `GroupBy.Fields` |
| `Aggregator` | `Aggregator` | Aggregation function |

---

#### `PageBy`

Pagination configuration.

| Property | Type | Description |
|----------|------|-------------|
| `PageNumber` | `int` | 1-based page index (must be > 0) |
| `PageSize` | `int` | Items per page (must be > 0) |

---

### Complex Classes

#### `Filter`

Combines filtering, selecting, ordering, and pagination in a single object.

| Property | Type | Description |
|----------|------|-------------|
| `ConditionGroup` | `ConditionGroup?` | Optional where-clause |
| `Selects` | `List<string>?` | Optional field projection (like SQL `SELECT col1, col2`) |
| `Orders` | `List<OrderBy>?` | Optional sort criteria |
| `Page` | `PageBy?` | Optional pagination |

---

#### `Segment`

Combines multiple condition sets with set operations (Union / Intersect / Except), plus ordering and pagination.

| Property | Type | Description |
|----------|------|-------------|
| `ConditionSets` | `List<ConditionSet>` | Ordered condition sets |
| `Selects` | `List<string>?` | Optional field projection |
| `Orders` | `List<OrderBy>?` | Optional sort criteria |
| `Page` | `PageBy?` | Optional pagination |

---

#### `Summary`

Combines filtering → grouping → having → ordering → pagination for aggregate reporting.

| Property | Type | Description |
|----------|------|-------------|
| `ConditionGroup` | `ConditionGroup?` | Optional where-clause (pre-grouping) |
| `GroupBy` | `GroupBy?` | **Required.** Grouping and aggregation config |
| `Having` | `ConditionGroup?` | Optional post-group filter. Each condition's `Field` must reference an `AggregateBy.Alias` |
| `Orders` | `List<OrderBy>?` | Sort on grouped result. Fields must be GroupBy fields or aggregate aliases |
| `Page` | `PageBy?` | Optional pagination on grouped result |

---

### Result Classes

#### `FilterResult<T>`

| Property | Type | Description |
|----------|------|-------------|
| `PageNumber` | `int` | Current page (0 when no pagination) |
| `PageSize` | `int` | Page size (0 when no pagination) |
| `PageCount` | `int` | Total pages. `1` when no page was requested (`0` with no rows) — before 3.1.0 an unpaged filter or summary reported one page per row, and an unpaged segment with condition sets reported none |
| `TotalCount` | `int` | Total matching records |
| `Data` | `List<T>` | The result entities |
| `QueryString` | `string?` | Generated SQL (when `getQueryString: true`) |
| `Policy` | `PolicyTrace?` | What the policy decided for a guarded query. Null when the query was not guarded, and, since 3.1.0, under `DwTier.Strict` unless `DwPolicyOptions.IncludeTraceInResult` is `true` — see [Results and the trace](#results-and-the-trace) |

#### `SegmentResult<T>`

Inherits all properties from `FilterResult<T>`. Returned by segment operations.

#### `SummaryResult`

| Property | Type | Description |
|----------|------|-------------|
| `PageNumber` | `int` | Current page (0 when no pagination) |
| `PageSize` | `int` | Page size (0 when no pagination) |
| `PageCount` | `int` | Total pages. `1` when no page was requested (`0` with no rows) — before 3.1.0 an unpaged filter or summary reported one page per row, and an unpaged segment with condition sets reported none |
| `TotalCount` | `int` | Total grouped records |
| `Data` | `List<dynamic>` | Dynamic objects with group keys + aggregation values |
| `QueryString` | `string?` | Generated SQL (when `getQueryString: true`) |
| `Policy` | `PolicyTrace?` | What the policy decided for a guarded query. Null when the query was not guarded, and, since 3.1.0, under `DwTier.Strict` unless `DwPolicyOptions.IncludeTraceInResult` is `true` — see [Results and the trace](#results-and-the-trace) |

---

## Extension Methods Reference

All extension methods live in `DynamicWhere.ex.Source.Extension` and operate on `IQueryable<T>` (or `IEnumerable<T>` for in-memory variants).

### `.Select<T>(List<string> fields)`

Projects only the specified fields into a new instance of `T`. Supports direct properties, whole navigation objects/collections, and nested navigation paths — including paths that traverse collection properties.

| Parameter | Type | Description |
|-----------|------|-------------|
| `fields` | `List<string>` | Property paths to include |

**Projection rules:**

| Path style | Behaviour | Example input | Effect on result |
|------------|-----------|---------------|------------------|
| Direct scalar | Bound directly | `"Name"` | `Name: "Laptop"` |
| Whole navigation object (non-dotted) | Bound as-is | `"Category"` | `Category: { Id: 5, Name: "Electronics", … }` |
| Whole navigation collection (non-dotted) | Bound as-is | `"Brands"` | `Brands: [{ Id: 1, … }, …]` |
| Dotted through reference navigation | Recursively projected | `"Category.Name"` | `Category: { Id: …, Name: "Electronics" }` |
| Dotted through collection navigation | Per-element `Select().ToList()` | `"Category.Vendors.Id"` | `Category: { Vendors: [{ Id: 1 }, …] }` |
| Multi-level (reference + collection) | Nested recursively | `"Category.Vendors.Product.Name"` | `Category: { Vendors: [{ Product: { Name: "…" } }] }` |

> **Note:** For nested entities (reference or collection), the `Id` property is always automatically included alongside any requested sub-fields.

**Validations:**
- `query` and `fields` cannot be null.
- `fields` must have at least one entry.
- Every field must exist on `T` (case-insensitive, auto-normalized).
- `T` must have a parameterless constructor.

**Returns:** `IQueryable<T>` — a projected query.

---

### `.SelectDynamic<T>(List<string> fields)`

Projects only the specified fields using `System.Linq.Dynamic.Core`'s string-based `Select`, returning a non-generic dynamic `IQueryable`.
Dotted navigation paths are projected as **nested dynamic objects** that mirror the navigation hierarchy, including through collection properties.

| Parameter | Type | Description |
|-----------|------|-------------|
| `fields` | `List<string>` | Property paths to include |

**Projection rules:**

| Path style | Behaviour | Example input | Dynamic result |
|------------|-----------|---------------|----------------|
| Non-dotted scalar | Projected as-is | `"Name"` | `Name: "Laptop"` |
| Non-dotted object | Projected as-is (whole object) | `"Category"` | `Category: { Id: 5, Name: "Electronics" }` |
| Non-dotted collection | Projected as-is (whole collection) | `"Brands"` | `Brands: [{ Id: 1, … }]` |
| Dotted through reference navigation | Nested object per segment | `"Category.Name"` | `Category: { Name: "Electronics" }` |
| Dotted through collection navigation | `Select` lambda per collection segment | `"Category.Vendors.Id"` | `Category: { Vendors: [{ Id: 1 }] }` |
| Multi-level dotted (reference + collection) | Mixed nesting and Select lambdas | `"Category.Vendors.Product.Name"` | `Category: { Vendors: [{ Product: { Name: "…" } }] }` |
| Nested collections (any depth) | Select lambda at each collection level | `"A.ListB.ListC.Name"` | `A: { ListB: [{ ListC: [{ Name: "…" }] }] }` |
| Multi-level dotted (deep reference) | Deeply nested objects | `"Category.SubCategory.Name"` | `Category: { SubCategory: { Name: "Laptops" } }` |

Multiple dotted fields sharing the same root segment are merged into the same nested object:
- `["Category.Name", "Category.Id"]` → `Category: { Name: "...", Id: 5 }`

**Validations:**
- `query` and `fields` cannot be null.
- `fields` must have at least one entry.
- Every field must exist on `T` (case-insensitive, auto-normalized).

**Returns:** `IQueryable` — a dynamic projected query where each element is an anonymous object.

> **Note:** Unlike `Select<T>`, this method does **not** require a parameterless constructor on `T`.
> **Note:** When both a whole-navigation field (e.g., `"Category"`) and sub-field paths sharing the same root segment (e.g., `"Category.Name"`) are requested, the **sub-field projection takes precedence** and the whole-navigation entry is silently dropped.

---

### `.Where<T>(Condition condition)`

Applies a single condition filter.

| Parameter | Type | Description |
|-----------|------|-------------|
| `condition` | `Condition` | The filter condition |

**Validations:** See [Condition Validation Rules](#condition-validation-rules).

**Returns:** `IQueryable<T>` — filtered query.

---

### `.Where<T>(ConditionGroup group)`

Applies a group of conditions joined by `And` / `Or`, with optional nested sub-groups.

| Parameter | Type | Description |
|-----------|------|-------------|
| `group` | `ConditionGroup` | The filter group |

**Validations:**
- All `Condition.Sort` values within the group must be unique.
- All `SubConditionGroups.Sort` values must be unique.
- Each child condition is validated individually.

**Returns:** `IQueryable<T>` — filtered query.

---

### `.Group<T>(GroupBy groupBy)`

Groups the query by the specified fields and applies aggregations.

| Parameter | Type | Description |
|-----------|------|-------------|
| `groupBy` | `GroupBy` | Grouping and aggregation config |

**Validations:** See [GroupBy Validation Rules](#groupby-validation-rules).

**Returns:** `IQueryable` — dynamic query with grouped results.

---

### `.Order<T>(OrderBy order)` / `.Order<T>(List<OrderBy> orders)`

Sorts the query by one or multiple criteria.

| Parameter | Type | Description |
|-----------|------|-------------|
| `order` / `orders` | `OrderBy` / `List<OrderBy>` | Sort criteria |

**Validations:**
- `Field` must be non-empty and valid on `T`.
- `Field` may not end on a collection of entities/complex types (there is no single value to compare).

**Collection paths:** when `Field` crosses a collection navigation, the collection is reduced to one comparable value — the **smallest** element ascending, the **largest** descending. See [Ordering Across Collections](#14-ordering-across-collections).

**Returns:** `IQueryable<T>` — ordered query.

---

### `.Page<T>(PageBy page)`

Paginates the query.

| Parameter | Type | Description |
|-----------|------|-------------|
| `page` | `PageBy` | Page number and size |

**Validations:**
- `PageNumber` must be > 0.
- `PageSize` must be > 0.

**Returns:** `IQueryable<T>` — paged query.

---

### `.Filter<T>(Filter filter)`

Applies a complete `Filter` (where → order → page → select) to a query.
Ordering and pagination are applied on the strongly-typed `IQueryable<T>` **before** the select projection so that original field names remain valid.

**Returns:** `IQueryable<T>` — composed query.

---

### `.FilterDynamic<T>(Filter filter)`

Applies a complete `Filter` (where → order → page → dynamic select) to a query and returns a dynamic `IQueryable`.
Ordering and pagination are applied on the strongly-typed `IQueryable<T>` **before** the dynamic projection so that original field names remain valid.

**Returns:** `IQueryable` — dynamic composed query.

---

### `.ToList<T>(Filter filter, bool getQueryString = false)`

Materializes a `Filter` and returns a `FilterResult<T>` with pagination metadata.

**Returns:** `FilterResult<T>`

---

### `.ToList<T>(IEnumerable<T>, Filter filter, bool getQueryString = false)`

In-memory variant — wraps the collection with `AsQueryable()` then delegates.

**Returns:** `FilterResult<T>`

---

### `.ToListAsync<T>(Filter filter, bool getQueryString = false)`

Async version of `ToList<T>(Filter)`. Uses `CountAsync()` and `ToListAsync()` for EF Core.

**Returns:** `Task<FilterResult<T>>`

---

### `.ToListDynamic<T>(Filter filter, bool getQueryString = false)`

Materializes a `Filter` using `SelectDynamic<T>` and returns a `FilterResult<dynamic>` with pagination metadata.
Where and count run on the typed query; ordering, pagination, and projection all happen before materialisation.

**Returns:** `FilterResult<dynamic>`

---

### `.ToListDynamic<T>(IEnumerable<T>, Filter filter, bool getQueryString = false)`

In-memory variant — wraps the collection with `AsQueryable()` then delegates to the `IQueryable<T>` overload.

**Returns:** `FilterResult<dynamic>`

---

### `.ToListAsyncDynamic<T>(Filter filter, bool getQueryString = false)`

Async version of `ToListDynamic<T>(Filter)`. Uses `CountAsync()` and `ToDynamicListAsync()` for EF Core.

**Returns:** `Task<FilterResult<dynamic>>`

---

### `.Summary<T>(Summary summary)`

Applies where → group → having → order → page to a query.

**Returns:** `IQueryable` — dynamic grouped query.

---

### `.ToList<T>(Summary summary, bool getQueryString = false)`

Materializes a `Summary` and returns a `SummaryResult`.

**Returns:** `SummaryResult`

---

### `.ToList<T>(IEnumerable<T>, Summary summary, bool getQueryString = false)`

In-memory variant for summary operations.

**Returns:** `SummaryResult`

---

### `.ToListAsync<T>(Summary summary, bool getQueryString = false)`

Async version of `ToList<T>(Summary)`.

**Returns:** `Task<SummaryResult>`

---

### `.ToListAsync<T>(Segment segment)`

Async-only segment operation. Combines every `ConditionSet` with set operations (`Union` / `Intersect` / `Except`) into one query, then orders, pages, projects and counts it in the database exactly as `ToListAsync(Filter)` does. Only the requested page is read, and `Orders` apply before `Selects`. `Union` and `Intersect` combine the sets' conditions and `Except` matches rows by primary key, never by object reference, so on a type with a primary key, tracking, `AsNoTracking()` and `Selects` return the same rows. Ordering follows the database: text sorts by its collation.

**Returns:** `Task<SegmentResult<T>>`

---

## Validation Rules

### Condition Validation Rules

| Rule | Error Code |
|------|------------|
| `Field` must be non-empty and exist on `T` | `InvalidField` |
| `Field`'s first segment must not be a word the expression parser keeps — `new`, `iif`, `np`, `isnull`, `is`, `as`, `cast`, `true`, `false`, `null` | `FieldPath[{path}]StartsWithReservedName` — no `ErrorCode` member (3.1.0) |
| `Between` / `NotBetween` require exactly 2 values | `RequiredTwoValue` |
| `In` / `IIn` / `NotIn` / `INotIn` require 1+ values | `RequiredValues` |
| `IsNull` / `IsNotNull` require 0 values | `NotRequiredValues` |
| All other operators require exactly 1 value | `RequiredOneValue({Operator})` |
| A null or blank value is **not** refused as such: it normalizes to `""`, which `Text` and `Enum` accept and every other DataType rejects on parsing | `InvalidFormat` — `ErrorCode.InvalidValue` exists but is never thrown |
| `Guid` values must parse as `Guid` | `InvalidFormat` |
| `Number` values must parse as a numeric type | `InvalidFormat` |
| `Boolean` values must parse as `bool` | `InvalidFormat` |
| `Date` / `DateTime` values must be ISO 8601, year-first, or a declared format | `InvalidFormat`, or `AmbiguousDateFormat` for a day/month-first date |

### ConditionGroup Validation Rules

| Rule | Error Code |
|------|------------|
| `Conditions` Sort values must be unique | `ConditionsUniqueSort` |
| `SubConditionGroups` Sort values must be unique | `SubConditionsGroupsUniqueSort` |

### GroupBy Validation Rules

| Rule | Error Code |
|------|------------|
| Must have at least one field | `GroupByMustHaveFields` |
| Fields must be unique (case-insensitive) | `GroupByFieldsMustBeUnique` |
| Fields cannot be complex/navigation types | `GroupByFieldCannotBeComplexType` |
| Fields cannot be a collection **of collections** — the element type is what is checked, so an ordinary collection of entities reports `GroupByFieldCannotBeComplexType` instead | `GroupByFieldCannotBeCollectionType` |
| Aggregation alias must be a plain identifier (letters, digits, underscores; not starting with a digit) | `InvalidAlias` |
| Aggregation aliases must be unique | `AggregationAliasesMustBeUnique` |
| Aggregation alias cannot match a GroupBy field | `AggregationAliasCannotBeGroupByField({alias})` |
| Aggregation field must be a simple type | `AggregationFieldMustBeSimpleType` |
| Aggregation field cannot be a collection **of collections** — the element type is what is checked, so a collection of entities reports `AggregationFieldMustBeSimpleType` instead, and a collection of simple values such as `List<string>` passes | `AggregationFieldCannotBeCollectionType` |
| `Sumation` / `Average` only work on numeric fields | `UnsupportedAggregatorForType({agg},{type})` |
| `Minimum` / `Maximum` do not work on `Boolean` | `UnsupportedAggregatorForType({agg},{type})` |

### Segment Validation Rules

| Rule | Error Code |
|------|------------|
| `ConditionSets` Sort values must be unique | `SetsUniqueSort` |
| Sets at index 1+ must have `Intersection` specified | `RequiredIntersection` |

### Summary Validation Rules

| Rule | Error Code |
|------|------------|
| `GroupBy` is required (not null) | `ArgumentNullException` |
| Order fields must exist in GroupBy fields or aggregate aliases | `SummaryOrderFieldMustExistInGroupByOrAggregate({field})` |
| Having condition fields must reference aggregate aliases | `HavingFieldMustExistInAggregateByAlias({field})` |

### Page Validation Rules

| Rule | Error Code |
|------|------------|
| `PageNumber` must be > 0 | `InvalidPageNumber` |
| `PageSize` must be > 0 | `InvalidPageSize` |

---

## JSON Examples for Every Extension Method

### 1. `Select<T>` — Field Projection

**Direct scalars:**
```json
{ "fields": ["Id", "Name", "Price"] }
```

**Dotted path through reference navigation:**
```json
{ "fields": ["Id", "Name", "Category.Name"] }
```
`Category` is projected with only the requested `Name` sub-field (`Id` auto-included).

**Dotted path through collection navigation:**
```json
{ "fields": ["Id", "Name", "Category.Vendors.Id"] }
```
`Category.Vendors` is a collection — each `Vendor` element is projected with only its `Id` (`Id` auto-included).

**Whole navigation object (non-dotted):**
```json
{ "fields": ["Id", "Name", "Category"] }
```
The entire `Category` object is bound as-is.

**Whole collection (non-dotted):**
```json
{ "fields": ["Id", "Name", "Brands"] }
```
The entire `Brands` collection is bound as-is.

> **Backend:** `query.Select(fields)`

---

### 2. `Where<T>(Condition)` — Single Condition

**Text — case-insensitive contains:**
```json
{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["phone"]
}
```

**Number — between range:**
```json
{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": ["100", "500"]
}
```

**Date — greater than:**
```json
{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "Date",
  "operator": "GreaterThan",
  "values": ["2024-01-01"]
}
```

**DateTime — exact match:**
```json
{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "DateTime",
  "operator": "Equal",
  "values": ["2024-06-15T14:30:00"]
}
```

**Guid — equality:**
```json
{
  "sort": 1,
  "field": "CustomerId",
  "dataType": "Guid",
  "operator": "Equal",
  "values": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"]
}
```

**Boolean — exact match:**
```json
{
  "sort": 1,
  "field": "IsActive",
  "dataType": "Boolean",
  "operator": "Equal",
  "values": ["true"]
}
```

**Enum — in set:**
```json
{
  "sort": 1,
  "field": "Status",
  "dataType": "Enum",
  "operator": "In",
  "values": ["Active", "Pending"]
}
```

**Null check:**
```json
{
  "sort": 1,
  "field": "DeletedAt",
  "dataType": "DateTime",
  "operator": "IsNull",
  "values": []
}
```

**Text — In (multiple values):**
```json
{
  "sort": 1,
  "field": "Country",
  "dataType": "Text",
  "operator": "IIn",
  "values": ["USA", "Canada", "UK"]
}
```

---

### 3. `Where<T>(ConditionGroup)` — Group of Conditions

**AND group:**
```json
{
  "connector": "And",
  "conditions": [
    {
      "sort": 1,
      "field": "Name",
      "dataType": "Text",
      "operator": "IContains",
      "values": ["john"]
    },
    {
      "sort": 2,
      "field": "Age",
      "dataType": "Number",
      "operator": "GreaterThanOrEqual",
      "values": ["18"]
    }
  ],
  "subConditionGroups": []
}
```

**Nested groups (AND with nested OR):**
```json
{
  "connector": "And",
  "conditions": [
    {
      "sort": 1,
      "field": "IsActive",
      "dataType": "Boolean",
      "operator": "Equal",
      "values": ["true"]
    }
  ],
  "subConditionGroups": [
    {
      "sort": 1,
      "connector": "Or",
      "conditions": [
        {
          "sort": 1,
          "field": "Role",
          "dataType": "Text",
          "operator": "Equal",
          "values": ["Admin"]
        },
        {
          "sort": 2,
          "field": "Role",
          "dataType": "Text",
          "operator": "Equal",
          "values": ["Manager"]
        }
      ],
      "subConditionGroups": []
    }
  ]
}
```

> **Equivalent SQL:** `WHERE IsActive = true AND (Role = 'Admin' OR Role = 'Manager')`

---

### 4. `Order<T>` — Single / Multiple Ordering

**Single order:**
```json
{
  "sort": 1,
  "field": "CreatedAt",
  "direction": "Descending"
}
```

**Multiple orders:**
```json
[
  { "sort": 1, "field": "LastName", "direction": "Ascending" },
  { "sort": 2, "field": "FirstName", "direction": "Ascending" }
]
```

---

### 5. `Page<T>` — Pagination

```json
{
  "pageNumber": 1,
  "pageSize": 25
}
```

---

### 6. `Group<T>` — GroupBy with Aggregations

```json
{
  "fields": ["Category"],
  "aggregateBy": [
    { "field": null, "alias": "TotalCount", "aggregator": "Count" },
    { "field": "Price", "alias": "AvgPrice", "aggregator": "Average" },
    { "field": "Price", "alias": "MaxPrice", "aggregator": "Maximum" }
  ]
}
```

---

### 7. `Filter<T>` / `ToList<T>(Filter)` / `ToListAsync<T>(Filter)` — Full Filter (Typed)

```json
{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "Price",
        "dataType": "Number",
        "operator": "GreaterThan",
        "values": ["50"]
      },
      {
        "sort": 2,
        "field": "Category.Name",
        "dataType": "Text",
        "operator": "IEqual",
        "values": ["electronics"]
      }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders": [
    { "sort": 1, "field": "Price", "direction": "Descending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

**Response shape (`FilterResult<Product>`):**
```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    {
      "id": 7,
      "name": "Laptop Pro",
      "price": 1299.99,
      "isActive": false,
      "createdAt": "0001-01-01T00:00:00",
      "category": { "id": 5, "name": "Electronics" }
    }
  ],
  "queryString": null
}
```

> **Note:** A typed row is a whole `Product`, not a trimmed object. `selects` decides which members are read; the rest are still present, holding their defaults, and a selected reference navigation also carries its `Id`. Use `ToListDynamic` for a payload holding only the selected members.

---

### 8. `Summary<T>` / `ToList<T>(Summary)` / `ToListAsync<T>(Summary)` — Group + Aggregate + Having

```json
{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "IsActive",
        "dataType": "Boolean",
        "operator": "Equal",
        "values": ["true"]
      }
    ],
    "subConditionGroups": []
  },
  "groupBy": {
    "fields": ["Category.Name"],
    "aggregateBy": [
      { "field": null, "alias": "ProductCount", "aggregator": "Count" },
      { "field": "Price", "alias": "AvgPrice", "aggregator": "Average" },
      { "field": "Price", "alias": "TotalRevenue", "aggregator": "Sumation" }
    ]
  },
  "having": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "ProductCount",
        "dataType": "Number",
        "operator": "GreaterThan",
        "values": ["5"]
      }
    ],
    "subConditionGroups": []
  },
  "orders": [
    { "sort": 1, "field": "TotalRevenue", "direction": "Descending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

**Response shape (`SummaryResult`):**
```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 1,
  "totalCount": 3,
  "data": [
    { "CategoryName": "Electronics", "ProductCount": 15, "AvgPrice": 349.99, "TotalRevenue": 5249.85 },
    { "CategoryName": "Clothing", "ProductCount": 12, "AvgPrice": 45.00, "TotalRevenue": 540.00 }
  ],
  "queryString": null
}
```

> **Note:** Dotted GroupBy fields like `Category.Name` become flattened aliases in the result (e.g., `CategoryName`).

---

### 9. `ToListAsync<T>(Segment)` — Set Operations

```json
{
  "conditionSets": [
    {
      "sort": 1,
      "intersection": null,
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          {
            "sort": 1,
            "field": "Category.Name",
            "dataType": "Text",
            "operator": "Equal",
            "values": ["Electronics"]
          }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 2,
      "intersection": "Union",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          {
            "sort": 1,
            "field": "Price",
            "dataType": "Number",
            "operator": "LessThan",
            "values": ["20"]
          }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 3,
      "intersection": "Except",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          {
            "sort": 1,
            "field": "IsActive",
            "dataType": "Boolean",
            "operator": "Equal",
            "values": ["false"]
          }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "selects": ["Id", "Name", "Price"],
  "orders": [
    { "sort": 1, "field": "Name", "direction": "Ascending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 20
  }
}
```

> **Logic:** `(Electronics) UNION (Price < 20) EXCEPT (Inactive)` → order → paginate

**Response shape (`SegmentResult<Product>`):**
```json
{
  "pageNumber": 1,
  "pageSize": 20,
  "pageCount": 2,
  "totalCount": 35,
  "data": [
    { "id": 1, "name": "Adapter Cable", "price": 9.99, "isActive": false, "createdAt": "0001-01-01T00:00:00" }
  ],
  "queryString": null
}
```

> **Note:** As in example 7, a typed row is a whole `Product`. `isActive` reads `false` because it was not selected, not because the row is inactive.

---

### 10. Nested Collection Navigation

---

### 11. `SelectDynamic<T>` — Dynamic Field Projection

**Direct scalars:**
```json
{ "fields": ["Id", "Name", "Price"] }
```

**Response shape:**
```json
{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99 }
```

---

**Dotted path through reference navigation (nested object):**
```json
{
  "fields": ["Id", "Name", "Price", "Category.Name"]
}
```
`Category.Name` produces a nested `Category` object in the result.

**Response shape:**
```json
{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }
```

---

**Dotted path through collection navigation (Select lambda):**
```json
{
  "fields": ["Id", "Name", "Category.Vendors.Id"]
}
```
`Category.Vendors` is a collection — each element is projected via a `Select` lambda so only `Id` is extracted.

**Response shape:**
```json
{ "Id": 7, "Name": "Laptop Pro", "Category": { "Vendors": [{ "Id": 3 }, { "Id": 7 }] } }
```

---

**Multi-level dotted path (reference → collection → reference):**
```json
{
  "fields": ["Id", "Category.Vendors.Product.Name"]
}
```

**Response shape:**
```json
{ "Id": 7, "Category": { "Vendors": [{ "Product": { "Name": "Laptop Pro" } }] } }
```

---

**Multiple dotted fields merged under the same root segment:**
```json
{
  "fields": ["Id", "Category.Name", "Category.Id"]
}
```
`Category.Name` and `Category.Id` are merged into a single nested `Category` object.

**Response shape:**
```json
{ "Id": 7, "Category": { "Name": "Electronics", "Id": 5 } }
```

---

**Whole navigation object (non-dotted):**
```json
{
  "fields": ["Id", "Name", "Category"]
}
```
`Category` has no dot → projected as the whole object.

**Response shape:**
```json
{ "Id": 7, "Name": "Laptop Pro", "Category": { "Id": 5, "Name": "Electronics" } }
```

---

**Whole collection (non-dotted):**
```json
{
  "fields": ["Id", "Name", "OrderItems"]
}
```

**Response shape:**
```json
{ "Id": 7, "Name": "Laptop Pro", "OrderItems": [ { "Id": 1, "Quantity": 2 } ] }
```

---

**Deep nesting through reference navigations:**
```json
{
  "fields": ["Id", "Category.SubCategory.Name"]
}
```

**Response shape:**
```json
{ "Id": 7, "Category": { "SubCategory": { "Name": "Laptops" } } }
```

---

### 12. `FilterDynamic<T>` / `ToListDynamic<T>(Filter)` / `ToListAsyncDynamic<T>(Filter)` — Full Dynamic Filter

Uses the same `Filter` JSON shape as example 7. The difference is the return type: `IQueryable` / `FilterResult<dynamic>` instead of `IQueryable<T>` / `FilterResult<T>`.

```json
{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "Price",
        "dataType": "Number",
        "operator": "GreaterThan",
        "values": ["50"]
      },
      {
        "sort": 2,
        "field": "Category.Name",
        "dataType": "Text",
        "operator": "IEqual",
        "values": ["electronics"]
      }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders": [
    { "sort": 1, "field": "Price", "direction": "Descending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

**Response shape (`FilterResult<dynamic>`):**
```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }
  ],
  "queryString": null
}
```

> **Note:** For `selects`, the same projection rules as `SelectDynamic` apply: non-dotted paths are projected as-is (whole object or collection); dotted paths through reference navigations produce nested dynamic objects (`Category: { Name: "..." }`); dotted paths through collection navigations use `Select` lambdas to project individual element fields (`Category: { Vendors: [{ Id: … }] }`).

---

### 13. Nested Collection Navigation

When a field path traverses a collection property (e.g., `Orders.OrderItems.ProductName`), the library automatically wraps the inner segment in a `.Any()` lambda.

```json
{
  "sort": 1,
  "field": "Orders.OrderItems.ProductName",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["laptop"]
}
```

> **Generated expression:** `Orders.Any(i1 => i1.OrderItems.Any(i2 => i2.ProductName != null && i2.ProductName.ToLower().Contains("laptop")))`

---

### 14. Ordering Across Collections

A sort needs a single comparable value per row, so `.Any()` is not applicable to `OrderBy`. When an order field path crosses a collection property, each collection segment is reduced with an aggregate instead: **`Min` when sorting ascending, `Max` when sorting descending** — that is, rows are ordered by their *best matching* element in the requested direction.

```json
{ "sort": 1, "field": "Tags.Value", "direction": "Ascending" }
```

> **Generated expression:** `Tags.Min(Value) asc` — rows sorted by their alphabetically first tag.

```json
{ "sort": 1, "field": "Tags.Value", "direction": "Descending" }
```

> **Generated expression:** `Tags.Max(Value) desc` — rows sorted by their alphabetically last tag.

This works at any depth, and paths may continue through reference navigations after a collection:

| Field | Direction | Generated expression |
|-------|-----------|----------------------|
| `Category.Name` | Ascending | `Category.Name asc` (no collection — emitted as-is) |
| `OrderItems.UnitPrice` | Ascending | `OrderItems.Select(UnitPrice).DefaultIfEmpty().Min() asc` |
| `OrderItems.Product.Name` | Ascending | `OrderItems.Min(Product.Name) asc` |
| `Orders.OrderItems.Quantity` | Descending | `Orders.Select(OrderItems.Select(Quantity).DefaultIfEmpty().Max()).DefaultIfEmpty().Max() desc` |
| `Tags` (`List<string>`) | Ascending | `Tags.Min() asc` |

**Empty collections.** A row whose collection is empty has no value to sort by:

- **Reference and nullable types** (`string`, `int?`, …) yield `null`, which sorts first ascending and last descending on most providers.
- **Non-nullable value types** (`int`, `decimal`, `DateTime`, …) use `DefaultIfEmpty()` and yield the type default (`0`, `0m`, `DateTime.MinValue`). Without it, `Min`/`Max` over an empty sequence throws `Sequence contains no elements` under LINQ to Objects — the mode used by the `IEnumerable<T>` overloads.

**Not supported.** A path may not *end* on a collection of entities or other complex types — there is nothing comparable to sort by, and a `LogicException` with `OrderField[{field}]CannotEndOnCollectionOfComplexElements` is thrown. Sort by a scalar inside it instead (`Tags` ✗ → `Tags.Value` ✓). Collections of simple values (`List<string>`, `List<int>`, …) are supported.

> **Note:** `Summary.Orders` is unaffected — it sorts grouped results by GroupBy fields and aggregate aliases, and GroupBy fields cannot be collections.

---

## Field-Level Policies

*New in 3.0. Entirely opt-in: a project with no policy attributes and no `DwPolicy.Configure` call behaves exactly as 2.1.5.*

The library accepts a `Filter` from any caller. Policies decide **who is asking and what they may see** — per field, across all six query features.

### The shape

Requests are sanitized before the query is built; results are transformed after they materialize. The four core query files are untouched.

```
caller request → ApplyPolicy(ctx) → sanitize → existing engine → transform → FilterResult.Policy
```

```csharp
// Once, at startup. Refused on a second call: the tier is read by every request thread.
DwPolicy.Configure(new DwPolicyOptions
{
    Tier = DwTier.Convenience,
    HashSalt = secret,
});

// Once per request, never once per query.
var caller = await DwPolicy.PrepareAsync(
    new DwPolicyContext()
        .WithSubject(DwSubjectKind.User, userId)
        .WithSubject(DwSubjectKind.Role, "Support")
        .WithSubject(DwSubjectKind.Tenant, tenantId)
        .WithValue("TenantId", tenantId));

var result = await db.Employees.ApplyPolicy(caller).ToListAsync(filter);
```

A context carries the snapshot it was served, and the staleness ceiling measures how old that snapshot is — which is why it is built once per request rather than reused. Since 3.1.0 an unprepared context is **refused by `ApplyPolicy` itself**, with `PolicyContextNotPrepared`, whether or not a store is configured: before that only a store provider refused one, so an attributes-only deployment accepted the missing call and would have started refusing the day it gained a store. `DwPolicyContext.IsPrepared` reports it. The overload taking explicit options and a resolver does not check — that host composes its own configuration and owns preparation.

### Attribute reference

| Attribute | Applies to | Effect |
|---|---|---|
| `[DwEntity(RequirePolicy = true)]` | class | An unguarded query on the type throws `PolicyRequired` |
| `[DwEntity(DefaultOrder = "CreatedAt desc, Id")]` | class | The order a guarded query takes when its caller sends none. Unguarded calls ignore it. See [Default order](#default-order) |
| `[DwDeny(features)]` | member | Refuse any of `Where`, `Select`, `Order`, `Group`, `Aggregate`, `Segment` |
| `[DwDenied]` | member | Refuse all six |
| `[DwNoWhere]` `[DwNoSelect]` `[DwNoOrder]` `[DwNoGroup]` `[DwNoAggregate]` | member | Refuse one feature each |
| `[DwOperators(Allow =, Deny =)]` | member | Restrict which operators may target the member |
| `[DwAlias("name")]` | member | A public name, accepted anywhere a field path is, renamed back on output |
| `[DwForceWhere(op, Value =, ContextValue =, AllowNull =)]` | member | A predicate ANDed into every guarded query. `AllowNull = true` lets rows whose member is null through as well — see [A forced predicate that lets null through](#a-forced-predicate-that-lets-null-through) |
| `[DwRequireWhere(Operators =)]` | member | The caller must filter on this member |
| `[DwMask(strategy)]` | member | `Full` `Partial` `Email` `Phone` `Regex` `Fixed` `Hash` `Null` `Tokenize` |
| `[DwMutate(typeof(T))]` | member | Hand the value to an `IValueTransformer`; the type is checked at startup |
| `[DwDefault]` / `[DwDefault("v")]` | member | Replace with the type default or a constant |
| `[DwGeneralize(mode)]` | member | `Round` `Bucket` `DatePart` `Truncate` — reduce precision, keep the type |
| `[DwTruncate(n)]` | member | Shorten text |
| `[DwFormat("fmt")]` | member | Render through a .NET format string |
| `[DwDescribe(Label =, Description =, Group =, Order =)]` | member | Feed the schema endpoint |
| `[DwAllowedValues(...)]` | member | Offer a list rather than a free-text box |
| `[DwCost(weight)]` | member | Charge the member against the query budget |
| `[DwAudit(features)]` | member | Record every use to `IDwAuditSink` |

All six transform attributes also carry `AllowAggregate` and `MinGroupSize`. Every attribute except `[DwEntity]` derives from `DwPolicyAttribute` and so carries `Overridable`, which defaults to **false**. It decides nothing on `[DwOperators]`, whose lists are intersected, or on `[DwForceWhere]`, whose predicates are collected — no rule can widen either, with or without the flag.

### Precedence

Six levels, lowest number wins:

| Level | Source |
|---|---|
| 1 | `SealedAttribute` — the default. No runtime rule can lift it |
| 2 | `DynamicUser` |
| 3 | `DynamicRole` |
| 4 | `DynamicTenant` |
| 5 | `DynamicGlobal` |
| 6 | `OverridableAttribute` — an attribute marked `Overridable = true` |

Ties break by level → specificity (an exact field beats a wildcard) → priority → strongest effect (`Deny` > `Mask` > `Allow`).

Two things are deliberately **not** elected: forced predicates are collected and ANDed, because a conjunction can only narrow; operator restrictions intersect. Transform stages are elected per stage, so a rule can add a truncation on top of a sealed mask but cannot replace the mask.

### A forced predicate that lets null through

*New in 3.1.0.* `AllowNull = true` on `[DwForceWhere]` lets a row whose member is null pass as well. It is the shape of a record that belongs to one tenant or to none, such as a system role no institution owns:

```csharp
[DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
public int? InstitutionId { get; set; }
```

The injected term is `(field op value OR field IS NULL)` — here, `InstitutionId` equal to the context's `TenantId`, or null. It sits in a group of its own, joined by `And` to the caller's group and to every other forced predicate, so a caller's `Or` cannot merge with it. No combination of forced predicates could say this before: two on one member are joined by `And`.

- It works with every operator that takes a value. Combined with `Operator.IsNull` or `Operator.IsNotNull`, or placed on a member that can never be null (a non-nullable value type), it is refused with `ArgumentException` at resolution and reported by the startup check ([Checking the model at startup](#checking-the-model-at-startup)).
- The context value is still required: a context that does not supply it is refused with `MissingContextValue`. Which rows pass widens; the caller's own scope does not.
- The widened term is a disjunction, so it does not satisfy a `[DwRequireWhere]` on the same member. The caller must still filter on it.
- The trace records the injection as `forced predicate (Equal, or null)`, naming the operator used. A dry run injects nothing, as for every forced predicate.
- A runtime rule can carry it too: `ForcedPredicate.FromConstant` and `ForcedPredicate.FromContext` gain overloads taking `bool allowNull` (the four-argument ones mean `false`), and a stored rule writes `"allowNull": true` in its `forced` object, only when it is true. A rule is written without the type to hand, so on a member that can never be null it is not refused: it injects the comparison alone, which is the same predicate.
- On a null check it is refused everywhere. `FromConstant` and `FromContext` throw `ArgumentException` for `allowNull: true` with `IsNull` or `IsNotNull` — "AllowNull widens a comparison, and a null check compares against nothing." — and a stored rule whose `forced` object pairs a null check with `"allowNull": true` is refused whether or not it also carries a `value` or `contextValue`. A null check ignores a constant, so a widened `IsNotNull` would inject `(field IS NOT NULL OR field IS NULL)`: a scope that scopes nothing. Without the flag, `FromConstant` still accepts a null check and ignores its value.
- A null check never reads the context. `FromContext` throws `ArgumentException` for `IsNull` or `IsNotNull`, whatever `allowNull` says; without the flag the message is "'IsNull' compares against nothing, so it reads no context value; build it with FromNullCheck." A stored rule whose `forced` object pairs a null check with `contextValue` is refused when it is read, as `[DwForceWhere]` already refused a `ContextValue` on a null check. Fixed in 3.1.0: the factory used to accept it, and the key was still required. A caller without the key was refused with `MissingContextValue`, and a caller with it had the value added to a null check that validation refuses (`ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues`), so every guarded query on the type failed. Build a null check with `FromNullCheck`.

### Default order

*New in 3.1.0.* A type can declare the order a guarded query takes when its caller sends none:

```csharp
[DwEntity(DefaultOrder = "CreatedAt desc, Id")]
public class Ticket { ... }
```

`DefaultOrder` is a comma-separated list. Each entry is a field path — a navigation path included — optionally followed by `asc` or `desc` in any letter case; an entry with neither is ascending, and a blank entry, such as a trailing comma leaves, is ignored. End it with a unique field such as the key, or rows sharing the leading values can still change places between pages.

It applies only under `ApplyPolicy`, when the caller sends no orders (`Orders` null or empty), through:

- `ToList`, `ToListAsync`, `ToListDynamic` and `ToListAsyncDynamic` with a `Filter`;
- `ToListAsync` with a `Segment`;
- the composable `Filter` and `FilterDynamic`;
- the composable `Page`, on a source nothing has ordered or projected.

It is never applied:

- by an unguarded call. The core extension methods on a plain `IQueryable<T>` or `IEnumerable<T>`, and a query taken out through `AsUnguardedQueryable()`, ignore the attribute and behave exactly as in 3.0;
- when the caller sends orders — the default is not appended to them as a tiebreak;
- to an `IQueryable<T>` that is already ordered, whether before it was guarded (`db.Tickets.OrderBy(t => t.Title).ApplyPolicy(caller)`) or by a composed `Order` earlier in the chain — even one whose every order the policy dropped, because the caller still sent orders. An in-memory sequence sorted with LINQ to Objects before `ApplyPolicy` is not seen as ordered, because `AsQueryable()` hides the sort, so the default replaces that order;
- to a projected query. A `Select` anywhere in the chain — the guarded composable `Select`, or a projection made before `ApplyPolicy` — leaves the query in its own order, because a default applied after a projection can name a field the projection left out, which EF Core cannot translate, so `guarded.Select(["Id", "Title"]).Page(page)` on a type ordered by `Priority` pages unordered, as in 3.0.0;
- to a `Summary`, or by the composable `Where`, `Select`, `Order` or `Group`.

Nothing is ordered that the type's own `[DwEntity]` did not declare, and a default is never a reason for the library to refuse a query. An entry naming a field the type does not have, one that is not a field optionally followed by a direction, one the core refuses to order by — a path ending on a collection of entities, such as `Tags` — or one whose name the expression parser keeps for itself, such as `Null`, is skipped. A path through a collection to a value, such as `Tags.Value`, is kept and sorted by its smallest value ascending or its largest descending. A field this caller may not order by is left out, in either tier, and never refused: the caller did not send it, and ordering by it would rank rows by a value they may not see. In a `Segment`, a field this caller may not use in a segment is left out as well, since a segment refuses it in any clause; a filter still orders by it. The trace records a `Dropped` decision for `Order` whose reason starts `left out of the default order`; a dry run keeps the field and still records the decision. A caller whose own orders were all dropped under `Convenience` sent orders, and gets no default in their place. The startup check reports every entry a query would skip or leave out.

A field the default keeps is a use of that field. One audited for `Order`, by `[DwAudit]` or a rule, is recorded as a use, `Effect` `Allow`, each time a guarded query orders by it, as a caller's own order is. A field the default leaves out is not recorded: the query does not order by it, and the caller never named it. A dry run keeps the field, so it records it, with its `Order` effect (`Deny` for a field the caller may not order by) and `DryRun` true. See [Auditing](#auditing).

`[DwEntity]` allows one attribute per type, and .NET attribute inheritance gives a derived type its own when it declares one: the base type's attribute is replaced, not merged. A subclass that declares `[DwEntity(DefaultOrder = "Id")]` loses its base type's `RequirePolicy`, and one that declares `[DwEntity(RequirePolicy = true)]` loses the base type's `DefaultOrder`. Repeat both on the derived type.

### Checking the model at startup

`DwPolicy.ValidateModel(options, types)` inspects the policy attributes on the given types and throws `InvalidOperationException` listing every error; `PolicyModelValidator.Inspect(types, options)` returns the same `PolicyModelReport` — `Errors`, `Warnings`, `IsValid` — without throwing. Called at startup, either one lets a misconfiguration fail the deployment rather than a caller's request. Since 3.1.0 the scan also reports:

- every `[DwForceWhere]` resolution would refuse: `Value` and `ContextValue` both set or both missing on a comparison, either one set on a null check, a member whose type has no `DataType`, and `AllowNull` with `IsNull` / `IsNotNull` or on a member that can never be null. Before, these surfaced on the first query that resolved them;
- every `[DwEntity(DefaultOrder = ...)]` entry a guarded query would skip or leave out. An entry that is not a field optionally followed by `asc` or `desc` is an error; a field whose name starts with one of the words the expression parser keeps is an error, judged before the type is asked whether it has the member, because it may well have it — `"{Type}: DefaultOrder names '{field}', which starts with a name the expression parser keeps for itself, so no query can use it. Rename the member."`; a field the type does not have is a warning; a field no query can order by, such as a collection of entities, is an error; a field the type's own attributes seal against ordering is an error, because every guarded query would leave it out. A field denied for ordering only by attributes marked `Overridable = true` is a warning, because a rule can lift the denial for some callers — `"{Type}: DefaultOrder names '{field}', which its attributes deny for ordering unless a rule allows it, so guarded queries leave it out until one does."` — and so is a field the attributes deny for segments: `"{Type}: DefaultOrder names '{field}', which its attributes deny for segments, so guarded segments leave it out."`

### Blocked-action semantics

| Tier | A denied field |
|---|---|
| `Convenience` (default) | Is **dropped** — removed from the projection, the sort, the grouping |
| `Strict` | **Throws** |

A dropped field leaves nothing behind in the data, so the trace is the only way a caller can tell a policy drop from a null value; a convenience-tier result carries it on `FilterResult<T>.Policy` unless `IncludeTraceInResult` is `false` (see [Results and the trace](#results-and-the-trace)). `Strict` also refuses `getQueryString` and makes a deny-select field automatically deny-where inside a `Segment`.

**Under `Strict` an unknown field and a denied field answer alike.** A strict caller may be probing, and two different answers — a validation error for a name that matches nothing, a refusal naming the path and the attribute that sealed it for a denied field — list the columns that caller may not see, one guess at a time. Since 3.1.0, outside a dry run:

- A name that matches nothing on the type is no longer refused as `LogicException` `ConditionMustHasValidFieldName` while the request is read. It is gated as a field denied for every feature, at the step a denial is raised — after the caps — so it receives the refusal a `[DwDenied]` field receives in that clause: `FieldDeniedForWhere`, `FieldDeniedForSelect`, `FieldDeniedForOrder`, `FieldDeniedForGroup` or `FieldDeniedForAggregate`. A name padded with dots or blank segments, such as `NoSuchColumn....` or `. . . . X`, is normalized the way a real path is, so it is refused as a padded real field is rather than by `MaxNavigationDepth`.
- Inside a segment every field refusal is `FieldDeniedForSegment`, with `Feature` `Segment`, whichever clause refused it — a condition in any set, an order, a select, or the field taking part at all. Answered by clause, a field denied for every clause but not for segments would say `FieldDeniedForOrder` where a name that matches nothing says `FieldDeniedForSegment`. Filters and summaries keep their per-clause codes.
- Every refusal carrying one of those six codes has `FieldPath` `"*"`, a null `RuleId` and a null `SourceOrigin`, whatever the field — a real denied field and an alias included — so its message is the same too.
- A `CapExceeded` refusal names no path either. `MaxNavigationDepth` used to return the canonical spelling of the path the caller wrote, which confirmed that it named something. `SourceOrigin` still names the cap.
- `MissingContextValue` has `FieldPath` `"*"` and a null `SourceOrigin`, so it names neither the scope's column nor the context key it reads, which together describe how rows are partitioned. The trace keeps both, and an audited refusal records the scoped field.
- `MaxQueryCost` is checked after every field has passed its gate. A field weighted by `[DwCost]` that the caller may not use is refused as denied before its weight counts, exactly as a name that matches nothing is, so the budget cannot tell the two apart. An allowed weighted field is still refused with `QueryCostExceeded`.
- The trace keeps the real path and reason, and records an unknown name as `Denied` with the reason `names nothing on {TypeName}`. An audited refusal keeps the real field too (see [Auditing](#auditing)).

The convenience tier is unchanged: an unknown name fails validation with `LogicException` `ConditionMustHasValidFieldName`, a refusal names the field as the caller wrote it, with `RuleId` and `SourceOrigin` where a single source decided, and `MaxQueryCost` is checked before any field is gated. A dry run refuses no field, so an unknown name fails validation there in either tier.

### Results and the trace

Every guarded query records a `PolicyTrace`: its tier, whether it ran dry, and a `PolicyDecision` — `FieldPath`, `Feature`, `Action`, `Reason` — for each thing the policy decided. `PolicyQueryable<T>.LastTrace` holds it for the most recent call on the handle, the composable methods included.

`DwPolicyOptions.IncludeTraceInResult` (`bool?`, default null, new in 3.1.0) decides whether the guarded terminals also return it on `FilterResult<T>.Policy`, `SummaryResult.Policy` and `SegmentResult<T>.Policy`:

| `IncludeTraceInResult` | `Convenience` | `Strict` |
|---|---|---|
| `null` (default), following the tier | Carried | Null |
| `true` | Carried | Carried |
| `false` | Null | Null |

The trace names the fields a policy dropped, the attribute or rule that sealed each one, and every predicate injected on the caller's behalf. That is the detail the strict tier already refuses to return through `getQueryString`, and an API that serializes a result sends it to the caller, so under `Strict` it stays in the process by default. `LastTrace` is recorded whatever the option says, and the audit is unaffected. Before 3.1.0 every guarded result carried the trace (breaking point 17). The option freezes with the posture and binds from the configuration key `IncludeTraceInResult`.

### Auditing

`[DwAudit(features)]` records every use of a field, whatever the policy decided, as a `DwAuditEvent` in the caller's context (`DwPolicyContext.PendingAuditEvents`). A use is a field the request names, or, since 3.1.0, a field of the type's [default order](#default-order) that the query orders by: audited for `Order`, it is recorded each time, as a caller's own order is. A default field left out for this caller is not recorded, because the query does not order by it and the caller never named it. Nothing is stored until the buffer is drained to an `IDwAuditSink`, by `DwPolicy.DrainAuditAsync(context, sink)` or, per request, by the ASP.NET Core middleware `app.UseDwPolicyAudit()`. A buffer already holding `DwCaps.MaxAuditEvents` refuses the next audited use with `CapExceeded`.

A log of uses never shows a caller probing for columns they may not read: every guess is refused, so nothing was used. `DwPolicyOptions.AuditRefusals` (`bool`, default `false`, new in 3.1.0) records the refusals too. When it is on, every `PolicyException` raised by a guarded entry point of `PolicyQueryable<T>` — terminal or composable — and `ApplyPolicy(context)`'s refusal of an unprepared context are written to the same buffer and drain the same way:

| `DwAuditEvent` | On a refusal |
|---|---|
| `EntityType` | The type's full name |
| `FieldPath` | The field the refusal was about, by its canonical path in both tiers: the path an alias stands for, and under `Strict` the real field although the caller's refusal said `"*"`. A name that matches nothing is recorded as the caller sent it. `"*"` for a refusal of the whole request, such as `QueryStringDenied` or `PolicyContextNotPrepared`; `MissingContextValue` names the scoped field. At most 256 characters are kept, followed by `…`. Then every character in Unicode category Control (Cc), Format (Cf), Line Separator (Zl) or Paragraph Separator (Zp) is written as `\u` and four lowercase hex digits — a line feed as `\u000a`, U+2028 as `\u2028`, U+202E as `\u202e` — and a character outside the Basic Multilingual Plane is judged whole, with both halves of its surrogate pair escaped. A name the caller invented therefore cannot forge a second line in a log, or reverse the text after it |
| `Feature` | The refused feature |
| `Effect` | `Deny` |
| `Subjects`, `Purpose`, `Tier` | The caller's subjects and purpose, and the tier in force |
| `DryRun` | `false`: the refusal was enforced. A dry run refuses no field, so it records no field refusal; a refusal it still raises, such as `PolicyContextNotPrepared`, is recorded with `DryRun` `false` |
| `ErrorCode` | The `PolicyErrorCode`. Null on an event recording a use |

Each refusal is written at most once, and is never changed or swallowed. A full buffer records nothing and the original refusal is still thrown. A refusal with no guarded context behind it, such as `PolicyRequired` on an unguarded read of a `RequirePolicy` type, is not recorded.

It is off by default because it changes what reaches a sink: a deployment that registered one for `[DwAudit]` starts receiving events with an `ErrorCode`, and one that registered none is warned by the middleware on every refused request. That warning names both switches — remove `[DwAudit]` from the fields that produced the events, or turn off `DwPolicyOptions.AuditRefusals`. `DwAuditEvent` gains a constructor overload whose last parameter is `PolicyErrorCode? errorCode`; the nine-parameter constructor is unchanged, and `ToString()` includes the code when there is one. The option freezes with the posture and binds from `AuditRefusals`.

### Dynamic rules

An optional store supplies rules at runtime. `InMemoryPolicyStore` ships in the core package; Redis and EF Core are separate packages, and all three pass one shared conformance suite.

```csharp
var store = new EfPolicyStore(() => new DwPolicyDbContext(options));
var provider = await StorePolicyProvider.CreateAsync(store, policyOptions);

DwPolicy.Configure(policyOptions, provider);
```

> **`DwPolicyDbContext` lives in the package, not your assembly.** EF Core looks for migrations where the context is declared, so scaffolding fails until you redirect it:
> ```csharp
> options.UseNpgsql(connection, sql => sql.MigrationsAssembly("YourProject"));
> ```

| `options.StoreFailure` | When the store is unreachable |
|---|---|
| `LastKnownGood` (default) | Serve the last snapshot, bounded by `MaxSnapshotAge` |
| `FailClosed` | Refuse the query with `StoreUnavailable` |
| `StaticOnly` | Fall back to attributes alone |

`MaxSnapshotAge` (15 minutes by default) is the ceiling, and it is checked after the mode: a snapshot older than it refuses the query with `StoreUnavailable` even when nothing has failed. `StaticOnly` is the one escape, and only once the provider is already degraded — it has returned to attributes alone by then and never reaches the check. The age is renewed by a successful `RefreshAsync`, and also by a `RefreshInterval` poll that reads back the version already being served — a poll that confirms the snapshot is current counts as a load, so a healthy store nobody writes to keeps answering. A provider built with `autoRefresh: false` polls for nothing and renews on neither, so such a host must call `RefreshAsync` itself more often than `MaxSnapshotAge`.

A rule can never win anything a sealed attribute decides: resolution ranks `SealedAttribute` above every dynamic level, and that is the guarantee. Writing such a rule is refused earlier as a courtesy — every store's `UpsertAsync` and `POST /rules` call `SealedFields.Refuse` — but only when the store was handed a type resolver, since the check needs a `Type` and a store holds a name. Without one the write is accepted and the rule simply loses at resolution.

### Hiding a value you still want to group by

Two strategies keep a column usable while hiding what is in it. Both map one value to one output,
so a caller can still group by the column, join two results on it and count distinct subjects.

```csharp
[DwMask(MaskStrategy.Hash)]        // needs DwPolicyOptions.HashSalt
[DwMask(MaskStrategy.Tokenize)]    // needs DwPolicyOptions.TokenVault
```

A **hash** is computed from the value. It is HMAC-SHA256 keyed by `HashSalt`, which must be at
least 16 characters — a shorter one is refused where it is written, because a salt that can be
brute-forced offline puts every digest the deployment ever emitted back within reach. A blank salt
means none is configured, and a hashing query is then refused with `MissingHashSalt`.

A **token** is not computed from anything. It is drawn at random the first time a value is seen and
written down in `TokenVault`, so the only way back to the value is to read the vault. That makes the
mapping a store you can lock, move and revoke separately from the data, rather than a secret sitting
in configuration. A tokenizing query with no vault is refused with `MissingTokenVault`.

```csharp
new DwPolicyOptions
{
    HashSalt   = secret,                 // 16 characters or more
    TokenVault = new InMemoryTokenVault() // or RedisTokenVault, or EfTokenVault
}
```

| | `Hash` | `Tokenize` |
|---|---|---|
| Output | 64 hex characters (HMAC-SHA256) | 32 hex characters (16 random bytes) |
| Derived from the value | yes | no |
| Reversed by | holding the salt | reading the vault |
| A weak secret | brute-forced offline | does not exist |
| Survives a restart | always | only with a durable vault |
| Discloses equality | yes | yes |

Three vaults ship, all held to one conformance suite. `InMemoryTokenVault` lives and dies with the
process, which is right for a test and wrong for any column compared across restarts.
`RedisTokenVault` and `EfTokenVault` keep the mapping outside the process, and each caches every
mapping it resolves — a token is written once and never rewritten, so a cached answer cannot go
stale.

Tokens are namespaced by the field's own path, so two columns holding the same value get different
tokens. Name a shared `TokenScope` when you want them to match:

```csharp
[DwMask(MaskStrategy.Tokenize, TokenScope = "person-identifier")]
public string NationalId { get; set; }
```

**What neither closes.** Anyone who can write a chosen value and read the column back masked learns
that value's output and can then recognise it in every other row. That is inherent in preserving
equality and no setting removes it. A field that cannot accept it wants `Fixed`, `Null`, or a denial.

### k-anonymity — the control you would not guess

`SUM`, `MAX` and `MIN` execute **in SQL against the stored value**, before any transform applies. `GROUP BY Department` with `MAX(Salary)` over a department of one returns that person's exact salary.

Two halves, and neither works alone:

1. **Aggregating a transformed field is denied by default.** Opt in with `AllowAggregate = true`.
2. **`MinGroupSize` suppresses any group smaller than *k*.** Groups below the floor are removed from the result, not refused. It applies to `ToList`/`ToListAsync` over a `Summary` and to the composable `Group(GroupBy)` and `Summary(Summary)` alike — the composable pair runs its sanitized summary through the same pipeline, and re-projects afterwards so the floor's own counting column never reaches the caller.

```csharp
[DwGeneralize(GeneralizeMode.Round, Step = 5000, AllowAggregate = true, MinGroupSize = 5)]
public decimal Salary { get; set; }
```

`DwCaps.MinGroupSize` is the global floor and **defaults to 5**. A per-field `MinGroupSize` raises it for that field; the effective floor is the largest in play.

Say `MinGroupSize = 1` to switch it off, and it is off — in production, with nothing refused and nothing warned about. The setting starts *unset* rather than at one precisely so that those two are different sentences: `IsMinGroupSizeSet` tells a deliberate opt-out from a deployment that never heard of the control, and without that distinction any check strict enough to catch the second would trap the first.

```csharp
new DwPolicyOptions()                                  // floor of 5
new DwPolicyOptions { Caps = { MinGroupSize = 1 } }    // no floor, and meant
new DwPolicyOptions { Caps = { MinGroupSize = 10 } }   // stricter
```

### Configuration

| Cap | Default | Meaning |
|---|---|---|
| `MaxPageSize` | 1000 | Largest page a caller may request |
| `DefaultPageSize` | 0 (off) | The page a guarded query is given when it asks for none. `MaxPageSize` only ever read a page the caller sent, so the request with none was the one nothing bounded. Composable `Filter`, `FilterDynamic` and `Summary` return the query already paged; `Where`, `Order`, `Select` and `Group` take no page and are never given one. A `Segment` is paged in the database like a filter, so this bounds what it reads as well as what it returns |
| `MaxConditions` | 50 | Conditions in one filter |
| `MaxConditionDepth` | 10 | How deep condition groups may nest: the top group counts as one and each level of `SubConditionGroups` adds one. `MaxConditions` bounds the count and says nothing about the shape. Measured on the caller's groups, before forced predicates are injected — a summary's `ConditionGroup` and `Having` each, and every set of a segment. New in 3.1.0; see breaking point 16 |
| `MaxConditionSets` | 10 | Condition sets in one segment, empty sets included. Every set adds a condition or a `NOT EXISTS` subquery to the one statement a segment becomes, and a set with no conditions passes every other cap, so this is what bounds that statement. New in 3.1.0; see breaking point 16 |
| `MaxConditionValues` | 1000 | Values in any one condition. An `In` or `NotIn` is one comparison per value, so one condition could build a predicate of any size while spending one condition and one field. The condition carrying the most values is compared: a filter's conditions, a summary's conditions and its `Having`, every set of a segment. New in 3.1.0; see breaking point 19 |
| `MaxAggregates` | 50 | `AggregateBy` entries in one summary, through the summary terminals and the composable `Group` and `Summary`. The count the group-size floor adds for itself is not counted. New in 3.1.0; see breaking point 19 |
| `MaxOrderFields` | 10 | Order fields in one query |
| `MaxNavigationDepth` | 4 | How deep a field path may reach |
| `MaxQueryCost` | 1000 | Budget consumed by `[DwCost]` weights |
| `DefaultFieldCost` | 1 | Charged for an unweighted field, and since 3.1.0 for an aggregate with no field, such as a `Count` |
| `MaxAuditEvents` | 10000 | Audit buffer before draining |
| `SchemaDepth` | 2 | Levels a schema request walks when it names no depth |
| `SchemaCycleLimit` | 2 | Times one type may appear on one path |
| `MaxSchemaFields` | 2000 | Fields one schema response may carry before it truncates |
| `MinGroupSize` | 5 | k-anonymity group floor. Set 1 to switch it off |

An unguarded call is held to none of these caps. The structural caps — `MaxPageSize`, `MaxConditions`, `MaxConditionDepth`, `MaxConditionSets`, `MaxConditionValues`, `MaxAggregates`, `MaxOrderFields` and `MaxNavigationDepth` — refuse in both tiers with `CapExceeded`, and `SourceOrigin` names the cap and what the request had: `"MaxConditionDepth cap (10), request had 11"`. Under `Strict` the refusal's `FieldPath` is `"*"` for all of them. All but `MaxNavigationDepth` only count, and are checked before any field name is resolved, so an oversized request is refused before its names are looked at, a name that does not exist included; `MaxNavigationDepth` needs a resolved path and runs after them.

Options are frozen at startup. Every cap refuses a value below one, except two that accept zero: `DefaultFieldCost`, which is the posture for a model weighing only its few expensive fields and leaving the rest free, and `DefaultPageSize`, where zero means no page is supplied. `DefaultPageSize` is the only one that refuses nothing — it fills a page in rather than rejecting a request that carried none, and is bounded by `MaxPageSize`.

### Administration

`app.MapDwPolicyAdmin(...)` mounts seven endpoints under `/dw-policies`, which is `DwPolicyAdminOptions.RoutePrefix`'s default and not a fixed path — set `o.RoutePrefix` to mount them anywhere. It **refuses to map without both `ReadPolicy` and `WritePolicy` named** — there is no default, and it fails at startup rather than on the first request.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/schema` | Fields for a filter UI. Sealed fields are absent |
| `GET` | `/rules?subject=` | List rules. The filter is `Kind[:Key]` — `Role:auditor`, not a bare key. Omit it for every enabled broad rule; a user's rules live in the narrow zone and need `User:{key}` |
| `POST` | `/rules` | Upsert. Sealed fields are rejected |
| `DELETE` | `/rules/{id}` | Delete |
| `POST` | `/explain` | The decision chain: what won, what it overrode, what tied with it |
| `POST` | `/simulate` | The sanitized clause, without executing or auditing |
| `GET` | `/health` | Snapshot version, age, degraded state, last error |

### Schema discovery

`POST /dw-policies/schema` describes what one caller may do with one entity. It is a POST rather
than a GET because the request carries a **list** of paths, and a list in a query string needs a
separator — a comma is legal in a `[DwAlias]`, so the separator would eventually split a name in
half and resolve neither piece.

```jsonc
{ "entity": "employee" }                              // 59 fields, two levels
{ "entity": "employee", "depth": 1 }                  // 13 fields, the entity alone
{ "entity": "employee", "depth": 99 }                 // 99 fields, as deep as a query may reach
{ "entity": "employee", "paths": ["Manager"] }        // 59 fields, rooted at the manager
{ "entity": "employee", "paths": ["Manager", "Address"], "depth": 1 }   // 20 fields
```

`depth` is an integer and nothing else. A value beyond the query cap is clamped rather than refused,
and the response reports both the depth it used and the ceiling — so a caller wanting everything
sends a large number and learns the limit from the reply. There is no sentinel to parse.

The response is **flat, with a parent on every entry**, which is a tree in adjacency form:

```jsonc
{
  "entity": "employee",
  "roots": ["Manager"], "depth": 2, "maxDepth": 4, "truncated": false,
  "fields": [ { "path": "Manager.FirstName", "parent": "Manager", "dataType": "Text", ... } ],
  "nodes":  [ { "path": "Manager.Address", "parent": "Manager", "entity": "address",
                "depth": 3, "expanded": false, "remainingDepth": 0 } ]
}
```

`nodes` carries every navigation the walk touched, expanded or not, so a tree UI hangs each node and
each field under its parent in one pass with no path parsing. `remainingDepth` says what asking for
that path would return, so a node reporting zero has nothing to open.

Five rules bound the walk.

1. **A returned path never exceeds the query cap**, so the schema cannot advertise a path the query
   would refuse.
2. **A type may appear at most twice on one path** (`SchemaCycleLimit`). That is what holds a
   full-depth request to 99 fields rather than 335 on a self-referencing entity. The 236 paths it
   removes are repeats like `Manager.Manager.Email`, which stay queryable and stay reachable by
   asking for the subtree.
3. **The guard counts within the view you asked for.** Drilling into the manager describes it as
   though it were the entity, which is what keeps drilling productive — counting from the entity
   would make a request for `Manager.Manager` describe nothing at all.
4. **Fields are deduplicated by path**, so two overlapping roots list a field once.
5. **`MaxSchemaFields` bounds any single response**, setting `truncated` rather than throwing.

A path naming a simple field is refused; a path naming nothing is a 404. `POST /explain` takes the
same two parameters, because it walks the same field list.

### Configuration from a file

Every value on the posture binds from `IConfiguration`. The entity catalogue, the token vault and
the service provider cannot: they are objects rather than values, and stay in code.

```csharp
builder.Services.AddDwPolicies(
    builder.Configuration.GetSection("DynamicWhere:Policies"),
    options =>
    {
        options.Entities.Expose<Employee>("Employee");
        options.TokenVault = new RedisTokenVault(redis);
    });
```

```jsonc
{
  "DynamicWhere": {
    "Policies": {
      "Tier": "Strict",
      "StoreFailure": "LastKnownGood",
      "MaxSnapshotAge": "00:15:00",
      "Caps": { "MinGroupSize": 5, "SchemaDepth": 2, "MaxSchemaFields": 2000 }
    }
  }
}
```

Configuration binds first and the callback runs second, so a line somebody wrote deliberately is
never overwritten by a file. **A key nothing answers to refuses to start**: the binder's own default
is to ignore an unmatched key, which would let `MinGropSize` sit in a file doing nothing while the
deployment believed it had set a floor.

Every setter's own validation still applies. A cap below one, a snapshot age that is not a positive
interval and a salt shorter than sixteen characters are all refused exactly as they are in code, and
the group floor's opt-out survives unchanged — saying nothing leaves it unset, writing `1` records a
deliberate choice.

`HashSalt` binds like anything else, and configuration is the right channel for it through user
secrets, an environment variable or a vault. A salt committed to `appsettings.json` is not a salt,
and nothing here can tell the difference.

### Performance

There are two budgets, because there are two costs. Gating is paid **once per query**;
transformation is paid **per row per transformed field**, so no single percentage describes it — the
same guard is 1.16× over a hundred rows and 1.62× over ten thousand, on identical code.

Measured with BenchmarkDotNet over 10,000 in-memory rows:

| 10,000 rows | Time | Allocated |
|---|---|---|
| Unguarded | 685 µs | 210 KB |
| Guarded, nothing denied or transformed | 683 µs (**1.00×**) | 220 KB (**1.05×**) |
| Guarded, one field deny-select | 785 µs (1.15×) | 409 KB (1.95×) |
| Guarded, two fields transformed on every row | 1,111 µs (1.62×) | 1,488 KB (7.1×) |

**Gating costs nothing measurable.** Resolving every field, sanitizing the filter and injecting
forced predicates lands inside the noise of the unguarded query.

**Deny-select costs 1.15×** because denying a field means the query projects instead of returning
entities. That belongs to the feature rather than to the guard.

**Transformation costs about 21 ns and 65 bytes per value**, against a design budget of 100 ns. It
has to build a new value for each one, because the change happens after materialization rather than
in SQL.

A cached field resolve is 232–234 ns against a 1 µs target, and sanitizing a five-condition filter
is 2.8 µs against 50 µs.

There is no database round trip in those numbers, so the policy layer's share looks as large as it
ever can — against a real query the I/O dominates.

Run them yourself:

```
dotnet run -c Release --project DynamicWhere.Benchmarks -- --filter "*PolicyBenchmarks*" --job medium
```

### Error codes

`PolicyException.ErrorCode`, values 1–22: `FieldDeniedForWhere` `FieldDeniedForSelect` `FieldDeniedForOrder` `FieldDeniedForGroup` `FieldDeniedForAggregate` `FieldDeniedForSegment` `AllSelectsDenied` `OperatorNotAllowed` `CapExceeded` `PolicyRequired` `RequiredFilterMissing` `MissingContextValue` `AmbiguousFieldName` `QueryStringDenied` `AmbiguousGroupKey` `TransformRequiresMaterialization` `StoreUnavailable` `PolicyContextNotPrepared` `QueryCostExceeded` `GroupTooSmall` `MissingHashSalt` `MissingTokenVault`.

Under `Strict` the six `FieldDeniedFor…` refusals carry `FieldPath` `"*"` and no `RuleId` or `SourceOrigin`, outside a dry run a name that matches nothing receives them too, inside a segment every one of them is `FieldDeniedForSegment`, and `MissingContextValue` carries `FieldPath` `"*"` and no `SourceOrigin` — see [Blocked-action semantics](#blocked-action-semantics).

---

## Reflection Cache & Optimization

DynamicWhere.ex caches all reflection lookups (property metadata, property paths, collection type analysis) to avoid repeated reflection overhead. The cache system is **thread-safe** and provides three configurable eviction strategies.

### Architecture

| Component | Responsibility |
|-----------|---------------|
| `CacheReflection` | Core reflection operations with caching |
| `CacheDatabase` | Thread-safe `ConcurrentDictionary` stores & access tracking |
| `CacheEviction` | FIFO / LRU / LFU eviction algorithms |
| `CacheReporting` | Statistics, memory usage, performance reports |
| `CacheCalculator` | Actual memory measurement |
| `CacheExpose` | **Public API** — the only class consumers interact with |

### Three Cache Stores

| Store | Key | Value | Purpose |
|-------|-----|-------|---------|
| **TypeProperties** | `Type` | `Dictionary<string, PropertyInfo>` | All public instance properties per type |
| **PropertyPath** | `(Type, string)` | `string` | Validated & normalized property paths |
| **CollectionElementType** | `Type` | `Type?` | Element type for collection types |

### `CacheOptions` Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxCacheSize` | `int` | `1000` | Max entries per cache store |
| `LeastUsedThreshold` | `int` | `25` | % of entries to remove on eviction |
| `MostUsedThreshold` | `int` | `75` | % of entries to keep (= 100 − LeastUsedThreshold) |
| `EvictionStrategy` | `CacheEvictionStrategy` | `LRU` | Algorithm: `FIFO`, `LRU`, or `LFU` |
| `EnableLruTracking` | `bool` | `true` | Auto-managed based on strategy |
| `EnableLfuTracking` | `bool` | `false` | Auto-managed based on strategy |
| `AutoValidateConfiguration` | `bool` | `true` | Auto-correct mismatched settings |

### Configuring the Cache

```csharp
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;

// Option 1: Use a preset
CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());

// Option 2: Builder pattern
CacheExpose.Configure(options =>
{
    options.MaxCacheSize = 2000;
    options.LeastUsedThreshold = 20;
    options.EvictionStrategy = CacheEvictionStrategy.LFU;
});

// Option 3: Direct object
CacheExpose.Configure(new CacheOptions
{
    MaxCacheSize = 3000,
    LeastUsedThreshold = 15,
    MostUsedThreshold = 85,
    EvictionStrategy = CacheEvictionStrategy.LRU
});
```

### Cache Warmup

Pre-populate caches at application startup to avoid first-request latency:

```csharp
// Generic warmup
CacheExpose.WarmupCache<Product>("Name", "Category.Name", "Price");
CacheExpose.WarmupCache<Order>("Customer.Name", "OrderItems.Product.Name");

// Non-generic warmup
CacheExpose.WarmupCache(typeof(Customer), "Name", "Email", "Address.City");
```

### Monitoring & Diagnostics

```csharp
// Get structured statistics
CacheStatistics stats = CacheExpose.GetCacheStatistics();
CacheConfiguration config = CacheExpose.GetCacheConfiguration();
CacheMemoryUsage memory = CacheExpose.GetMemoryUsage();

// Generate reports
string perfReport = CacheExpose.GeneratePerformanceReport();
string compactReport = CacheExpose.GenerateCompactStatusReport();
string analysisReport = CacheExpose.GenerateCacheAnalysisReport();
string healthSummary = CacheExpose.GetQuickHealthSummary();

// Monitoring data for dashboards
Dictionary<string, object> monitoringData = CacheExpose.GenerateMonitoringReport();

// Health alerts
var alerts = CacheExpose.GenerateHealthAlerts(new HealthAlertsInput { ... });

// Cache management
CacheExpose.ClearAllCaches();
CacheExpose.ClearCache(CacheMemoryType.PropertyPath);
CacheExpose.ForceEvictionOnAllCaches();
bool isFull = CacheExpose.IsCacheFull(CacheMemoryType.TypeProperties);
```

---

## Cache Configuration Presets

| Preset | MaxCacheSize | Eviction | LeastUsed% | Use Case |
|--------|:-----------:|:--------:|:---------:|----------|
| **Default** | 1000 | LRU | 25% | General purpose |
| `ForHighMemoryEnvironment()` | 5000 | LRU | 10% | Servers with ample RAM |
| `ForLowMemoryEnvironment()` | 250 | LFU | 40% | Constrained environments |
| `ForDevelopment()` | 100 | FIFO | 50% | Testing & debugging |
| `ForHighFrequencyAccess()` | 2000 | LFU | 20% | Repeated queries on same types |
| `ForTemporalAccess()` | 1500 | LRU | 25% | Recent-access-heavy workloads |

---

## Error Codes Reference

All validation errors throw `LogicException` (inherits `Exception`) with one of the following messages. Every message is a fixed code but one, the sentence in the last row:

| Error Code | Message | When |
|------------|---------|------|
| `SetsUniqueSort` | `ListOfConditionsSetsMustHasUniqueSortValue` | Duplicate Sort in ConditionSets |
| `ConditionsUniqueSort` | `AnyListOfConditionsMustHasUniqueSortValue` | Duplicate Sort in Conditions |
| `SubConditionsGroupsUniqueSort` | `AnyListOfSubConditionsGroupsMustHasUniqueSortValue` | Duplicate Sort in SubConditionGroups |
| `RequiredIntersection` | `ConditionsSetOfIndex[1-N]MustHasIntersection` | Missing Intersection on set index 1+ |
| `InvalidField` | `ConditionMustHasValidFieldName` | Empty or invalid field name. Under `ApplyPolicy` in the strict tier, outside a dry run, a name that matches nothing is refused as a `PolicyException` instead, like a denied field — see [Blocked-action semantics](#blocked-action-semantics) |
| `StartsWithReservedName(path)` | `FieldPath[{path}]StartsWithReservedName` | A field path whose first segment is one of the expression parser's own words — `new`, `iif`, `np`, `isnull`, `is`, `as`, `cast`, `true`, `false`, `null`, whatever the letter case. Raised for every clause that takes a path, and for a `[DwAlias]` target. `LogicException.Subject` carries that first segment, trimmed. A `DefaultOrder` entry naming one is skipped like an unreadable entry, and reported by the startup scan. 3.1.0 |
| `InvalidValue` | `ConditionValuesAreNullOrWhiteSpace` | Defined and never thrown. A null value normalizes to `""` and is judged by the DataType like any other string |
| `RequiredValues` | `ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues` | In/NotIn with 0 values |
| `NotRequiredValues` | `ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues` | IsNull with values |
| `RequiredTwoValue` | `ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues` | Between without exactly 2 values |
| `RequiredOneValue(op)` | `ConditionWithOperator[{op}]MustHasOnlyOneValue` | Single-value operator with wrong count |
| `InvalidPageNumber` | `PageNumberMustBeGreaterThanZero` | PageNumber ≤ 0 |
| `InvalidPageSize` | `PageSizeMustBeGreaterThanZero` | PageSize ≤ 0 |
| `MustHaveFields` | `MustHasFields` | Empty fields list in Select |
| `InvalidFormat` | `InvalidFormat` | Value doesn't parse for declared DataType. For a date: not ISO 8601, year-first, or a declared format |
| `AmbiguousDateFormat` | `AmbiguousDateFormat` | A date value that leads with a day or a month (`01/09/2026`) and matches no declared format, or one two accepted formats read differently. `LogicException.Subject` carries the field: its path, and under `ApplyPolicy` the name the caller wrote |
| `SelectTypeMustHaveParameterlessConstructor` | `SelectTypeMustHaveParameterlessConstructor` | `Select<T>` or `Filter.Selects` on a `T` the projection cannot construct. `LogicException.Subject` carries the type's name, `typeof(T).Name` |
| `InvalidAlias` | `AggregationMustHasValidAlias` | Alias is not a plain identifier — empty, or carrying a dot, comma, space, or dash |
| `GroupByMustHaveFields` | `GroupByMustHasAtLeastOneField` | GroupBy with no fields |
| `GroupByFieldsMustBeUnique` | `GroupByFieldsMustBeUnique` | Duplicate GroupBy fields |
| `GroupByFieldCannotBeComplexType` | `GroupByFieldCannotBeComplexType` | Non-simple GroupBy field |
| `GroupByFieldCannotBeCollection` | `GroupByFieldCannotBeCollectionType` | GroupBy field ending on a collection of collections |
| `AggregationFieldMustBeSimpleType` | `AggregationFieldMustBeSimpleType` | Complex aggregation field |
| `AggregationFieldCannotBeCollection` | `AggregationFieldCannotBeCollectionType` | Aggregation field ending on a collection of collections |
| `AggregationAliasesMustBeUnique` | `AggregationAliasesMustBeUnique` | Duplicate aliases |
| `AggregationAliasCannotBeGroupByField(alias)` | `AggregationAlias[{alias}]CannotBeUsedInGroupByFields` | Alias clashes with field |
| `UnsupportedAggregatorForType(agg, type)` | `Aggregator[{agg}]IsNotSupportedForFieldType[{type}]` | Invalid aggregator for type |
| `SummaryOrderFieldMustExistInGroupByOrAggregate(f)` | `SummaryOrderField[{f}]MustExistInGroupByFieldsOrAggregateByAliases` | Order on non-grouped field |
| `HavingFieldMustExistInAggregateByAlias(f)` | `HavingField[{f}]MustExistInAggregateByAliases` | Having references unknown alias |
| `OrderFieldCannotEndOnComplexCollection(f)` | `OrderField[{f}]CannotEndOnCollectionOfComplexElements` | Order path ends on a collection of entities |
| — | `Unsupported combination of DataType '{type}' and Operator '{op}'.` | A `DataType` and `Operator` pair the predicate builder does not support, such as `Guid` with `GreaterThan`. Raised when the predicate is built, after the value checks have passed |

---

## Breaking Changes & Known Limitations

### ⚠️ Breaking Points

1. **Parameterless Constructor Required for Select Projection**
   `Select<T>(fields)` requires `T` to have a parameterless (default) constructor. If `T` does not have one — a positional record, most often — a `LogicException` is thrown whose `Message` is the stable code `SelectTypeMustHaveParameterlessConstructor` and whose `Subject` carries `typeof(T).Name`. Before 3.1.0 that message was an English sentence with the type name inside it. Most EF Core entity classes have parameterless constructors by default. A guarded query reaches the same refusal when a member carries `[DwNoSelect]`, because deny-select projects.

2. **Segment Operations are Async-Only**
   `ToListAsync<T>(Segment)` is the only entry point for segment queries. There is no synchronous `ToList<T>(Segment)` variant. The condition sets are combined into one query that the database orders and pages. `Union` and `Intersect` combine the sets' conditions; only `Except` on a type with a primary key needs a provider that translates a correlated `EXISTS`. Under `ApplyPolicy`, `DwCaps.MaxConditionSets` (default 10) bounds how many sets one request may carry.

   Until 3.1.0 each set was loaded into a list and the lists were combined in memory by object reference. With `AsNoTracking()`, with `Selects`, and under `ApplyPolicy` (always untracked), `Intersect` returned nothing, `Except` removed nothing and `Union` counted a row once per set; ordering ran after projection, and every row of every set was read. Untracked, projected and guarded segments now return the rows their sets describe, a tracking query without `Selects` returns the same rows as before, and sorting follows the database's collation instead of .NET string comparison. A type with no primary key uses SQL `UNION` / `INTERSECT` / `EXCEPT`, which needs every column to be comparable and a provider that supports the operators the request uses.

3. **Date Values Are ISO 8601, Year-First, or a Declared Format**
   Since 3.1.0 a date value must be ISO 8601, year-first, or a format the deployment declared through `DwDates.Configure`. The server's culture used to decide: `01/09/2026` was 1 September on a day-first server and 9 January on another. It is now refused with `AmbiguousDateFormat` unless the order is declared, and forms the lenient parser used to accept — `12:00` as today at noon — are `InvalidFormat`. A deployment that sent culture-formatted dates either switches its clients to ISO 8601 or declares the format once at startup. In exchange, a filter no longer depends on the server's culture or calendar, `DateTimeOffset` and `DateOnly` columns work, and a `DateTimeOffset` value is normalised to UTC. A zoned value on a `DateTime` member still converts to the host's local time. A format whose own text ISO 8601 or a year-first date already reads, such as `yyyy-MM-dd'T'HH:mm:ss'Z'`, is refused at `Configure`: declaring it could only change what such a value means. See [How the two date types compare](#how-the-two-date-types-compare).

4. **Case-Insensitive Operators use `.ToLower()`**
   All `I*` operators (e.g., `IContains`, `IEqual`) normalize both sides via `.ToLower()`. This works correctly with SQL Server (`COLLATE` is typically case-insensitive), but be aware of potential performance or behavior differences on case-sensitive database collations (e.g., PostgreSQL with `C` locale).

5. **`DataType.Enum` Reads the Member Name, Whatever the Storage**
   A value is matched by member name (any case) or by number, and EF Core translates it for an `int` column as readily as for a `string` one — the storage is not what decides. What the type does decide is the operator list: `Equal`, `NotEqual`, `In`, `NotIn`, `IsNull` and `IsNotNull` only. `Contains` / `StartsWith` / `EndsWith` against an enum-typed member throw `ParseException` (`No applicable method 'Contains' exists in type '<Enum>'`) under either storage. Use `DataType.Text` for a `string` column that merely holds enum names and needs those operators.

6. **Having Clause Fields Reference Aliases, Not Entity Properties**
   `Summary.Having` is itself the `ConditionGroup`, so the path is `Having.Conditions[].Field` (and the same inside its `SubConditionGroups`). Each of those fields must match an `AggregateBy.Alias`, not an entity property path.

7. **GroupBy Flattens Dotted Field Names in Results**
   Dotted `GroupBy` fields (e.g., `Category.Name`) produce flattened alias keys in the dynamic result objects (e.g., `CategoryName`). Order fields in `Summary.Orders` should use the dotted form; the library handles alias mapping internally.

8. **Collection Navigation Auto-Wraps with `.Any()`**
   When a condition's `Field` path traverses a collection property, the library automatically inserts `.Any()` lambdas. This means the filter checks if **any** item in the collection matches — there is no built-in `.All()` support.

9. **Thread-Safe Cache, But Configuration Changes are Eventually Consistent**
   `CacheExpose.Configure()` is thread-safe, but already-in-progress operations may use the previous configuration until they complete.

   Fixed in 3.1.0: validating a field path recorded its access for eviction before the path was validated. A path that fails adds no cache entry for eviction to remove, so under `LRU` (the default) or `LFU` every distinct invalid name a caller sent kept its record for the life of the process, and a caller sending unique invented names grew the process without limit. A path is now tracked only once it has validated.

10. **`getQueryString` Parameter Requires EF Core Provider**
   Passing `getQueryString: true` to `ToList` / `ToListAsync` calls `.ToQueryString()`, which needs an active EF Core database provider to produce SQL. On an in-memory `IEnumerable<T>` it does not fail: `QueryString` holds a placeholder sentence where the SQL would be.

11. **`SelectDynamic` / `FilterDynamic` / `ToListDynamic` / `ToListAsyncDynamic` Return Non-Generic Types**
    These methods return `IQueryable` or `FilterResult<dynamic>` instead of the strongly-typed equivalents. Downstream code must work with `dynamic` objects. Property names in the dynamic result follow these rules:
    - **Non-dotted paths** (`Name`, `Category`, `OrderItems`, …) are projected as-is — access them by their exact field name at runtime.
    - **Dotted paths through reference navigations** (e.g., `Category.Name`) produce **nested dynamic objects** reflecting the navigation hierarchy — access them as `result.Category.Name`, not as a flat `CategoryName`.
    - **Dotted paths through collection navigations** (e.g., `Category.Vendors.Id`) generate a `Select` lambda per collection segment — the result is a nested collection of dynamic objects accessible as `result.Category.Vendors[0].Id`.
    - **Multiple dotted fields** sharing the same root segment (e.g., `Category.Name` + `Category.Id`) are merged into a single nested object: `result.Category.Name` and `result.Category.Id`.
    - **Mixed whole-navigation + sub-field paths**: when both `"Category"` and `"Category.Name"` are requested, the sub-field projection takes precedence and `"Category"` is silently dropped.

12. **All Filter Extensions Apply Order and Page Before the Select Projection**
    All Filter extensions — both typed (`Filter<T>`, `ToList<T>(Filter)`, `ToListAsync<T>(Filter)`, and `ToListAsync<T>(Segment)` since 3.1.0) and dynamic (`FilterDynamic<T>`, `ToListDynamic<T>`, `ToListAsyncDynamic<T>`) — apply ordering and pagination on the typed `IQueryable<T>` **before** the select projection. This ensures that field names referenced in `orders` always resolve against the original entity type `T`, regardless of which fields are projected.

    Changed in 3.1.0: `PageCount` on an unpaged result is `1`, the one page the whole result occupies, and `0` when nothing matched, on filter, summary and segment results alike. It used to equal `TotalCount` for a filter or summary — one page per row — and to be `0` for a segment with condition sets. A client that draws page links from `PageCount` drew one link per row on every unpaged endpoint and now draws one.

13. **Condition Values Become Escaped Literals, Not Query Parameters**
    A condition's `Values` are written into the generated dynamic LINQ expression as string literals. Since **2.1.4** they are escaped first — a backslash is doubled and a double quote is backslash-escaped — so any value matches literally, `\` and `"` included, and a value can no longer break out of its literal to alter the predicate. Before 2.1.4 a value ending in `\` threw `ParseException: ')' or ',' expected`, and a crafted value could append predicate logic of its own.
    The literal then reaches the provider as a constant, so EF Core inlines it into the SQL rather than binding a parameter — a `Contains` on `"الثانية\"` renders as `instr(lower("p"."Name"), 'الثانية\') > 0`. EF Core escapes that literal for SQL itself, so this is not a SQL injection path; it does mean each distinct search term produces a distinct statement and its own plan-cache entry.

    Fixed in 3.1.0: a long `In` list ended the process. `In`, `NotIn`, `IIn` and `INotIn` on `Text`, and `In` and `NotIn` on `Guid`, `Number` and `Enum`, joined their values into one flat chain — `f == "a" || f == "b" || …` — which the expression parser reads as one level of nesting per value. EF Core and the expression compiler walk that tree recursively, so a single condition carrying about seven hundred values overflowed the request thread's stack, guarded or not, and a stack overflow ends the process: no `catch` can stop it. A list longer than 32 values is now nested as a balanced tree of flat chains of at most 32 terms. A list of 32 or fewer is written exactly as before, so its predicate and its SQL do not change, and a longer list returns the same rows. Under `ApplyPolicy`, `MaxConditionValues` also bounds the values of one condition (point 19).

14. **`AggregateBy.Alias` Must Be a Plain Identifier**
    The alias is emitted verbatim into the generated `Select` projection, so since **2.1.4** it must be a leading letter or underscore followed by letters, digits, or underscores. Letters are matched by Unicode category, so a non-Latin alias such as `"المجموع"` stays valid. Earlier releases only rejected aliases containing a dot, which let an alias holding a comma — `"Total, 1 as Leaked"` — append terms of its own to the projection. Aliases carrying any other separator never parsed, so nothing that worked is rejected.

15. **Members Named `Root`, `It` or `Parent` Are Ordinary Names**
    System.Linq.Dynamic.Core treats `it`, `root` and `parent` as keywords, in any case. Before 3.1.0 the library parsed with them on, so a navigation named `Root` or `It` was read as the row itself — `Root.Name` filtered, sorted, grouped, aggregated and projected the row's own `Name` — a navigation named `Parent` threw `ParseException`, and an `AggregateBy.Alias` named `root`, `it` or `parent` failed in `Having` and `Summary.Orders`. Under `ApplyPolicy` the gate decided on the path the caller named while the query read the row's own column: a dynamic projection of `Root.Name` returned a `[DwDenied]` `Name`, a filter on it tested the denied column, and a `[DwForceWhere]` scope reached through such a navigation filtered the row's own column. Every expression is now parsed with a configuration of the library's own, with the keywords off. `ParsingConfig.Default` is no longer read, so a host's changes to it do not reach DynamicWhere queries.

    With the context keywords off, `it`, `root` and `parent` name members like any other identifier, and so do the parser's predefined type names — `String`, `Int32`, `DateTime`, `Math`, `Convert`, `Guid`, `Uri`, `Object`, `Enum` and the rest. The words the parser does keep for itself are refused by name since 3.1.0. A path whose first segment is `new`, `iif`, `np`, `isnull`, `is`, `as`, `cast`, `true`, `false` or `null`, whatever the letter case, throws `LogicException` `FieldPath[{path}]StartsWithReservedName`, with that segment on `Subject`. The refusal sits where a path is validated, so every clause a caller writes answers alike — condition fields, `Orders`, `Selects`, `GroupBy.Fields`, `AggregateBy.Field`, the target of a `[DwAlias]` — guarded or not. A `DefaultOrder` entry naming one refuses nothing: a guarded query drops it, as it drops an entry it cannot read, and `ValidateModel` reports it as an error. Under `ApplyPolicy` the strict tier answers it as it answers every name it cannot use: the clause's `FieldDeniedFor*` code with `FieldPath` `"*"`. Only a path's first segment is affected, because the parser looks for a member after a dot, so `Owner.New` names the member and an alias named after one of these words still works. The parser used to answer instead: seven of the names raised its `ParseException`, `True` and `False` an `InvalidOperationException`, and `Null` was read as the null literal, so the query returned no rows and no error. A typed `Selects` entry naming such a member did work, since a typed projection is built without the parser; it is refused now too. The remedy for such a column is to rename the CLR property and map the column with `[Column("New")]`.

16. **`MaxConditionDepth`, `MaxConditionSets` and an Unprepared Context Refuse Guarded Requests 3.0 Ran**
    Two caps new in 3.1.0 bound the shape of a guarded request. `DwCaps.MaxConditionDepth` bounds how deeply condition groups nest: the top group counts as one, each level of `SubConditionGroups` adds one, and the count is taken on the caller's groups before forced predicates are injected. `DwCaps.MaxConditionSets` bounds how many condition sets one `Segment` sends, empty sets included. Both default to 10, so a guarded request 3.0.0 ran with groups nested eleven levels deep, or a segment with eleven or more sets, is now refused with `PolicyException` `CapExceeded` — `SourceOrigin` `"MaxConditionDepth cap (10), request had 11"` or `"MaxConditionSets cap (10), request had 11"` — unless the deployment raises the cap. Both refuse a value below 1, freeze with the posture, and bind from `Caps:MaxConditionDepth` and `Caps:MaxConditionSets`. Only `ApplyPolicy` enforces them: an unguarded query is not affected.

    A guarded query also requires a prepared context since 3.1.0. `ApplyPolicy(context)` throws `PolicyException` `PolicyContextNotPrepared` for a context that never went through `DwPolicy.PrepareAsync`, whether or not a store is configured. Only a store provider used to refuse one, so an attributes-only deployment ran such queries and would have started refusing the day it gained a store. `DwPolicyContext.IsPrepared` reports it, and the overload taking explicit options and a resolver does not check. See [The shape](#the-shape).

17. **The Strict Tier Keeps the Policy Trace Off Results**
    Before 3.1.0 every guarded terminal put its `PolicyTrace` on `FilterResult<T>.Policy`, `SummaryResult.Policy` or `SegmentResult<T>.Policy`, in both tiers. The trace names the fields a policy dropped, the attribute or rule that sealed each one, and every injected predicate — the detail the strict tier already refuses through `getQueryString` — and an API that serializes its result sends all of it to the caller. Under `DwTier.Strict`, `Policy` is now null unless `DwPolicyOptions.IncludeTraceInResult` is `true`; under `Convenience` it is still carried unless the option is `false`. `PolicyQueryable<T>.LastTrace` still holds the trace, so a strict deployment that read `result.Policy` reads `LastTrace` instead, or sets `IncludeTraceInResult = true`. See [Results and the trace](#results-and-the-trace).

18. **Under the Strict Tier an Unknown Field and a Denied Field Answer Alike**
    Before 3.1.0 a guarded query refused a field name matching nothing on the type with `LogicException` `ConditionMustHasValidFieldName`, and a denied field with a `PolicyException` carrying its path and, where one source decided, its `RuleId` and `SourceOrigin`. The two answers let a caller list the columns they may not see, one guess at a time. Under `DwTier.Strict`, outside a dry run, an unknown name is now gated as a field denied for every feature and receives the refusal a `[DwDenied]` field receives in that clause — `FieldDeniedForWhere`, `FieldDeniedForSelect`, `FieldDeniedForOrder`, `FieldDeniedForGroup` or `FieldDeniedForAggregate`, and `FieldDeniedForSegment` anywhere in a segment — after the caps. Every refusal with one of those six codes carries `FieldPath` `"*"`, a null `RuleId` and a null `SourceOrigin`, whatever the field, and a `CapExceeded` refusal names no path either. The same tier closes the other ways to tell them apart: inside a segment every field refusal is `FieldDeniedForSegment`; a name padded with dots is normalized as a real path is; `MaxQueryCost` is checked after every field gate, so a `[DwCost]` weight cannot set a hidden field apart from a missing one; and `MissingContextValue` carries `FieldPath` `"*"` and no `SourceOrigin`. Code that caught `ConditionMustHasValidFieldName` from a strict guarded query, matched a clause's code inside a segment, or read `FieldPath`, `RuleId` or `SourceOrigin` off a strict refusal, reads `PolicyQueryable<T>.LastTrace` instead, which keeps the real path and reason, or records refusals with `DwPolicyOptions.AuditRefusals`. The convenience tier and dry runs are unchanged. See [Blocked-action semantics](#blocked-action-semantics).

19. **`MaxConditionValues` and `MaxAggregates` Refuse Guarded Requests 3.0 Ran**
    Two more caps new in 3.1.0. `DwCaps.MaxConditionValues` (default 1000) bounds the values one condition carries — the largest condition of the where clause, a summary's `Having` and every segment set is the one compared — because an `In` is one comparison per value and so could build a predicate of any size for the price of one condition and one field. `DwCaps.MaxAggregates` (default 50) bounds the `AggregateBy` entries of one summary, through the summary terminals and the composable `Group` and `Summary`; the group-size floor's own count is not counted. A guarded request over either is refused in both tiers with `PolicyException` `CapExceeded`, `FieldPath` `"*"` and `SourceOrigin` `"MaxConditionValues cap (1000), request had 1001"` or `"MaxAggregates cap (50), request had 51"`, unless the deployment raises the cap. Both refuse a value below 1, freeze with the posture, and bind from `Caps:MaxConditionValues` and `Caps:MaxAggregates`. An aggregate with no field, such as a `Count`, is now charged `DefaultFieldCost` toward `MaxQueryCost`, where it cost nothing, so a summary that sat just under its budget can be refused with `QueryCostExceeded`. Every count cap is now checked before any field name is resolved, so an oversized request that also names a field that does not exist is refused with `CapExceeded`, where 3.0.0 resolved names first and answered `ConditionMustHasValidFieldName`. Only `ApplyPolicy` enforces them: an unguarded query is not affected.

---

## License

**MIT** — Free Forever. **Copyright © 2023-2026 Sajjad H. Al-Khafaji**

Free for commercial and personal use, forever. No license acceptance required.

Repository: [https://github.com/Sajadh92/DynamicWhere.ex](https://github.com/Sajadh92/DynamicWhere.ex)