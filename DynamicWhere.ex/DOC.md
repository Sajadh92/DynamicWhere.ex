# DynamicWhere.ex

**Version:** 3.3.0 &nbsp;|&nbsp; **Target Framework:** .NET 6+ &nbsp;|&nbsp; **License:** MIT (Free Forever)

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
dotnet add package DynamicWhere.ex --version 3.3.0
```

**Dependencies:**
| Package | Version |
|---------|---------|
| `Microsoft.EntityFrameworkCore` | 6.0.22 |
| `System.Linq.Dynamic.Core` | 1.6.7 |
| `Microsoft.Extensions.Caching.Memory` | 6.0.2 |
| `Microsoft.Extensions.Configuration.Abstractions` | 6.0.0 |
| `Microsoft.Extensions.Configuration.Binder` | 6.0.0 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 6.0.0 |

`Microsoft.Extensions.Caching.Memory` is named for its patched version (3.3.0) and is not used by the library directly: EF Core 6.0.22 asks for 6.0.1 or later, and 6.0.1 is the last version open to CVE-2024-43483 (GHSA-qj66-m88j-hmgj), so a host on the EF Core 6 floor resolved a vulnerable version through all four packages. Naming 6.0.2 raises that floor for all four; a host on EF Core 8 or later already resolves a newer one and sees no change.

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
| `Number` | Numeric value (byte → decimal). The value is read as the expression parser reads it, in the invariant culture, and has to compare with the member (3.3.0) — see [Condition Validation Rules](#condition-validation-rules) | `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull` |
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

A value is read once to validate its format and again to build the predicate, so pass values that do not change: one whose `ToString()` answers differently each time is validated as one value and queried as another. Anything decoded from JSON is such a value already. No policy decision reads a value's content — only how many there are — so nothing a guard decides rests on which read won.

**A number is read as the expression parser reads it (3.3.0).** The builder writes a `Number` value into the generated expression unquoted, exactly as sent, so validation reads it the same way rather than through the host's culture. First the parser's grammar, in the invariant culture and ASCII digits only: optional white space, an optional minus, digits, an optional fraction — a point with a digit on both sides — and an optional exponent. No leading plus, no thousands separator, no trailing sign, no parentheses, no `NaN` and no `Infinity`; an integer must fit `UInt64`, or `Int64` when negative, while a real has no bound. Then, in a `Where` condition and for the operators that write the value into a comparison, whether that literal compares with the member the condition names — the parser itself is asked, against the member's declared type, so `1.5` is refused on an `int?` but not on an `int`, an exponent form on a `decimal`, an integer above `Int64.MaxValue` on a signed integral member, a negative number on a `ulong`, any number on a `string`, `bool`, `Guid`, `DateTime` or `char` member or on a collection of simple values, and a nullable enum under an ordering operator. A `Having` condition reads the grammar and stops, since an alias has no member type to ask about. Everything refused is `InvalidFormat`, the same in both policy tiers, and nothing that ran before is refused now. JavaScript writes `0.0000001` as `1e-7`, which a `decimal` member refuses; send it as the string `"0.0000001"`. See breaking point 38.

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

**`Clone()`** *(public since 3.3.0)* returns a deep copy — the condition tree with its groups and conditions, the projection list, each order and the page — every node new, though the values a condition carries stay the caller's own objects in a new list — so reading the same request again with one part changed, the next page or another order, never edits what the caller handed in. Rebuilding a request around the caller's own clauses leaves both holding one condition tree, and a rewrite of either reaches both. A null entry inside a list is copied as a null entry rather than failing on it (3.3.0), so the refusal belongs to the method that runs the request and reads the same for a copy; it used to throw `NullReferenceException`.

---

#### `Segment`

Combines multiple condition sets with set operations (Union / Intersect / Except), plus ordering and pagination.

| Property | Type | Description |
|----------|------|-------------|
| `ConditionSets` | `List<ConditionSet>` | Ordered condition sets |
| `Selects` | `List<string>?` | Optional field projection |
| `Orders` | `List<OrderBy>?` | Optional sort criteria |
| `Page` | `PageBy?` | Optional pagination |

**`Clone()`** *(public since 3.3.0)* returns a deep copy — every condition set with its own condition group, the projection list, each order and the page — every node new, though the values a condition carries stay the caller's own objects in a new list — so reading the same request again with one part changed, the next page or another order, never edits what the caller handed in. Rebuilding a request around the caller's own clauses leaves both holding one condition tree, and a rewrite of either reaches both. A null entry inside a list is copied as a null entry rather than failing on it (3.3.0), so the refusal belongs to the method that runs the request and reads the same for a copy; it used to throw `NullReferenceException`.

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

**`Clone()`** *(public since 3.3.0)* returns a deep copy — the condition group, the group-by with its aggregates, the having clause, each order and the page — every node new, though the values a condition carries stay the caller's own objects in a new list — so reading the same request again with one part changed, the next page or another order, never edits what the caller handed in. Rebuilding a request around the caller's own clauses leaves both holding one condition tree, and a rewrite of either reaches both. A null entry inside a list is copied as a null entry rather than failing on it (3.3.0), so the refusal belongs to the method that runs the request and reads the same for a copy; it used to throw `NullReferenceException`.

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

All extension methods live in `DynamicWhere.ex.Source.Extension` and operate on `IQueryable<T>` (or `IEnumerable<T>` for in-memory variants). There are 28 of them: the eighteen on `IQueryable<T>` that 3.1 had, three in-memory variants on `IEnumerable<T>`, and, since 3.2.0, seven more on `IQueryable<T>` — overloads of the asynchronous terminals that take a `CancellationToken` (see [Cancellation](#cancellation)).

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
- No entry may be null or blank — `InvalidField` since 3.3.0, where it used to be an `ArgumentNullException` from the name lookup.
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
- No entry may be null or blank — `InvalidField` since 3.3.0, where it used to be an `ArgumentNullException` from the name lookup.
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

Groups the query by the specified fields and applies aggregations. Under `ApplyPolicy`, groups smaller than `DwCaps.MinGroupSize` — **5 by default** — are dropped; see [k-anonymity](#k-anonymity--the-control-you-would-not-guess).

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

**Collection paths:** when `Field` crosses a collection navigation, the collection is reduced to one comparable value — the **smallest** element ascending, the **largest** descending. See [Ordering Across Collections](#13-ordering-across-collections).

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

The offset, `(PageNumber - 1) * PageSize`, is worked out in 64 bits and held to `int.MaxValue` (3.3.0), here and in the three summary methods. In 32 bits the product wrapped for a large enough page number: a negative offset is an error on SQL Server and PostgreSQL, so the request became a five-hundred, and the first page again on SQLite and in memory, so a page far past the last row returned rows. A page past the last row is an empty page however far past it is. The core sets no upper bound on either value; the policy layer caps `PageSize` through `MaxPageSize` and never `PageNumber`.

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

Since 3.2.0 two overloads take a `CancellationToken`, which reaches the count and the read: `.ToListAsync<T>(Filter filter, CancellationToken cancellationToken)` and `.ToListAsync<T>(Filter filter, bool getQueryString, CancellationToken cancellationToken)`. See [Cancellation](#cancellation).

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

Async version of `ToListDynamic<T>(Filter)`. Counts with EF Core's `CountAsync()` and, since 3.2.0, reads with EF Core's own `ToListAsync()`, called for the query's element type: the class the projection generates, or `T` when `Selects` is null. It used to read with Dynamic LINQ's `ToDynamicListAsync()`, asynchronous as well but with no token to pass on; on an EF Core query a canceled token now reaches the database. The rows are the same. Only the count needs an EF Core async provider; on any other provider the read falls back to Dynamic LINQ's.

Since 3.2.0 two overloads take a `CancellationToken`, which reaches the count and the read: `.ToListAsyncDynamic<T>(Filter filter, CancellationToken cancellationToken)` and `.ToListAsyncDynamic<T>(Filter filter, bool getQueryString, CancellationToken cancellationToken)`. See [Cancellation](#cancellation).

**Returns:** `Task<FilterResult<dynamic>>`

---

### `.Summary<T>(Summary summary)`

Applies where → group → having → order → page to a query. Under `ApplyPolicy`, groups smaller than `DwCaps.MinGroupSize` — **5 by default** — are dropped; see [k-anonymity](#k-anonymity--the-control-you-would-not-guess).

**Returns:** `IQueryable` — dynamic grouped query.

---

### `.ToList<T>(Summary summary, bool getQueryString = false)`

Materializes a `Summary` and returns a `SummaryResult`. Under `ApplyPolicy`, groups smaller than `DwCaps.MinGroupSize` — **5 by default** — are dropped from the result; see [k-anonymity](#k-anonymity--the-control-you-would-not-guess).

**Returns:** `SummaryResult`

---

### `.ToList<T>(IEnumerable<T>, Summary summary, bool getQueryString = false)`

In-memory variant for summary operations. `ApplyPolicy` takes an `IEnumerable<T>` too, and a summary read through it is floored like any other: groups smaller than `DwCaps.MinGroupSize` — **5 by default** — are dropped; see [k-anonymity](#k-anonymity--the-control-you-would-not-guess).

**Returns:** `SummaryResult`

---

### `.ToListAsync<T>(Summary summary, bool getQueryString = false)`

Async version of `ToList<T>(Summary)`, and floored the same way: under `ApplyPolicy`, groups smaller than `DwCaps.MinGroupSize` — **5 by default** — are dropped; see [k-anonymity](#k-anonymity--the-control-you-would-not-guess). On an EF Core query it counts the groups with EF Core's `CountAsync()` and reads them with EF Core's `ToListAsync()`. Until 3.2.0 the count ran synchronously and the read went through Dynamic LINQ's `ToDynamicListAsync()`, which had no token to pass on; on an EF Core query a canceled token now reaches the database. The count and the rows are the same. A source whose provider is not EF Core's, such as rows in memory through `AsQueryable()`, keeps the synchronous count and Dynamic LINQ's read, on the calling thread.

Since 3.2.0 two overloads take a `CancellationToken`, which reaches the count and the read: `.ToListAsync<T>(Summary summary, CancellationToken cancellationToken)` and `.ToListAsync<T>(Summary summary, bool getQueryString, CancellationToken cancellationToken)`. See [Cancellation](#cancellation).

**Returns:** `Task<SummaryResult>`

---

### `.ToListAsync<T>(Segment segment)`

Async-only segment operation. Combines every `ConditionSet` with set operations (`Union` / `Intersect` / `Except`) into one query, then orders, pages, projects and counts it in the database exactly as `ToListAsync(Filter)` does. Only the requested page is read, and `Orders` apply before `Selects`. `Union` and `Intersect` combine the sets' conditions and `Except` matches rows by primary key, never by object reference, so on a type with a primary key, tracking, `AsNoTracking()` and `Selects` return the same rows. Ordering follows the database: text sorts by its collation.

Since 3.2.0 an overload takes a `CancellationToken`, `.ToListAsync<T>(Segment segment, CancellationToken cancellationToken)`, and passes it to the count and the read. See [Cancellation](#cancellation).

**Returns:** `Task<SegmentResult<T>>`

---

### Cancellation

*New in 3.2.0.* Every asynchronous terminal has overloads that take a `CancellationToken`, guarded and unguarded:

| Terminal | Overloads with a token |
|---|---|
| `ToListAsync` with a `Filter` | `(Filter filter, CancellationToken cancellationToken)` · `(Filter filter, bool getQueryString, CancellationToken cancellationToken)` |
| `ToListAsyncDynamic` with a `Filter` | `(Filter filter, CancellationToken cancellationToken)` · `(Filter filter, bool getQueryString, CancellationToken cancellationToken)` |
| `ToListAsync` with a `Summary` | `(Summary summary, CancellationToken cancellationToken)` · `(Summary summary, bool getQueryString, CancellationToken cancellationToken)` |
| `ToListAsync` with a `Segment` | `(Segment segment, CancellationToken cancellationToken)` |

- The token reaches the count and the read. On an EF Core query a canceled token stops whichever of the two is running, and the call throws `OperationCanceledException`. EF Core's `TaskCanceledException` derives from it. `ToListAsync(Summary)` on a provider that is not EF Core's, such as rows in memory, checks the token before its synchronous count and read.
- The overloads without a token pass `CancellationToken.None`.
- They are overloads, not an optional parameter added to the old signatures. The 3.1 signatures are unchanged, so code compiled against 3.1 still binds. A reflection lookup of `ToListAsyncDynamic` by name alone now finds three methods where it found one, on the extension class and on the guarded handle, so `Type.GetMethod` given only the name throws `AmbiguousMatchException`; pass the parameter types.
- `ToListAsync(filter, default)` does not compile: `default` fits both `bool getQueryString` and `CancellationToken`, so the call is ambiguous (CS0121). So are `ToListAsyncDynamic(filter, default)` and `ToListAsync(summary, default)`, on a query and on the guarded handle alike. Write `false`, a token, or a named argument. A `Segment` takes no `getQueryString`, so `ToListAsync(segment, default)` binds the token overload.
- The guarded handle, `PolicyQueryable<T>`, has the same seven overloads (see [The shape](#the-shape)).

```csharp
// A minimal API binds a CancellationToken parameter to HttpContext.RequestAborted,
// so a client that disconnects cancels the count or the read.
app.MapPost("/customers/search", async (Filter filter, AppDbContext db, CancellationToken cancellationToken) =>
{
    var result = await db.Customers.ToListAsync(filter, cancellationToken);
    return Results.Ok(result);
});
```

---

## Validation Rules

**Before any of these (3.3.0).** Every method that takes a shape walks its lists for a null entry first, with or without a policy, in both tiers, sync and async: the composables `Where(ConditionGroup)`, `Order(List<OrderBy>)`, `Select`, `SelectDynamic`, `Group` and `Summary`, and every terminal for a `Filter`, a `Segment` and a `Summary`. `Filter` and `FilterDynamic` compose `Where`, `Order` and `Select`, so each list is walked as its clause is reached. Under `ApplyPolicy` the walk runs at the top of the sanitizer, before the caps and before the gate, because it is about the request's shape and not a policy decision. A null entry in `Conditions`, `SubConditionGroups`, `ConditionSets`, `Orders` or `AggregateBy` is `NullEntry(list)`; a `Selects` entry that is null or blank is `InvalidField`. A list that is itself null still means what it meant — most readers read it as empty. Before 3.3.0 a null entry surfaced as a `NullReferenceException` or an `ArgumentNullException` from inside the library. See breaking point 39.

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
| `Number` values must be a literal the expression parser reads — invariant, no thousands separator, no leading plus, no `NaN` — and, in a `Where` condition, one it can compare with the member the condition names (3.3.0) | `InvalidFormat` |
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

> Guarded, this is floored as a summary is: `DwCaps.MinGroupSize` defaults to **5**, and a smaller group is dropped rather than refused.

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

> **A guarded summary drops small groups by default.** `DwCaps.MinGroupSize` ships **on, at 5**, so a group with fewer than five rows is removed from the result — not refused, and nothing in the answer says a group was dropped. That is right for anonymised reporting and surprising for an operational count, where five is a real number of orders. Set `Caps.MinGroupSize = 1` to switch the floor off, deliberately. An unguarded summary is never floored. See [k-anonymity](#k-anonymity--the-control-you-would-not-guess).

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

### 10. `SelectDynamic<T>` — Dynamic Field Projection

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

### 11. `FilterDynamic<T>` / `ToListDynamic<T>(Filter)` / `ToListAsyncDynamic<T>(Filter)` — Full Dynamic Filter

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

### 12. Nested Collection Navigation

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

### 13. Ordering Across Collections

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
// At startup. A second call asking for the same posture does nothing; a different one is
// refused, because the tier is read by every request thread. See "Configuring twice".
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

`ApplyPolicy` returns a `PolicyQueryable<T>`, whose terminals mirror the core's: `ToList`, `ToListAsync`, `ToListDynamic` and `ToListAsyncDynamic` with a `Filter`, `ToList` and `ToListAsync` with a `Summary`, and `ToListAsync` with a `Segment`. Since 3.2.0 every asynchronous one also has the overloads that take a `CancellationToken` — `ToListAsync(Filter, CancellationToken)`, `ToListAsync(Filter, bool, CancellationToken)`, the same two for `ToListAsyncDynamic` and for `ToListAsync` with a `Summary`, and `ToListAsync(Segment, CancellationToken)`. The policy is applied first, so a refusal is thrown whatever the token says; the token then reaches the count and the read. See [Cancellation](#cancellation).

```csharp
var result = await db.Employees.ApplyPolicy(caller).ToListAsync(filter, cancellationToken);
```

### Attribute reference

| Attribute | Applies to | Effect |
|---|---|---|
| `[DwEntity(RequirePolicy = true)]` | class | An unguarded query on the type throws `PolicyRequired` |
| `[DwEntity(DefaultOrder = "CreatedAt desc, Id")]` | class | The order a guarded query takes when its caller sends none. Unguarded calls ignore it. See [Default order](#default-order) |
| `[DwDeny(features)]` | member | Refuse any of `Where`, `Select`, `Order`, `Group`, `Aggregate`, `Segment` |
| `[DwDenied]` | member | Refuse all six |
| `[DwNoWhere]` `[DwNoSelect]` `[DwNoOrder]` `[DwNoGroup]` `[DwNoAggregate]` | member | Refuse one feature each |
| `[DwOperators(Allow =, Deny =)]` | member | Restrict which operators may target the member |
| `[DwAlias("name")]` | member | A public name, accepted anywhere a field path is, renamed back on output. A name spelled like another member of the same type is reported by `ValidateModel` (3.3.0): a generated row cannot carry one name twice, so the rename is not applied there and both columns keep their own names |
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

Only public instance properties are read, through navigations and collection elements, up to paths of four segments. Two kinds of path lie outside that walk, and since 3.3.0 both are policed:

- **Beneath a member whose type the framework declares.** `Salary.Value` and `Salary.HasValue` on a `decimal?`, `Secret.Length` on a `string`, `Born.Year` on a `DateTime`, `Bag.Count` on a dictionary, `Lines.Count` on an application's own collection class — the collection's own member, not an element's. No attribute can be placed there, so such a path takes every fragment of the member it reads, whichever provider supplied it: the deny effects per feature, the `[DwOperators]` restriction (intersected), the `[DwCost]` weight and the audited features. Not what is said to the caller about the member — the alias, the required filter (a filter on `TenantId.Value` does not satisfy a `[DwRequireWhere]` on `TenantId`), the forced scope, and the descriptive facts. A rule naming the sub-path itself still applies alongside. One feature is one feature: `[DwNoWhere] Born` refuses `WHERE Born.Year` and still allows `GROUP BY Born.Year`, and a member nothing denies is read beneath as before, so `Name.Length` still runs. Where the member is transformed there is no member beneath it to apply the chain to, so `Select`, `Group` and `Aggregate` on the path are refused. A member only a **subtype** of the navigated type declares is not such a path: it is decided by the fragments naming it, so a grant of `Zone` under a `"*"` deny does not grant what a subtype of Zone's type declares. A navigation into an application's own type is still a separate field, because its members can be decorated: a denial on `Contact` leaves `Contact.Email` open.
- **Past the walk's depth.** `Caps.MaxNavigationDepth` defaults to 4, the depth the walk reads to, and a host may raise it; a request naming five or more segments then reached what no fragment covered. The attributes of the member at the end of such a path are read directly now — the deny family, `[DwOperators]`, the transform stages, `[DwCost]`, `[DwAudit]`, `[DwDescribe]` and allowed values — by any resolver that reads attributes, which every resolver `DwPolicy.Configure` builds does. What is declared about the queried entity itself is left out there, as it is around a cycle: `[DwAlias]`, `[DwRequireWhere]`, `[DwForceWhere]`. A transformed member there is still a member, so `Selects` naming it returns it transformed; only a grouping key and an aggregated field are refused, because a summary's own transform finds a generated row's columns by the type's list, which stops at four segments.

`[DwForceWhere]`, `[DwRequireWhere]` and `[DwAlias]` are left out around a cycle, on a type reached from itself, and apply on every other path within four segments. The walk used to return at its depth limit with the type still marked as being inside it, so a type first met at the fourth segment read as a cycle wherever it was met again in the same walk and the three were dropped from a shorter path reaching it directly — which of two members was declared first decided whether a tenant scope applied. Fixed in 3.3.0, so a query that ran unscoped is scoped and a required filter may now be demanded.

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
- the composable `Page`, on a source nothing has ordered and whose projection hides no field the default names.

It is never applied:

- by an unguarded call. The core extension methods on a plain `IQueryable<T>` or `IEnumerable<T>`, and a query taken out through `AsUnguardedQueryable()`, ignore the attribute and behave exactly as in 3.0;
- when the caller sends orders — the default is not appended to them as a tiebreak;
- to an `IQueryable<T>` that is already ordered, whether before it was guarded (`db.Tickets.OrderBy(t => t.Title).ApplyPolicy(caller)`) or by a composed `Order` earlier in the chain — even one whose every order the policy dropped, because the caller still sent orders. Since 3.2.0 a `Filter` composed on the handle that sent orders counts the same way. An in-memory sequence sorted with LINQ to Objects before `ApplyPolicy` is not seen as ordered, because `AsQueryable()` hides the sort, so the default replaces that order;
- to a source whose projection could hide a field the default names. Only the outermost `Select` of the chain counts, because it makes the rows the default orders. Since 3.2.0 it hides nothing when it builds `T` itself in an object initializer, `Select(t => new TicketRow { CreatedAt = t.CreatedAt, Id = t.Id, … })`, and assigns every field the default names a column, at every level of a nested path (`"Owner.Name"` needs `Owner = new OwnerRow { Name = … }`). On EF Core a column is a member the model maps on the entity the `Select` reads, read directly, through reference navigations (`t.Owner.Name`) or through `EF.Property` (a shadow property included); in memory any assigned field is one. The default then applies, because EF Core translates an order by a column. A constructor with arguments, a default field the initializer does not assign, a nested path through anything but an initializer, a member the model does not map, or a default field the projection computes, by the application's own method (`Label = Decorate(r.Code)`), a framework one such as `Regex.Replace` or `ToUpper`, or an operator, leaves the query in its own order, as every projection did in 3.1.0: which of these EF Core can order by depends on the provider, and a default must never be the reason a query that ran unguarded fails;
- after a projection composed on the handle. The guarded `Select`, or a guarded `Filter` whose `Selects` is set, leaves the rest of the chain unordered even when it keeps every default field, so `guarded.Select(["Id", "Title"]).Page(page)` on a type ordered by `Priority` pages unordered, as in 3.0.0. The default is for the rows the caller's source makes;
- to a `Summary`, or by the composable `Where`, `Select`, `Order` or `Group`.

Nothing is ordered that the type's own `[DwEntity]` did not declare, and a default is never a reason for the library to refuse a query. An entry naming a field the type does not have, one that is not a field optionally followed by a direction, one the core refuses to order by — a path ending on a collection of entities, such as `Tags` — or one whose name the expression parser keeps for itself, such as `Null`, is skipped. A path through a collection to a value, such as `Tags.Value`, is kept and sorted by its smallest value ascending or its largest descending. A field this caller may not order by is left out, in either tier, and never refused: the caller did not send it, and ordering by it would rank rows by a value they may not see. In a `Segment`, a field this caller may not use in a segment is left out as well, since a segment refuses it in any clause; a filter still orders by it. The trace records a `Dropped` decision for `Order` whose reason starts `left out of the default order`; a dry run keeps the field and still records the decision. A caller whose own orders were all dropped under `Convenience` sent orders, and gets no default in their place. The startup check reports every entry a query would skip or leave out.

A field the default keeps is a use of that field. One audited for `Order`, by `[DwAudit]` or a rule, is recorded as a use, `Effect` `Allow`, each time a guarded query orders by it, as a caller's own order is. A field the default leaves out is not recorded: the query does not order by it, and the caller never named it. A dry run keeps the field, so it records it, with its `Order` effect (`Deny` for a field the caller may not order by) and `DryRun` true. See [Auditing](#auditing).

`[DwEntity]` allows one attribute per type, and .NET attribute inheritance gives a derived type its own when it declares one: the base type's attribute is replaced, not merged. A subclass that declares `[DwEntity(DefaultOrder = "Id")]` loses its base type's `RequirePolicy`, and one that declares `[DwEntity(RequirePolicy = true)]` loses the base type's `DefaultOrder`. Repeat both on the derived type.

### Checking the model at startup

`DwPolicy.ValidateModel(options, types)` inspects the policy attributes on the given types and throws `InvalidOperationException` listing every error; `PolicyModelValidator.Inspect(types, options)` returns the same `PolicyModelReport` — `Errors`, `Warnings`, `IsValid` — without throwing. Called at startup, either one lets a misconfiguration fail the deployment rather than a caller's request. Since 3.1.0 the scan also reports:

- every `[DwForceWhere]` resolution would refuse: `Value` and `ContextValue` both set or both missing on a comparison, either one set on a null check, a member whose type has no `DataType`, and `AllowNull` with `IsNull` / `IsNotNull` or on a member that can never be null. Before, these surfaced on the first query that resolved them;
- every `[DwEntity(DefaultOrder = ...)]` entry a guarded query would skip or leave out. An entry that is not a field optionally followed by `asc` or `desc` is an error; a field whose name starts with one of the words the expression parser keeps is an error, judged before the type is asked whether it has the member, because it may well have it — `"{Type}: DefaultOrder names '{field}', which starts with a name the expression parser keeps for itself, so no query can use it. Rename the member."`; a field the type does not have is a warning; a field no query can order by, such as a collection of entities, is an error; a field the type's own attributes seal against ordering is an error, because every guarded query would leave it out; the attributes include those of the member's other declarations, an interface member it implements, a subtype's override and a public member a subtype hides with `new`. A field denied for ordering only by attributes marked `Overridable = true` is a warning, because a rule can lift the denial for some callers — `"{Type}: DefaultOrder names '{field}', which its attributes deny for ordering unless a rule allows it, so guarded queries leave it out until one does."` — and so is a field the attributes deny for segments: `"{Type}: DefaultOrder names '{field}', which its attributes deny for segments, so guarded segments leave it out."`

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
- A blank name fails a guarded query as it fails an unguarded one, in every clause. A blank grouping key used to reach the resolver and fail with `ArgumentNullException`, which is neither a refusal nor the malformed-clause failure an endpoint turns into a four-hundred (3.3.0).
- `MaxNavigationDepth` counts the canonical path, so a name the caller wrote as one token — an alias — is refused as an unknown name is rather than with the cap's own code, which would say the token named something several navigations deep (3.3.0). A caller who wrote the path themselves meets the cap, as every over-long request does.
- Four more refusals name the clause rather than a field (3.3.0): an ambiguous name is refused as an unknown name is, and `AmbiguousGroupKey`, `TransformRequiresMaterialization`, `MissingHashSalt` and `MissingTokenVault` carry `FieldPath` `"*"`. All but the transform refusal drop their `SourceOrigin` too; that one's origin names a method rather than a field.
- A `CapExceeded` refusal names no path either. `MaxNavigationDepth` used to return the canonical spelling of the path the caller wrote, which confirmed that it named something. `SourceOrigin` still names the cap.
- `MissingContextValue` has `FieldPath` `"*"` and a null `SourceOrigin`, so it names neither the scope's column nor the context key it reads, which together describe how rows are partitioned. The trace keeps both, and an audited refusal records the scoped field.
- `MaxQueryCost` is checked after every field has passed its gate. A field weighted by `[DwCost]` that the caller may not use is refused as denied before its weight counts, exactly as a name that matches nothing is, so the budget cannot tell the two apart. An allowed weighted field is still refused with `QueryCostExceeded`.
- The trace keeps the real path and reason, and records an unknown name as `Denied` with the reason `names nothing on {TypeName}`. An audited refusal keeps the real field too (see [Auditing](#auditing)).

The convenience tier is unchanged: an unknown name fails validation with `LogicException` `ConditionMustHasValidFieldName`, a refusal names the field as the caller wrote it, with `RuleId` and `SourceOrigin` where a single source decided, and `MaxQueryCost` is checked before any field is gated. A dry run refuses no field, so an unknown name fails validation there in either tier.

**A member the query cannot compute is refused the same way** *(3.3.0)*. A member of the row's type is not always a value a database can produce. A shared type such as

```csharp
public sealed class LocalizedText
{
    public string Ar { get; set; } = "";
    public string En { get; set; } = "";
    public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
}
```

gives `Name.Ar` and `Name.En`, which translate, and `Name.IsEmpty`, which is a getter over the two. The policy has nothing to say about it — `[DwNoWhere]` on `Name` matches that path and not the ones beneath it — so every check passed and EF Core threw `InvalidOperationException`: a five-hundred where `Strict` promises a refusal. Such a path is now refused as an unknown name is, with the clause's own code and `FieldPath` `"*"`, in every clause the database has to compute: a filter, an order, a grouping key, an aggregated field, and a filter or an order inside a `Segment`.

**`Selects` is not one of them.** A projection is the last thing the provider builds, and EF Core evaluates that one on the client when it cannot translate it, so `Selects = ["Id", "Name.IsEmpty"]` returns the computed value exactly as it did before. Refusing it would take back a projection that has always worked.

It is refused only where the whole set of members a container can produce is known:

| Source | Read from | `Name.IsEmpty` |
|---|---|---|
| An entity | the EF Core model: columns, shadow properties, owned and complex members, navigations | Refused |
| A row a `Select` built before `ApplyPolicy` | the initializer's own assignments, at every level, both branches of a conditional included | Refused |
| …where the initializer assigns the member from something else: a method call, a captured value, a subquery, or two branches building it two ways | nothing — the assignment is not one this shape reads | Left alone, as it always did |
| …where that `Select` copies the member from the entity, `Name = role.Name` | the model, beneath the member it copies | Refused |
| Rows in memory | nothing — the getter runs | Runs, as it always did |
| Anything beneath a column, a converted one included | nothing — the converter decides | Left alone, as it always did |
| A framework member such as `Length`, `Year` or `HasValue` | nothing — the provider translates it | Runs, as it always did |
| A source the library cannot read | nothing | Left alone, as it always did |
| A provider in front of EF Core: an expression expander, a decompiler | nothing — it rewrites what EF Core cannot translate | Left alone, as it always did |
| A column only a subtype maps, queried through the base | the queried type's model, which is what EF Core translates against | Refused |
| A projection a provider that is not EF Core's ran | nothing — its rules are its own | Left alone, as it always did |

A shadow property is a separate matter and unchanged: a field path names CLR members, and a shadow property has none, so no clause can name one — guarded or not, before this release or after it. Map it to a property, or project it with `EF.Property` and name the projected member.

A member assigned through a sequence operator — `Lines = o.Lines.ToList()`, `o.Lines.Where(...).ToList()`, or a subquery building rows of its own — is left alone: what the row holds is not always what the navigation holds, and reading it as the navigation would refuse a member the row carries. A projection that does not build its rows with an object initializer is left alone whole: an anonymous type, and a constructor with arguments, say nothing about which member each value sets, so no member of such a row is refused here and none is claimed. A projection is otherwise read only as far as its initializer can be read. An entity query names every producible member from the model; a projection names them only where each assignment is a nested initializer, a member copied from the entity, a value built and left empty, or a conditional over those — a null branch beside one of them included. A member assigned nothing but a null is left alone, like any assignment this shape cannot read. Past `MaxComplexDepth` — eight levels — it stops reading and stops speaking. Under `Strict` such a path still reaches the provider and still fails there, exactly as it did before 3.3.0.

**Left alone** is not a promise that the path runs. The policy does not refuse it, so it behaves exactly as it does unguarded: `Name.IsEmpty` beneath a column mapped through a value converter still fails inside the provider, as it always has.

An unmapped getter on the entity itself, `Display => $"{Code}:{Id}"`, is refused for the same reason. The convenience tier and a dry run are unchanged: both fail exactly as the unguarded query does, which is the provider's own error. The trace records the refusal — `the member exists on the type and the query cannot compute it` — and since 3.3.0 `LastTrace` is set before a request is sanitized, so a refusal leaves it readable rather than null.

The rule is EF Core's own provider's — that exact type, from EF Core's own assembly. A provider that wraps EF Core — LinqKit's `AsExpandable()`, DelegateDecompiler's `Decompile()` — exists to rewrite the members EF Core cannot translate, so a member it computes is one the query produces and it is left alone, over a projection and over an entity alike. A provider of any other type is left alone for the same reason turned around: the library cannot tell one that rewrites from one that passes straight through, and refusing on that guess would take back a query the rewriting host answers today. That covers a host registering its own provider through EF Core's `ReplaceService<IAsyncQueryProvider, …>`, whose queries are read as another provider's and never refused here, however plain the provider is. A rewrite *inside* EF Core's own pipeline is a different matter: a member-translator plugin or a replaced query preprocessor leaves EF Core's own provider in place, so a member it computes without a mapping is refused with the rest. A row the library itself projected is read like any other: the core's typed `Select` null-guards every nested node it builds, and both branches of that guard are read, so composing `Select` and then filtering refuses exactly what the bare handle refuses.

A refusal here raises no `[DwAudit]` event, for the same reason an unknown name raises none: no field was read, and the refusal names none. `AuditRefusals` records it, and so does the trace.

The rule is the model's: a member it maps nowhere is one the database cannot compute. A member some provider extension computes without a mapping is refused with the rest, so map it, or filter on the columns beneath it.

### A navigation named in Selects

A `Selects` entry can name a navigation, such as `"Lines"`, rather than the fields beneath it. With nothing denied beneath it, the entry is kept as written. With a denied field beneath it, the `Convenience` tier replaces the entry with the allowed fields beneath it, and the `Strict` tier refuses it with `FieldDeniedForSelect`.

| `Selects` names a navigation | `Convenience` | `Strict` |
|---|---|---|
| with nothing denied beneath it | Kept whole, or narrowed around a transform that lands on a property with no setter (3.2.0) | Kept whole, or narrowed the same way |
| with a denied field beneath it | Replaced by the allowed fields beneath it | `FieldDeniedForSelect` |
| whose key, `Lines.Id`, is denied (3.2.0) | `FieldDeniedForSelect` | `FieldDeniedForSelect` |
| with a denied field beneath it, where the narrowing cannot be built (3.2.0) | `FieldDeniedForSelect` | `FieldDeniedForSelect` |
| that can carry a field denied for `Select` that no path names: past four segments, in a framework generic, on a subtype, or unasked under a `"*"` deny (3.2.0) | Narrowed to the allowed fields where the core can narrow it; `FieldDeniedForSelect` where it cannot | `FieldDeniedForSelect` |

- The fields beneath a member are read the way the attribute walker reads them: through any collection type, and no deeper than its four segments. Since 3.2.0 a member typed `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `Collection<T>` or an application's own collection no longer hides the denials beneath it. The providers' own rules are asked too, so a denied property with no setter and a rule on a path reached through a cycle count.
- A narrowing that cannot be built as it was gated is refused in both tiers (3.2.0). The core's typed projection adds the key, `Id`, of every nested node it builds, so a navigation narrowed around its own denied key would get the key back. The core reads a path only through an array, `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `HashSet<T>` or `ISet<T>`, so a narrowing through any other collection fails its validation. And some members cannot be narrowed at all: a column, a complex property or a member stored as JSON, which EF Core reads whole; a member of a row in memory; and a member a projection builds some way the core cannot narrow.
- A named member can also carry a field denied for `Select` that no path names (3.2.0): one deeper than four segments, one inside a framework generic such as `Dictionary<string, T>`, or one a subtype of the member's type declares — a derived entity, a subclass, an interface's implementation. What it can carry is read from the source. On an entity it is read from the EF Core model, so only what loads counts: the navigation's columns, a converted one included, its owned chain at any depth, the navigations beneath it an include, an automatic include or a lazy loader fills, and each member the model does not map, read as its type, since its getter can hand out what EF Core loaded — for its type and every type the model derives from it. On a projected row it is the type the initializer constructs the member as, when it says, and otherwise the member's type and every loaded subtype of it, as on a row in memory. Under a policy with a `"*"` deny, a path the walk never asks about — past four segments, with no setter, or on a subtype — is a denied one unless the policy names it; around a cycle it always is. The `Strict` tier refuses such a member. The `Convenience` tier narrows it where the core can, which builds the declared type and so drops a subtype's fields; a path naming a framework generic itself narrows to nothing and is dropped. Where the core cannot narrow it — a column at the top of `T`, a member of a row in memory — both tiers refuse it.
- A navigation named through another, `Main.Lead`, gates the key of every node it passes through, which the core's projection adds, as a dotted path to a value always did (3.2.0). A denied key refuses the projection. Naming a field beside a denied key, `Lines.Name` when `Lines.Id` is denied, was already refused in both tiers.
- Under `Convenience` the refusal names the denied key, the first denied field beneath the member, or, for a denial no path names, the member itself. Under `Strict` its `FieldPath` is `"*"`, as on every field refusal. The trace records the reason either way.

### A request that sends no Selects

A request that sends no `Selects` returns whole rows, denied fields included, because the core projects only when `Selects` is set. So when a field denied for `Select` could reach the result, a guarded query synthesizes the projection itself. It does so in both tiers, typed and dynamic, for a whole `Filter` and for a `Segment`. A clause composed on its own, such as `Where`, `Order` or `Page`, synthesizes nothing.

The projection keeps the **allowed members**: what an unguarded call would return, less what the policy withholds. Before 3.2.0 it kept the allowed scalars only, and only a simple field denied at the top of the type asked for it (breaking point 20).

**When a projection is needed.**

- A field denied at the top of `T` always asks for one, whatever it holds: a scalar, a blob, a list, an owned object or a JSON column.
- A field denied beneath a member asks for one when its value can reach the result. On an entity, that is beneath a column, an owned or complex member, or a navigation something loads: an `Include` or `ThenInclude` on the query, an automatic include, or a lazy loader — EF Core's proxies, an injected `ILazyLoader`, a loader delegate or `ILazyLoader` the constructor takes and keeps in a field or any property, the asynchronous loader delegate of EF Core 7, or an injected `DbContext` — which fills a navigation after the query. On a row a projection builds, it is beneath a member the initializer assigns; a constructor with arguments counts every member as assigned, and an initializer after it still says what its own bindings hold. On a row in memory, it is beneath any member. A rule may spell the path in any letter case.
- Every navigation counts as loaded where the library cannot read which the query loads: an `Include` in a form it cannot read, one off the query's own chain, and a chain that reaches its rows through anything but the root's own rows — `Select(o => o.Customer)`, a `SelectMany`, a `Join`, a `GroupBy` — when it also has an include, which EF Core applies from the root to the entities it reaches, or when one of its lambdas hands its rows an object: one it builds, as a projection behind an identity `Select`, or an object built inside an anonymous row or a conditional, does; one an application's method returns from what the lambda gives it; or one it captured, another query with its own include or projection, or an object in memory. A call that reads nothing of the lambda's and returns a query or an expression — a specification, a repository's query, `FromSql`, a context's `Set` through an interface — is evaluated as EF Core evaluates it, and what it returns is read; a context's own query function is a query root; an anonymous object that only carries what the rows hold, query-syntax range variables or a composite key, builds nothing; and what only feeds a predicate or a key is a value and hands a row nothing. Such a chain with none of these is read from the model.
- A denial beneath a navigation nothing loads never leaves the database, so it asks for no projection. An entity whose only denials sit beneath such navigations is read as it was in 3.1.0.
- A member whose value can hold a field denied for `Select` that no path names — deeper than the walker's four segments, inside a framework generic such as `Dictionary<string, T>`, or declared by a subtype of its type — asks for one too, read as for a named member, so on an entity only what loads counts. Under a `"*"` deny, so does a member whose value can hold a path the walk never asks about and the policy does not name.
- A member that can hold an object of any type — one typed `object`, a framework interface such as `IComparable`, or a collection that is not generic, such as `IEnumerable`, `ArrayList` or an application's own — asks for nothing on its own: the policy cannot see into it whether or not a projection is built.
- A row can be a subtype of `T`. On an entity, a member a type the model derives from `T` declares, and what loads beneath it, counts as one of `T`'s own would; on a row in memory, a member any loaded subtype declares does; on a row a projection builds, a member the type its initializer constructs declares below `T`. The projection builds `T` and leaves them out, recorded with a reason starting `left out: a type derived`. A subtype is any type loaded outside the framework's assemblies that derives from the type or implements it, an open generic one and an application's subclass of `Exception` included; a rule on a path through a subtype's member counts as a rule on the declared type's own path does.
- A member EF Core does not map counts as loaded: its getter can hand out a mapped field or a private navigation, so its type is read whole.
- The denials beneath a member come from the providers' rules as well as from walking the type, so a denied property with no setter, a rule on a path reached through a cycle, and a rule deeper than the walk all count.
- A forced scope beneath a member asks for no projection on its own. It filters the rows that hold the member, as it always has. When a projection is needed anyway, the member is left out whole.

**What it keeps.** A member holding a value — a simple type, or a collection of one such as `byte[]`, `string[]` or `List<string>` — is kept when it is allowed and the source carries it. A member holding an object, or a list of them, is kept whole, narrowed or left out whole, as below, and only where the source carries it:

| Source | Values kept | Objects kept |
|---|---|---|
| A projection that builds its rows before `ApplyPolicy`: the outermost `Select` constructs the row, in an object initializer or with a constructor, as in `db.Roles.Select(r => new RoleRow { … })` | Every member the initializer assigns; every member when a constructor with arguments builds the row, with or without an initializer after it | The same |
| An entity query, or a `Select` that hands back an entity, as in `db.Orders.Select(o => o.Customer)` | Every member EF Core maps | Its columns, converted and JSON ones included, its owned members and, on EF Core 8 or later, its complex properties, read from the EF Core model; a converted value that can hold an object of any type is left out |
| Rows in memory, as in `roles.ApplyPolicy(caller)` | Every member | None |

A value EF Core does not map is left out: computing it would make EF Core read the whole entity, the denied columns included, and it holds only its initial value anyway. A source the library cannot read — no EF Core model, and neither a projection it can see into nor rows in memory — keeps values only, as in 3.1.0, and every denial beneath a member counts.

**Whole, narrowed or left out whole.** A member holding an object that the source carries is:

- **kept whole** when nothing beneath it is denied, nothing its value can hold is denied (its subtypes included), it cannot hold an object of any type (asked of a projected row, a row in memory, and an entity's column a value converter hands back, directly or inside a complex property: what EF Core materializes itself never holds one), under a `"*"` deny every path beneath it the walk skips is one the policy names, no forced scope is beneath it, and no transform beneath it lands on a property with no setter;
- **narrowed** otherwise, to the allowed fields beneath it, four segments deep, as a caller naming it would get it, where the core's narrowing translates: an object the projection's initializer builds, a list a subquery reads into a type the core can bind (not an array or a set), a navigation that is neither complex nor stored as JSON, or an entity's owned member not stored as JSON. The narrowing builds the declared type, so a subtype's fields are dropped. A field beneath it that can hold what the policy cannot name is left out;
- **left out whole** otherwise, and recorded as `Dropped` on `Select` with a reason that starts `left out whole`.

```text
left out whole: a scope forced beneath it cannot be applied to what it holds
left out whole: it is a column, which EF Core reads whole
left out whole: it is a complex property, which EF Core cannot narrow
left out whole: it is stored as JSON, which EF Core cannot narrow
left out whole: the projection builds it in a way the core cannot narrow
left out whole: the projection would add its key 'Contents.Id', which is denied
left out whole: the core cannot project 'Contents.Code'
left out whole: the core cannot build 'IContact', which it narrows into
left out whole: nothing beneath it may be selected
left out whole: it can hold what the policy cannot name
```

The last one is recorded on the field beneath the member that the narrowing leaves out.

**Never kept.**

- An entity's navigation, included or not: projecting it would load it. So once a denial needs a projection, an included or automatically included navigation is not returned, and the trace records it as `Dropped` with a reason starting `left out:`. Under `Convenience`, name it in `Selects` to get it narrowed; under `Strict`, name its allowed fields.
- An object held by a row in memory: a kept object is the caller's own, and a transform beneath it would change it in place. The projection's rows are new and hold no member of an object type, so the source objects are left as they were.
- A member with no setter, and a member named with one of the expression parser's own words.

**Other rules.**

- A narrowed reference that is null in the source comes back as an empty object, as it does for a caller's own dotted `Selects`.
- A narrowed member carries every allowed field beneath it. An entity reached beneath it therefore has its own navigations projected, and so loaded, whether or not the source included them, exactly as when `Selects` names the member.
- Each denied field whose value can reach the result, at the top or beneath, is recorded as `Dropped` on `Select`.
- It never throws for a denied field, in either tier. It throws `AllSelectsDenied` only when no field is left. When nothing asks for a projection, `Selects` stays null and the query is the one an unguarded call runs.
- A typed query projects into `T`, so `T` needs a public parameterless constructor, or the query fails with `SelectTypeMustHaveParameterlessConstructor` (breaking point 1). The dynamic terminals do not need one.
- A dry run synthesizes nothing. It records the denials and returns the rows whole.
- A simulation has no source, so it reads `T` as a source it cannot see into: every denial beneath a member counts, and the projection it shows keeps only members holding a value (see [Administration](#administration)).

**What the policy cannot see into.** A member typed `object`, a framework interface or a collection that is not generic, such as `IEnumerable`, `ArrayList` or an application's own, is opaque to the policy. It never asks for a projection; when one is needed anyway, a projected row, a row in memory, and an entity's converted column leave it out, and an entity's other columns keep it; and naming it returns whatever it holds. A converter returning an application type through a column typed `object` is opaque the same way, so type the member as what it holds. `BitArray` and the framework's string collections hold values. An application's own collection class, generic or not, still has its own members read, and a collection of values stays a value unless one of them is denied. Two members sharing a name, one hidden with `new` under another type or spelled in another case, are left out when either holds a denial: the core reads one of them, and a row carries both. Rows in memory are projected when a member a base type declares, and the row type hides with `new`, is denied. A method in a reshaping lambda that builds a query from captured values runs once more per guarded read, and one that answers differently on each call is enforced as it answered the guard. A framework generic holding a policed type, such as `Dictionary<string, LineDto>`, has no paths beneath it: naming it is refused in both tiers where the core cannot narrow it, at the top of `T` or on a row in memory, narrowed away under `Convenience` beneath a navigation, and a synthesized projection leaves it out. Hold such values in a list of the policed type instead. A member EF Core does not map is read as its type, since its getter can hand out what EF Core loaded; a getter that copies a denied column into a type with no denial is the application's to withhold.

**A projection builds the declared type.** A query over the root of a hierarchy whose derived type declares a denied field comes back as root-type rows, the derived types' allowed fields dropped too. Over an abstract root the typed terminals fail with `SelectTypeMustHaveParameterlessConstructor`, and the dynamic ones return the root's members. Query the derived type, `OfType<Company>()`, to keep its fields. Rows in memory can be any loaded subtype, and the policy does not look at the rows: when a subtype declares a denied field, they are projected and their objects left out, even if no row is that subtype. Under a `"*"` deny, a member is kept whole only when every path beneath it the walk skips is one the policy names. A `[DwDenied]` on an override, on a public member a subtype hides with `new`, or on a class's implementation of an interface member, through a variant instantiation too, applies to the path through the base type or the interface, on every row and in every clause.

**A forced scope on a list's element type filters rows, not elements.** A forced scope declared on a list's element type filters the rows that hold the list, never its elements. `Selects` naming the list returns every element, those the scope excludes included, as in every release; a synthesized projection leaves such a list out. Scope the elements where the row is built.

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

`[DwAudit(features)]` records every use of a field, whatever the policy decided, as a `DwAuditEvent` in the caller's context (`DwPolicyContext.PendingAuditEvents`). A use is a field the request names, or, since 3.1.0, a field of the type's [default order](#default-order) that the query orders by: audited for `Order`, it is recorded each time, as a caller's own order is. A default field left out for this caller is not recorded, because the query does not order by it and the caller never named it. Nothing is stored until the buffer is drained to an `IDwAuditSink`, by `DwPolicy.DrainAuditAsync(context, sink)` or, per request, by the ASP.NET Core middleware `app.UseDwPolicyAudit()`. That middleware does not drain with the request's abort token: it has a budget of its own, thirty seconds, which the caller cannot cancel and a hung sink cannot outlast (3.3.0). Until then a client that closed the connection, as the rows arrived or the moment they had, cancelled the write that follows the response — the sink threw, the middleware logged it, and the events went with the context, an audited read with nothing written down for the price of a socket. A path beneath a member whose type the framework declares is audited as that member is (3.3.0): reading `Salary.Value` records what reading `Salary` records, where it used to record nothing. A use is what the request reads, not only what it spells out. A request that sends no `Selects` receives the row, so every audited member the query hands back is recorded for `Select` (3.3.0) — one event per query, not per row, and only for a field `[DwAudit]` names. What it hands back is read strictly: a member kept whole records the audited paths inside it, a navigation nothing loads records nothing because the caller receives null for it, and a value the source does not carry records nothing either. A dry run applies no projection, so everything the row carries is recorded there, a denied member included, with the effect the policy decided. Until 3.3.0 only a field the request wrote down was recorded, which left an empty `Selects` as one token past the control: the same value, returned, with nothing written down. An audited member the rows hand back where no path of the policy names it is recorded once the rows show it (3.3.0). The gate records a use by path, before the query runs, and a member only a subtype of the row's type declares, or one past the four segments the attribute walk reads, has no path it could ask about: handed back inside a row returned whole or a navigation kept whole, it was read with nothing written down. The outbound walk's second pass reports each one it meets and the terminal records it — one `DwAuditEvent` per path per query, not per row, `Feature` `Select`, `Effect` `Mask` where the member is transformed as well and `Allow` otherwise, `EntityType` the queried type's full name, and `FieldPath` the path through the rows, such as `B.C.D.E.Five`, or `Hidden` for a subtype's member at the root. Only a member its own `[DwAudit]` audits for `Select`, and only where the projection carries it, since a member the projection left out is not a read. A member the declared types hold within four segments is the gate's and is left to it, and so is a path the projection spells out however long it is, so neither is recorded twice. It is recorded in a dry run too, read only by a resolver that reads attributes, and a model that declares neither an audit for `Select` nor a transform anywhere pays for no second pass. At `DwCaps.MaxAuditEvents` it fails closed as the gate does and the rows are withheld: under `Strict` outside a dry run the clause's own refusal with `FieldPath` `"*"` — `FieldDeniedForSegment` inside a segment — and `CapExceeded` otherwise, whose `SourceOrigin` names the cap and the undrained buffer.

A buffer already holding `DwCaps.MaxAuditEvents` refuses the next audited use with `CapExceeded` — except under `Strict` outside a dry run, where it refuses with the clause's own field refusal instead (3.3.0). An unknown name is never audited and never reaches the cap, so answering with the cap's own code there would have told a caller that the name they guessed is a real field and an audited one.

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

`RedisPolicyStore.UpsertAsync` and `DeleteAsync` commit conditionally on the rule's owner entry (3.3.0). Where a rule lives is read before the transaction that moves or deletes it, so two writers of one rule could read the same answer: the slower one then cleaned up after a copy the faster had already moved and left that writer's copy behind, under a user nobody any longer wrote it for and with no owner entry pointing at it, which no later write or delete could find. The writer that loses the race gets the `InvalidOperationException` a failed commit always raised — its message ends *Another writer moved or removed the same rule in the meantime; write it again* — and should write again.

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
| Reversed by | holding the salt | reading the vault, and its key where it has one |
| A weak secret | brute-forced offline | only a vault key under 16 bytes, which is refused |
| Survives a restart | always | only with a durable vault |
| Discloses equality | yes | yes |

Three vaults ship, all held to one conformance suite. `InMemoryTokenVault` lives and dies with the
process, which is right for a test and wrong for any column compared across restarts.
`RedisTokenVault` and `EfTokenVault` keep the mapping outside the process, and each caches every
mapping it resolves — a token is written once and never rewritten, so a cached answer cannot go
stale.

**Give a durable vault a key (3.3.0).** A vault stores its mapping under the scope and a digest of
the value. Without a key that digest is a plain SHA-256, and a tokenized column is nearly always
drawn from a space small enough to hash whole — phone numbers, national identifiers, card numbers.
So a copy of the store, a backup or a replica or a dump, gives back every value in it, and with them
the value behind every token ever issued. Under a key held where the store is not, in configuration
or a secret manager, the digest is an HMAC-SHA256 and the store and the key have to be taken
together. Guard the store as you would guard the column it protects either way.

```csharp
new DwPolicyOptions
{
    TokenVault = new RedisTokenVault(redis, key)                      // 16 bytes or more
    // TokenVault = new EfTokenVault(() => new AppDbContext(opts), key)
}
```

The constructors that take no key are unchanged and unkeyed, and so is `DwToken.KeyFor(scope,
value)`. `DwToken.KeyFor(scope, value, key)` writes `hmac:{scope}:{64 lowercase hex}`, an HMAC-SHA256
under the key over the scope, one zero byte and the value, so one value tokenized in two scopes
shares no digest; `DwToken.RequireKey` refuses a key that is null or shorter than
`DwToken.MinimumKeyLength` (16) and returns a copy of it, and `DwToken.KeyedPrefix` is the `hmac:`
an operator can tell the two kinds of key apart by. `InMemoryTokenVault` draws a random 32-byte key
of its own per instance — nothing to configure, no API change — since its mappings die with the
process anyway.

**Adoption keeps every token already issued.** A keyed vault meeting a value with no keyed mapping
looks up the unkeyed mapping too, and the token found there is the one written under the keyed key,
so yesterday's export still lines up with today's. The unkeyed mapping stays until `retireUnkeyed`
is true, and a retiring vault deletes it the first time it meets the value, whether it wrote the
keyed mapping or found it. **Roll out in two steps: give every instance the key, then turn
`retireUnkeyed` on.** An instance still running without the key mints a *new* token for a value
whose unkeyed mapping is gone, and a value first met while keyed and unkeyed instances run side by
side can end up with two tokens. Unkeyed mappings of values never met again stay until an operator
deletes them — `HSCAN` the Redis token hash and delete the fields that do not match `hmac:*`, or
delete the rows of `DwPolicyTokens` whose `Key` does not start with `hmac:` — knowing such a value
gets a new token the next time it is met. Changing the key re-issues every token, unless unkeyed
mappings remain to adopt from.

The cost is small and there is no schema change. Redis reads both fields in one round trip, so a
value new to the store costs two round trips instead of one; EF Core costs one more read for a new
value, and in retire mode one more read per first-met value. A keyed key is at most 326 characters
against the 512 the `Key` column already holds.

Tokens are namespaced by the field's own path, so two columns holding the same value get different
tokens. Name a shared `TokenScope` when you want them to match:

```csharp
[DwMask(MaskStrategy.Tokenize, TokenScope = "person-identifier")]
public string NationalId { get; set; }
```

**What neither closes.** Anyone who can write a chosen value and read the column back masked learns
that value's output and can then recognise it in every other row. That is inherent in preserving
equality and no setting removes it. A field that cannot accept it wants `Fixed`, `Null`, or a denial.

**A value no path of the policy names.** The outbound walk transforms along the paths the policy names — the declared types, four segments deep — and a value can sit in the materialized rows where none of them goes: a `[DwMask]` member five segments down an included or in-memory graph, one only a subtype of the row's type declares (`Dog.Chip` on rows typed `Animal`, in memory or in a TPH hierarchy), one on an object a dictionary holds, and the far side of a cycle. Each came back exactly as stored, at the default caps, under `Strict`, with no `Selects`, with the navigation named whole in `Selects`, and in a dynamic projection holding a real object. Fixed (security) in 3.3.0: the rows are walked by run-time type as well, and a member that declares a transform attribute and was not transformed along a named path is transformed by its own attributes, exactly once — an object reached both ways is not transformed twice. Only members that declare a transform or an audit for `Select`, or that can lead to one, are read, so a navigation whose type can reach neither is never touched and a lazy loader behind it is not woken, and a model that declares neither anywhere pays for no second pass. The same pass reports each audited member it meets where the policy names no path to it, which the terminal records as a read (see [Auditing](#auditing)). The transform is the member's own attributes: no rule can speak to such a member, since no path names it, and a resolver built over no `AttributePolicyProvider` reads no attribute here either. It obeys `Selects` as the first pass does, runs in a dry run as transforms always have, and fails the query with `InvalidOperationException` for a transformed member with no setter. The trace records the path with its stages and the note `(declared on the member; no path of the policy names it)`. A member typed `object`, or a collection that is not generic, still says nothing about what it holds and is not read into.

**A query the caller runs.** `SelectDynamic`, `Group`, `FilterDynamic` and `Summary` on the guarded handle hand back a query the library never sees materialized, so they are refused with `TransformRequiresMaterialization` on a type whose values are transformed on the way out. Whether a type is one was read from the paths the policy names, so a type whose only transforms sit off them — on a member only a subtype declares, one five segments down, one of an object a dictionary holds — got the query and its rows exactly as stored. Since 3.3.0 the refusal asks what a row of the type can hold as well, any transform attribute anywhere in what the type can reach, which only a resolver that reads attributes is asked; with no named column to list it names the clause, `FieldPath` `"*"`, in both tiers, where under `Convenience` it otherwise lists the transformed columns. A type nothing transforms anywhere still gets its query. Materialize through `ToListDynamic` or `ToList(Summary)`, or leave the policy deliberately with `AsUnguardedQueryable()`.

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

The floor appends its own `Count` aggregate under the reserved alias `__dwGroupSize`, and refuses a summary that already uses that name with `GroupTooSmall`. The walk over `Having` that looks for it reads a null `Conditions` or `SubConditionGroups` as an empty list (3.3.0): a request body sending `"conditions": null` or `"subConditionGroups": null` overwrites the list's initializer, and such a summary used to fail guarded with a `NullReferenceException` wherever the floor is on, which is the default, though the same summary ran unguarded. It runs, and the floor still applies.

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
| `MaxNavigationDepth` | 4 | How deep a field path may reach. Also the depth the attribute walk reads to: raised above 4, a request can name a path no fragment of that walk reached, and the member at the end of it is read for its own attributes (3.3.0) |
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

A simulation, through `/simulate` or `PolicySimulator`, has no source, so it reads the type as a source it cannot see into. That shows in a clause that sends no `Selects`: every denial beneath a member counts, and the projection it shows keeps only the members that hold a value, a collection of values included. A guarded query keeps what its own source carries — over a projected row, the objects its initializer assigns; over an entity, its columns, owned and complex members, asking only about the denials whose value it loads; over rows in memory, values only. So the simulated clause can list fewer members than the query returns, and can show a projection an entity query does not need. For the same reason it cannot refuse a path no database can compute: that refusal is read from the model behind the source, which a simulation does not have, so a simulation shows such a request running where the strict query refuses it. See [A request that sends no Selects](#a-request-that-sends-no-selects).

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

#### Configuring twice

*(3.3.0)* The first call decides the posture. A second `DwPolicy.Configure` **asking for the posture
already in force does nothing and returns**; one asking for a different posture still throws
`InvalidOperationException`. A second `AddDwPolicies` binds and builds its options as ever, changes
no posture, and registers the one in force. The comparison happens inside the lock
that does the configuring, so a caller needs no lock and no `IsConfigured` check of its own — which
matters because that check is a check-then-act two hosts starting at once can both pass.

This is what an integration suite needs. Several `WebApplicationFactory<Program>` hosts run the same
composition root, and before 3.3.0 the second one threw, so every such suite wrote the check itself
and re-registered `DwPolicy.Options` by hand.

What counts as the same posture:

| Compared | Not compared |
|---|---|
| `Tier`, `DryRun`, `AuditRefusals`, and `IncludeTraceInResult` by the value that applies | `TokenVault` |
| `HashSalt`, `StoreFailure`, `MaxSnapshotAge`, `RefreshInterval` | `Services` |
| Every value on `Caps`, the floor that applies rather than whether it was written down | The provider *instances* |
| The exposed entity catalogue: the same types, every name each answers to, and the name each is reported under | |
| The provider *types*, in the order they were supplied | |

`IncludeTraceInResult` is compared the way the group floor is: it defaults to the tier's own answer, and the
tiers are equal by then, so a host writing that answer out and a host leaving it null hand a caller
the same result. A type exposed under two names is a different matter — it is reported under the last
name it was given, so two catalogues that resolve every name alike still answer a schema request
differently, and the second posture is refused.

The three on the right are objects a host builds for itself, and a second host builds its own, so
comparing them by reference would make every second call a refusal. They stay as the first call left
them: **a second host runs with the first host's vault, container and rule stores.** In one test
process that is what you want. Start a second host in production only if it is.

`AddDwPolicies` registers the posture in force rather than the instance it has just built, so
whatever resolves `DwPolicyOptions` reads what the query path reads. The options handed to a second
call are frozen too, so nothing goes on setting values that decide nothing.

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
| `CacheReflection` | Core reflection operations with caching. A lookup takes no lock and a hit allocates nothing (3.3.0): the configuration in force is read with one volatile read, where every lookup used to lock and copy it. `GetCacheConfigOptions()` still returns a copy |
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
| `InvalidField` | `ConditionMustHasValidFieldName` | Empty or invalid field name, and since 3.3.0 a `Selects` entry that is null or blank, where it used to be an `ArgumentNullException` from the name lookup. Under `ApplyPolicy` in the strict tier, outside a dry run, a name that matches nothing is refused as a `PolicyException` instead, like a denied field — see [Blocked-action semantics](#blocked-action-semantics) |
| `NullEntry(list)` | `ListOf[{list}]MustNotHasNullEntry` | A list of the request shape holds a null entry — `Conditions`, `SubConditionGroups`, `ConditionSets`, `Orders` or `AggregateBy`, spelled as the shape declares it. New in 3.3.0: such an entry used to surface as a `NullReferenceException` from wherever it was first touched |
| `StartsWithReservedName(path)` | `FieldPath[{path}]StartsWithReservedName` | A field path whose first segment is one of the expression parser's own words — `new`, `iif`, `np`, `isnull`, `is`, `as`, `cast`, `true`, `false`, `null`, whatever the letter case. Raised for every clause that takes a path, and for a `[DwAlias]` target. `LogicException.Subject` carries that first segment, trimmed. A `DefaultOrder` entry naming one is skipped like an unreadable entry, and reported by the startup scan. 3.1.0 |
| `InvalidValue` | `ConditionValuesAreNullOrWhiteSpace` | Defined and never thrown. A null value normalizes to `""` and is judged by the DataType like any other string |
| `RequiredValues` | `ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues` | In/NotIn with 0 values |
| `NotRequiredValues` | `ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues` | IsNull with values |
| `RequiredTwoValue` | `ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues` | Between without exactly 2 values |
| `RequiredOneValue(op)` | `ConditionWithOperator[{op}]MustHasOnlyOneValue` | Single-value operator with wrong count |
| `InvalidPageNumber` | `PageNumberMustBeGreaterThanZero` | PageNumber ≤ 0 |
| `InvalidPageSize` | `PageSizeMustBeGreaterThanZero` | PageSize ≤ 0 |
| `MustHaveFields` | `MustHasFields` | Empty fields list in Select |
| `InvalidFormat` | `InvalidFormat` | Value doesn't parse for declared DataType. For a number (3.3.0): not a literal the expression parser reads, or not one it can compare with the member the condition names. For a date: not ISO 8601, year-first, or a declared format |
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

These are numbered as this document numbers them. The website's [breaking-changes page](https://doc.dynamicwhere.com/docs/breaking-changes) carries the same points with its own numbering, which runs further, so follow a point by its title rather than by its number.

1. **Parameterless Constructor Required for Select Projection**
   `Select<T>(fields)` requires `T` to have a parameterless (default) constructor. If `T` does not have one — a positional record, most often — a `LogicException` is thrown whose `Message` is the stable code `SelectTypeMustHaveParameterlessConstructor` and whose `Subject` carries `typeof(T).Name`. Before 3.1.0 that message was an English sentence with the type name inside it. Most EF Core entity classes have parameterless constructors by default. A guarded query reaches the same refusal when a member carries `[DwNoSelect]`, because deny-select projects — since 3.2.0 whatever the member holds, and beneath another member when its value can reach the result (point 20).

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

   Changed in 3.3.0: under `LRU` a read refreshes the entry's last-access time once it is a second old rather than on every read. Eviction only asks which entries are oldest, and an entry read a moment ago is already among the newest; writing the time on every read put every thread reading the same few entries into one queue. `LFU` still counts every read. One million lookups of one cached member went from 152 ms to 35 ms on one thread and from 2,697 ms to 108 ms on eight, and from 167 MB allocated to 22 MB.

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
    Before 3.1.0 a guarded query refused a field name matching nothing on the type with `LogicException` `ConditionMustHasValidFieldName`, and a denied field with a `PolicyException` carrying its path and, where one source decided, its `RuleId` and `SourceOrigin`. The two answers let a caller list the columns they may not see, one guess at a time. Under `DwTier.Strict`, outside a dry run, an unknown name is now gated as a field denied for every feature and receives the refusal a `[DwDenied]` field receives in that clause — `FieldDeniedForWhere`, `FieldDeniedForSelect`, `FieldDeniedForOrder`, `FieldDeniedForGroup` or `FieldDeniedForAggregate`, and `FieldDeniedForSegment` anywhere in a segment — after the caps. Every refusal with one of those six codes carries `FieldPath` `"*"`, a null `RuleId` and a null `SourceOrigin`, whatever the field, and a `CapExceeded` refusal names no path either; since 3.3.0 the audit cap does not answer with that code under this tier at all, because only a real, audited field can reach it. The same tier closes the other ways to tell them apart: inside a segment every field refusal is `FieldDeniedForSegment`; a name padded with dots is normalized as a real path is; `MaxQueryCost` is checked after every field gate, so a `[DwCost]` weight cannot set a hidden field apart from a missing one; and `MissingContextValue` carries `FieldPath` `"*"` and no `SourceOrigin`. Code that caught `ConditionMustHasValidFieldName` from a strict guarded query, matched a clause's code inside a segment, or read `FieldPath`, `RuleId` or `SourceOrigin` off a strict refusal, reads `PolicyQueryable<T>.LastTrace` instead, which keeps the real path and reason, or records refusals with `DwPolicyOptions.AuditRefusals`. The convenience tier and dry runs are unchanged. See [Blocked-action semantics](#blocked-action-semantics).

19. **`MaxConditionValues` and `MaxAggregates` Refuse Guarded Requests 3.0 Ran**
    Two more caps new in 3.1.0. `DwCaps.MaxConditionValues` (default 1000) bounds the values one condition carries — the largest condition of the where clause, a summary's `Having` and every segment set is the one compared — because an `In` is one comparison per value and so could build a predicate of any size for the price of one condition and one field. `DwCaps.MaxAggregates` (default 50) bounds the `AggregateBy` entries of one summary, through the summary terminals and the composable `Group` and `Summary`; the group-size floor's own count is not counted. A guarded request over either is refused in both tiers with `PolicyException` `CapExceeded`, `FieldPath` `"*"` and `SourceOrigin` `"MaxConditionValues cap (1000), request had 1001"` or `"MaxAggregates cap (50), request had 51"`, unless the deployment raises the cap. Both refuse a value below 1, freeze with the posture, and bind from `Caps:MaxConditionValues` and `Caps:MaxAggregates`. An aggregate with no field, such as a `Count`, is now charged `DefaultFieldCost` toward `MaxQueryCost`, where it cost nothing, so a summary that sat just under its budget can be refused with `QueryCostExceeded`. Every count cap is now checked before any field name is resolved, so an oversized request that also names a field that does not exist is refused with `CapExceeded`, where 3.0.0 resolved names first and answered `ConditionMustHasValidFieldName`. Only `ApplyPolicy` enforces them: an unguarded query is not affected.

20. **A Guarded Query That Sends No `Selects` Keeps What the Source Carries**
    A request with no `Selects` returns whole rows, denied fields included, so a guarded query synthesizes a projection when a denied field could reach the result. 3.2.0 changed when it does so and what it keeps; the rules are under [A request that sends no Selects](#a-request-that-sends-no-selects).

    Fixed (security): a field denied only beneath a member, none at the top of `T`, synthesized nothing, so the whole row came back with the denied value in it — in a list or nested object of a row projected before `ApplyPolicy`, in a row held in memory, and in an entity's included, automatically included, lazily loaded or owned member — typed and dynamic, in both tiers, for a `Filter` and a `Segment`. Such a denial now synthesizes the projection whenever its value can reach the result: on an entity, beneath a column, an owned or complex member, or a navigation the query loads through `Include`, an automatic include or a lazy loader; on a projected row, beneath a member the initializer assigns; in memory, beneath any member. A denial beneath a navigation nothing loads never leaves the database, and the entity is read exactly as in 3.1.0.

    Fixed (security): what a query loads was read too narrowly. An include named from the root and reached through `Select(o => o.Customer)`, `SelectMany` or `Join`, a projection behind another `Select`, an initializer after a constructor with arguments, and a lazy loader the constructor takes and keeps in a field or a property of any name each loaded a denied value the gate read as unloaded. An injected `DbContext` or EF Core 7's asynchronous loader delegate did too, and so did a reshaping lambda that got its row from an application's method or from a captured query or object. An application's own collection class hid its own denied members, and a guarded query through a provider wrapping EF Core's, such as LinqKit's `AsExpandable`, ran tracking, so the context filled in navigations it already held and a masked value became a pending change. A field a subtype declares, a derived entity's or a subclass's held by a base-typed member, was not read at all, nor was a `[DwDenied]` on an override, on a member hidden with `new` or on an interface member's implementation, and under a `"*"` deny a path the walk never asked about was allowed. Each came back. Now every navigation counts as loaded on such a chain, the subtypes are read, and such a path is denied. When a type the model derives from `T`, or a loaded subclass of a row in memory, declares a denied field, the rows are projected to `T`, dropping a derived type's allowed fields too; over an abstract `T` the typed terminals fail with `SelectTypeMustHaveParameterlessConstructor` and the dynamic ones return its members.

    Fixed (security): a field denied at the top of `T` whose own type is not a simple value — a byte array, a list, an owned object, a JSON column — synthesized no projection either, so with nothing else denied the whole row came back with it.

    Changed: the projection kept simple fields only, so as soon as any field was denied, every nested object and list of a row projected before `ApplyPolicy` came back null or empty, and so did an entity's columns holding an object, its owned and complex members and its collections of simple values. A row a projection builds now keeps the members its initializer assigns; an entity keeps its mapped columns, converted and JSON ones included, except a converted one that can hold an object of any type, its owned and complex members, and every collection of simple values; rows in memory keep their values. A member holding an object is kept whole when nothing it can hold is denied, narrowed to the allowed fields where the core's narrowing translates, and otherwise left out whole, with a `Dropped` decision whose reason starts `left out whole`. An entity's navigations, included ones too, the objects of a row in memory, and a value EF Core does not map are left out; under `Convenience` name a navigation in `Selects` to get it narrowed, and under `Strict` name its allowed fields. A forced scope beneath a member asks for no projection on its own, and a projection needed for another reason leaves such a member out whole. A typed query needs `T` to have a public parameterless constructor for the projection, as it already did (point 1).

21. **`Selects` Naming a Member Is Gated Against Every Denial Beneath It**
    When `Selects` names a navigation with a denied field beneath it, the `Convenience` tier replaces the entry with the allowed fields beneath it, and the `Strict` tier refuses it. Since 3.2.0 the gate finds every denial beneath the member, and refuses, with `FieldDeniedForSelect`, a narrowing it cannot build as gated. See [A navigation named in Selects](#a-navigation-named-in-selects).

    Fixed (security): under the convenience tier, a navigation whose key (`Id`) is denied was narrowed to the allowed fields beneath it, and the core's typed projection, which adds the key of every nested node it builds, put the key back. Such a narrowing is refused in both tiers, as naming a sibling of the key already was. A navigation named through another, such as `Main.Lead`, now gates the key of `Main`, which the projection adds; it did not.

    Fixed (security): a member typed as a collection the core does not unwrap — `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `Collection<T>` or an application's own — returned every field beneath it, denied ones included, in both tiers. The projection gate read collections through a narrower list than the attribute walker and found nothing beneath the member. It now reads them as the walker does, and a narrowing the core cannot project is refused.

    Fixed (security): a member of a projected or in-memory row whose type holds a field denied for `Select` that no path names — deeper than four segments, or inside a framework generic such as `Dictionary<string, T>` — was returned whole. The strict tier refuses it now, and the convenience tier narrows it away; where it cannot be narrowed, both tiers refuse it. Denials beneath a named member are also read from the policy's own rules, so a denied property with no setter and a rule on a path reached through a cycle are found. A member that cannot be narrowed at all — a column, a complex property or a JSON-stored member, a member of a row in memory, or one a projection builds some way the core cannot narrow — is refused in both tiers when something beneath it is denied. A request that sends no `Selects` is not refused for such a member: its synthesized projection narrows it or leaves it out whole.

22. **`DefaultOrder` Reaches a Projection That Builds `T`**
    In 3.1.0 a `Select` anywhere in the chain kept a guarded query in its own order. Since 3.2.0 only the outermost `Select` counts, and when it builds `T` in an object initializer that assigns every field the default names a column, at every level of a nested path, the default applies: `db.Tickets.Select(t => new TicketRow { Id = t.Id, CreatedAt = t.CreatedAt, Title = t.Title }).ApplyPolicy(caller)` on a `TicketRow` declaring `"CreatedAt desc, Id"` was unordered and is now ordered. A column is a member the EF Core model maps on the entity the `Select` reads, read directly, through reference navigations or through `EF.Property`. A value the projection computes, by any method or operator, a member the model does not map, a constructor with arguments, a default field the initializer does not assign, or a nested path through anything but an initializer still leaves the query in its own order. A `Select`, or a `Filter` with `Selects`, composed on the guarded handle keeps the rest of the chain unordered, and a composed `Filter` that sent orders gets no default later in the chain, even when the policy dropped every one of them, as a composed `Order` already did not. See [Default order](#default-order).

23. **Every Async Terminal Takes a `CancellationToken`**
    Since 3.2.0 `ToListAsync` and `ToListAsyncDynamic` with a `Filter`, `ToListAsync` with a `Summary`, and `ToListAsync` with a `Segment` have overloads that take a `CancellationToken`, guarded and unguarded, and the token reaches the count and the read. The 3.1 signatures are unchanged, so code compiled against 3.1 still binds, but `ToListAsync(filter, default)`, `ToListAsyncDynamic(filter, default)` and `ToListAsync(summary, default)` no longer compile: `default` fits both `getQueryString` and the token (CS0121). Write `false`, a token, or a named argument. A reflection lookup of `ToListAsyncDynamic` by name alone now finds three methods where it found one, and one of `ToListAsync` finds more than it did. See [Cancellation](#cancellation).

    Changed: `ToListAsyncDynamic` and the async `Summary` read through EF Core's `ToListAsync` instead of Dynamic LINQ's `ToDynamicListAsync`, which had no token to pass on, and the async `Summary` counts through `CountAsync` where it counted synchronously. So on an EF Core query a canceled token now reaches the database. The rows and the counts are the same. A provider that is not EF Core's keeps Dynamic LINQ's read, on the calling thread.

24. **A Type in a Namespace That Starts with `System` Is Policed**
    The attribute walker does not descend into the framework's own types, which carry no policy attributes. Until 3.2.0 it took any namespace whose name started with `System` for the framework's, so an application namespace such as `SystemsCorp.Payroll` or `SystemX.Domain` got no policy beneath its types, and a `[DwDenied]` field on such a type, reached through a member, was returned, filterable and sortable. Fixed (security): only `System` and the namespaces beneath it are the framework's now, so a guarded request that filtered on, sorted by or selected such a field is refused or dropped, as for any denied field.

25. **Under `Strict`, a Path the Query Cannot Compute Is Refused**
    Since 3.3.0 a path whose leaf is a member no database can produce — a getter over columns, such as `LocalizedText.IsEmpty`, or an unmapped getter on the entity — is refused with the clause's own code and `FieldPath` `"*"`, as an unknown name is. Until 3.3.0 the package accepted it and EF Core threw `InvalidOperationException`, which reached a caller as a five-hundred where the tier promises a refusal. It applies where the whole set of members a container can produce is known: an entity's model, and the initializers of a projection composed before `ApplyPolicy`, including a member that projection copies from the entity. Rows in memory, a framework member the provider translates such as `Length` or `Year`, anything beneath a column, a query a provider in front of EF Core translates — an expression expander, a decompiler — the convenience tier and a dry run are all unchanged. A member a custom EF Core translator computes, through a member translator plugin or a replaced query preprocessor, is refused with the rest: map it, or filter on the columns beneath it. See [Blocked-action semantics](#blocked-action-semantics).

26. **`LastTrace` Is Set Before a Request Is Sanitized**
    Since 3.3.0 `PolicyQueryable<T>.LastTrace` carries the trace of a request that was refused. It used to be assigned after sanitizing returned, so a refusal left it holding the previous request's trace, or null on the first. A strict refusal names no field on purpose, and the trace is where the real path and reason live, so this is what makes one readable. Code that read `LastTrace` after catching a `PolicyException` and expected the earlier request's trace reads this request's now.

27. **Four More Refusals Name the Clause Under `Strict`**
    A strict refusal names no field, and four did. `AmbiguousFieldName` told a caller that the name they wrote matches more than one field, which is to say at least one; it is now refused as an unknown name is, with the ambiguity kept in the trace for the operator who has to fix the aliases. `AmbiguousGroupKey` reported the grouping key's canonical path — the column behind whatever alias the caller wrote — and an origin saying its values are transformed; it now reports `"*"` and no origin. `TransformRequiresMaterialization` listed every transformed column on the type — masked, generalized, truncated or formatted — to a caller who named none of them, and now names the clause while keeping its origin, which names the method and what to call instead rather than any field. `MissingHashSalt` and `MissingTokenVault` named the masked field a deployment forgot to configure for, and now report `"*"` with no origin. All four are unchanged under `Convenience` and in a dry run, whichever switch declares it — the posture's or the caller's — where the tier names fields anyway. The refusal audit still records the real field: `AuditRefusals` writes the path the refusal was about, as it does for every refusal whose caller-facing path is `"*"`. Code switching on `AmbiguousFieldName` under `Strict`, or reading `FieldPath` off any of the four, sees the change.

28. **`[DwAudit]` Records a Read the Request Did Not Name**
    A request that sends no `Selects` receives the row, and until 3.3.0 only a field it spelled out was recorded — so that caller read every audited member with nothing written down, one token past a control whose purpose is to answer who read a field. Every audited member a projection the caller did not name hands back is now recorded for `Select`: what the synthesized projection keeps where one is built, and every member the caller may select where none is. One event per query rather than per row, and only for a field `[DwAudit]` names. A deployment already running the control sees more events, and `DwCaps.MaxAuditEvents`, which refuses rather than dropping a record, can be reached by traffic that did not reach it before: raise the cap, or drain per request with `app.UseDwPolicyAudit()`.

29. **The Audit Cap Refuses Like Any Other Field, Under `Strict`**
    An audited field records one event per use, and the query is refused rather than the record dropped when `DwCaps.MaxAuditEvents` is reached. Until 3.3.0 that refusal carried `CapExceeded` and a `SourceOrigin` naming the cap, while a name matching nothing carried the ordinary field refusal and no origin — and an unknown name is never audited, so the difference told a caller which names are real and audited. Under `Strict`, outside a dry run, the cap now refuses with the clause's own code, `FieldPath` `"*"` and no origin. The request still fails, so the buffer still fails closed, and the trace still records which refusal it was. `Convenience` and a dry run still answer `CapExceeded`. Code switching on `CapExceeded` under `Strict` sees the change.

30. **`Configure` Takes the Same Posture Twice**
    Since 3.3.0 a second `DwPolicy.Configure` or `AddDwPolicies` asking for the posture already in force returns instead of throwing; a different posture still throws. Code that relied on the second call throwing — a test asserting it, or a `try`/`catch` around a second registration — no longer sees the exception. `AddDwPolicies` also registers the posture in force rather than the instance it built, so a container resolving `DwPolicyOptions` after a second registration gets the first one's. The token vault, the service provider and the provider instances are not compared and are not replaced. See [Configuring twice](#configuring-twice).


31. **A Path Beneath a Framework-Typed Member Takes That Member's Policy**
    The attribute walk descends into an application's own types and nowhere else, so no attribute can be placed beneath a member the framework declares the type of — `Salary.Value` and `Salary.HasValue` on a `decimal?`, `Secret.Length` on a `string`, `Born.Year` or `Born.Date.Year` on a `DateTime`, `Bag.Count` on a dictionary, `Lines.Count` on an application's own collection class. The pipeline validates each and the provider translates each, and no fragment named them, so they resolved as allowed. Fixed (security) in 3.3.0, in both tiers: until then a `[DwDenied] decimal?` was filtered on, sorted by, grouped by with its values as the group keys, aggregated as `MAX(Salary.Value)` and handed back by a dynamic projection under `Strict`; a transformed member gave its stored value the same way, an audited one was read with nothing recorded, a weighted one cost the default, and an operator restriction did not hold. Such a path now takes every fragment of the member it reads, whichever provider supplied it: the deny effects per feature, the `[DwOperators]` restriction (intersected), the `[DwCost]` weight and the audited features — never the alias, the required filter, the forced scope or the descriptive facts, which are about the member itself. A rule naming the sub-path still applies alongside. One feature is one feature: `[DwNoWhere] Born` refuses `WHERE Born.Year` and still allows `GROUP BY Born.Year`, and `Name.Length` on an undenied member still runs. Where the member is transformed, `Select`, `Group` and `Aggregate` on the path are refused, because there is no member beneath it to apply the chain to. A member only a subtype of the navigated type declares is not such a path. See [Attribute reference](#attribute-reference).

32. **A Transformed Member No Path Reaches Is Transformed**
    The outbound walk transforms along the paths the policy names — the declared types, four segments deep — and a value can sit in the materialized rows where none of them goes: a `[DwMask]` member five segments down an included or in-memory graph, one only a subtype of the row's type declares, one on an object a dictionary holds, one on the far side of a cycle. Each came back exactly as stored, in default configuration, at the default caps, under `Strict`. Fixed (security) in 3.3.0: the rows are walked by run-time type as well, and a member that declares a transform attribute and was not transformed along a named path is transformed by its own attributes, exactly once. Results that used to carry stored values now carry transformed ones, and a transformed member with no setter there now fails the query with `InvalidOperationException`, as one along a named path always has — give the member a setter, or project into a type that has one. Only members that declare a transform or can lead to one are read, so a model with no transform attribute anywhere pays nothing and a navigation whose type can reach no transform is never touched. See [Hiding a value you still want to group by](#hiding-a-value-you-still-want-to-group-by).

33. **A Forced Scope on a Type First Met at the Depth Limit Applies**
    `[DwForceWhere]`, `[DwRequireWhere]` and `[DwAlias]` are left out around a cycle, where they are meaningless on a type reached from itself. The attribute walk returned at its depth limit with the type still marked as being inside it, so a type *first* met at the fourth segment read as a cycle wherever it was met again in the same walk, and all three were dropped from a shorter path reaching that type directly — which of two members was declared first decided whether a forced tenant scope applied. Fixed (security) in 3.3.0: the three apply on every path within four segments that is not around a cycle, as the documentation always said. A query that ran unscoped is now scoped and returns fewer rows, a `[DwRequireWhere]` that was never demanded may now be demanded with `RequiredFilterMissing`, and a member reachable only by its real path now also answers to its alias.

34. **Paths Past Four Segments When `MaxNavigationDepth` Is Raised**
    `Caps.MaxNavigationDepth` defaults to 4, the depth the attribute walk reads to, and a host may raise it. A request could then name a path of five or more segments that no attribute fragment reached, so a `[DwDenied]` member at segment five was filtered on, grouped by and returned under `Strict`. Default configuration was never exposed to this one. Since 3.3.0 the attributes of the member at the end of such a path are read directly — the deny family, `[DwOperators]`, the transform stages, `[DwCost]`, `[DwAudit]`, `[DwDescribe]` and allowed values — by any resolver that reads attributes, which every resolver `DwPolicy.Configure` builds does. What is declared about the queried entity itself is not read there, as it is not around a cycle: `[DwAlias]`, `[DwRequireWhere]`, `[DwForceWhere]`. A transformed member there is still a member, so `Selects` naming it returns it transformed, in a typed projection and in a generated row alike; only a grouping key and an aggregated field are refused, with `FieldDeniedForGroup` and `FieldDeniedForAggregate`, because a summary's own transform finds a generated row's columns by the type's list and that list stops at four segments. Filtering and ordering run on the stored value, as at any depth.

35. **A Page Number Whose Offset Passes `Int32` Is an Empty Page**
    The offset a page skips, `(PageNumber - 1) * PageSize`, was worked out in 32 bits, and for a large enough page number the product wrapped: a negative offset is an error on SQL Server and PostgreSQL, so the request became a five-hundred, and the first page again on SQLite and in memory, so a page far past the last row returned rows. Since 3.3.0 it is worked out in 64 bits and held to `int.MaxValue`, in `Page` and in the three summary methods, guarded or not, and a page past the last row is an empty page however far past it is. The policy layer caps `PageSize` through `MaxPageSize` and never `PageNumber`, so a guarded query took the same path. Code that read the five-hundred as the signal for an out-of-range page now gets an empty page.

36. **A Query You Run Yourself Is Refused Where Only an Unnamed Member Is Transformed**
    `SelectDynamic`, `Group`, `FilterDynamic` and `Summary` on the guarded handle hand back a query the library never sees materialized, so they are refused with `TransformRequiresMaterialization` on a type whose values are transformed on the way out. Whether a type is one was read from the paths the policy names, so a type whose only transforms sit off them — on a member only a subtype declares, one five segments down, one of an object a dictionary holds — got the query, and its rows exactly as stored: the same gap point 32 closed for the terminals, one method call away from them. Fixed (security) in 3.3.0: the refusal asks what a row of the type can hold as well, which only a resolver that reads attributes is asked, and with no named column to list it names the clause — `FieldPath` `"*"` in both tiers, where under `Convenience` it otherwise lists the transformed columns. The origin, which names the method and what to call instead, is unchanged. A type nothing transforms anywhere still gets its query; a caller that composed one of the four on such a type materializes through `ToListDynamic` or `ToList(Summary)`, or leaves the policy deliberately with `AsUnguardedQueryable()`.

37. **`[DwAudit]` Records a Member No Path Names**
    Point 28 closed the read a request did not spell out; this closes the read the policy has no path for at all. The gate records a use by path, before the query runs, and a member only a subtype of the row's type declares, or one past the four segments the attribute walk reads, has no path it could ask about — so, handed back inside a row returned whole or a navigation kept whole, it was read with nothing written down. In a probe with four audited members, two were recorded. Fixed (security) in 3.3.0, in default configuration and both tiers: the outbound walk's second pass reports each audited member it meets where no path names it, and the terminal records it — one `DwAuditEvent` per path per query, not per row, `Feature` `Select`, `Effect` `Mask` where the member is transformed as well and `Allow` otherwise, and `FieldPath` the path through the rows. Only a member its own `[DwAudit]` audits for `Select`, and only where the projection carries it; a member the declared types hold within four segments is the gate's, and so is a path the projection spells out however long it is, so neither is recorded twice. Recorded in a dry run too, and read only by a resolver that reads attributes. At `DwCaps.MaxAuditEvents` it fails closed as the gate does and the rows are withheld: under `Strict` outside a dry run the clause's own refusal with `FieldPath` `"*"`, `FieldDeniedForSegment` inside a segment, and `CapExceeded` otherwise. A deployment already running the control sees more events for such models, and the cap can be reached by traffic that did not reach it before: raise it, or drain per request with `app.UseDwPolicyAudit()`.

38. **A `Number` Value Is Read the Way the Expression Parser Reads It**
    The predicate builder writes a `DataType.Number` value into the generated expression unquoted, exactly as sent, and validation checked it with `byte`/`short`/`int`/`long`/`float`/`double`/`decimal` `TryParse` in the host's culture. The two disagreed. `"1,000"`, `"5-"`, `"+5"`, `".5"`, `"5."`, `"-.5"`, `"1.e5"`, `"NaN"`, `"Infinity"`, `"-Infinity"` and an integer past `UInt64` — or below `Int64` when negative — all passed validation and then threw `System.Linq.Dynamic.Core.Exceptions.ParseException` when the query was built, which a host maps to a server error; `"1,5"` passed on a German host and was refused on an English one; and `"NaN"` and `"Infinity"` were written into the expression as identifiers, so on a type with a member of that name the condition compared two columns instead of filtering.

    A value is read in two steps since 3.3.0. First the parser's own grammar, in the invariant culture and ASCII digits only: optional white space, an optional minus, digits, an optional fraction — a point with a digit on both sides — and an optional exponent. No leading plus, no thousands separator, no trailing sign, no parentheses, no `NaN` and no `Infinity`; an integer must fit `UInt64`, or `Int64` when negative, while a real has no bound, so `1e400` still reads as infinity. A suffix (`5L`, `5m`), hex and `- 5` are refused as they always were, though the parser would read them: nothing is accepted now that was not accepted before. Then, in a `Where` condition and for the operators that write the value into a comparison — `Equal`, `NotEqual`, `In`, `NotIn`, the four orderings, `Between` and `NotBetween` — the literal has to compare with the member the condition names, which the parser itself is asked, against the member's declared type. Refused there: a literal written with a point and no exponent (`1.5`) on a **nullable** integral member, where a non-nullable `int` still takes it; an exponent form (`1e5`, `1E-7`) on a `decimal` or `decimal?`, and a real with more digits than a `decimal` holds; an integer above `Int64.MaxValue` on a signed integral member, since such a literal reads as a `ulong` which none of them converts to; a negative number on a `ulong` or `ulong?`; any number on a `string`, `bool`, `Guid`, `DateTime` or `char` member, or on a collection of simple values such as `List<int>`; and a nullable enum under an ordering operator, where equality still works. A `Having` condition reads the grammar and stops, since an alias has no member type to ask about.

    Every refusal is a `LogicException` with `InvalidFormat`, the same in both policy tiers, where a denied field is still refused by the gate before any value is read. Nothing that ran before is refused now: every value refused is one the parser refused. **Who is affected:** an endpoint that mapped `ParseException` to a five-hundred now gets a `LogicException` and a four-hundred, which is what it always should have been, and a client sending a locale-formatted number is refused on every host instead of working on some. A number a C# caller puts in `Values` is still written in the invariant culture and is unaffected, except that `double.NaN` is now `InvalidFormat`. JavaScript's `JSON.stringify(0.0000001)` is `1e-7`, which a `decimal` member refuses; send `"0.0000001"`.

39. **A `null` Entry in a Request's List Is a Malformed Request**
    A request body can say `"conditions": [null]`, `"subConditionGroups": [null]`, `"conditionSets": [null]`, `"orders": [null]`, `"aggregateBy": [null]` or `"selects": [null]`. Nothing read a list expecting that, so the null surfaced wherever it was first touched: a `NullReferenceException` from the sort-order check, from the ordering, or — under a policy — from inside the copy the sanitizer takes before it reads anything; and an `ArgumentNullException` for a null aggregate (parameter `"aggregate"`), a null summary order (parameter `"order"`) and, from the name lookup, a null or blank `Selects` entry (parameter `"name"`). A host maps those to a server error, for a request that was simply malformed.

    Since 3.3.0 each is a `LogicException`: `ListOf[Conditions]MustNotHasNullEntry`, `ListOf[SubConditionGroups]MustNotHasNullEntry`, `ListOf[ConditionSets]MustNotHasNullEntry`, `ListOf[Orders]MustNotHasNullEntry` and `ListOf[AggregateBy]MustNotHasNullEntry`. A `Selects` entry that is null **or** blank — empty or white space — is `ConditionMustHasValidFieldName`, the refusal a null or blank `GroupBy.Fields` entry has always had. The walk runs in every method that takes a shape, before anything else reads the lists, with or without a policy, in both tiers, sync and async; under `ApplyPolicy` it runs at the top of the sanitizer, before the caps and before the gate, because it is about the request's shape and not a policy decision. A list that is itself null still means what it meant, a `ConditionSet` whose `ConditionGroup` is null is still an `ArgumentNullException` as is a null `Summary.GroupBy`, and a null element inside `Condition.Values` still reads as the empty string. `Filter.Clone()`, `Segment.Clone()` and `Summary.Clone()` copy a null entry as a null entry instead of throwing, so the refusal belongs to the method that runs the request.

    **Who is affected:** any endpoint binding a request body it does not validate itself. Such a body used to produce a five-hundred and now produces a `LogicException`, which middleware written for this library already maps to a four-hundred. Code matching on `NullReferenceException`, or on the `ArgumentNullException` parameter names `"name"`, `"order"` or `"aggregate"`, to detect this needs updating.

---

## License

**MIT** — Free Forever. **Copyright © 2023-2026 Sajjad H. Al-Khafaji**

Free for commercial and personal use, forever. No license acceptance required.

Repository: [https://github.com/Sajadh92/DynamicWhere.ex](https://github.com/Sajadh92/DynamicWhere.ex)