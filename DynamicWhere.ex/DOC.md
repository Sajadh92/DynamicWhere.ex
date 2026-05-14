# DynamicWhere.ex

**Version:** 2.1.0 &nbsp;|&nbsp; **Target Framework:** .NET 6+ &nbsp;|&nbsp; **License:** Free Forever

> A powerful and versatile library for dynamically creating complex filter, sort, paginate, group, aggregate, and set-operation expressions in Entity Framework Core applications — all driven by simple JSON objects from any front-end or API consumer.

---

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Enums Reference](#enums-reference)
4. [Classes Reference](#classes-reference)
5. [Extension Methods Reference](#extension-methods-reference)
6. [Validation Rules](#validation-rules)
7. [JSON Examples for Every Extension Method](#json-examples-for-every-extension-method)
8. [Reflection Cache & Optimization](#reflection-cache--optimization)
9. [Cache Configuration Presets](#cache-configuration-presets)
10. [Error Codes Reference](#error-codes-reference)
11. [Breaking Changes & Known Limitations](#breaking-changes--known-limitations)

---

## Installation

```bash
dotnet add package DynamicWhere.ex --version 2.1.0
```

**Dependencies:**
| Package | Version |
|---------|---------|
| `Microsoft.EntityFrameworkCore` | 6.0.22 |
| `System.Linq.Dynamic.Core` | 1.6.7 |

---

## Quick Start

```csharp
using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
| `DateTime` | Full timestamp | `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `IsNull`, `IsNotNull` |
| `Date` | Date-only (compared via `.Date`) | Same as `DateTime` (compares `.Date` part only) |
| `Enum` | Enum stored as string | `Equal`, `NotEqual`, `Contains`, `NotContains`, `StartsWith`, `EndsWith`, `NotStartsWith`, `NotEndsWith`, `In`, `NotIn`, `IsNull`, `IsNotNull` |

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

Set operation applied between `ConditionSet` results in a `Segment`.

| Value | Description |
|-------|-------------|
| `Union` | Combines both sets (SQL `UNION`) |
| `Intersect` | Keeps only common items |
| `Except` | Removes items found in the second set |

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
| `FirstOrDefault` | First value | Required | No |
| `LastOrDefault` | Last value | Required | No |

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
| numeric / `IFormattable` | `InvariantCulture` formatting |
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
| `Alias` | `string?` | Name of the result column (must not conflict with `GroupBy.Fields`, no dots) |
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
| `PageCount` | `int` | Total pages |
| `TotalCount` | `int` | Total matching records |
| `Data` | `List<T>` | The result entities |
| `QueryString` | `string?` | Generated SQL (when `getQueryString: true`) |

#### `SegmentResult<T>`

Inherits all properties from `FilterResult<T>`. Returned by segment operations.

#### `SummaryResult`

| Property | Type | Description |
|----------|------|-------------|
| `PageNumber` | `int` | Current page (0 when no pagination) |
| `PageSize` | `int` | Page size (0 when no pagination) |
| `PageCount` | `int` | Total pages |
| `TotalCount` | `int` | Total grouped records |
| `Data` | `List<dynamic>` | Dynamic objects with group keys + aggregation values |
| `QueryString` | `string?` | Generated SQL (when `getQueryString: true`) |

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

Async-only segment operation. Executes each `ConditionSet` independently, then applies set operations (`Union` / `Intersect` / `Except`), followed by ordering and pagination.

**Returns:** `Task<SegmentResult<T>>`

---

## Validation Rules

### Condition Validation Rules

| Rule | Error Code |
|------|------------|
| `Field` must be non-empty and exist on `T` | `InvalidField` |
| `Between` / `NotBetween` require exactly 2 values | `RequiredTwoValue` |
| `In` / `IIn` / `NotIn` / `INotIn` require 1+ values | `RequiredValues` |
| `IsNull` / `IsNotNull` require 0 values | `NotRequiredValues` |
| All other operators require exactly 1 value | `RequiredOneValue({Operator})` |
| Values must not be null/whitespace | `InvalidValue` |
| `Guid` values must parse as `Guid` | `InvalidFormat` |
| `Number` values must parse as a numeric type | `InvalidFormat` |
| `Boolean` values must parse as `bool` | `InvalidFormat` |
| `Date` / `DateTime` values must parse as `DateTime` | `InvalidFormat` |

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
| Fields cannot be collection types | `GroupByFieldCannotBeCollectionType` |
| Aggregation alias must not be empty and must not contain dots | `InvalidAlias` |
| Aggregation aliases must be unique | `AggregationAliasesMustBeUnique` |
| Aggregation alias cannot match a GroupBy field | `AggregationAliasCannotBeGroupByField({alias})` |
| Aggregation field must be a simple type | `AggregationFieldMustBeSimpleType` |
| Aggregation field cannot be a collection | `AggregationFieldCannotBeCollectionType` |
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
    { "id": 7, "name": "Laptop Pro", "price": 1299.99, "category": { "name": "Electronics" } }
  ],
  "queryString": null
}
```

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
    { "id": 1, "name": "Adapter Cable", "price": 9.99 }
  ],
  "queryString": null
}
```

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

All validation errors throw `LogicException` (inherits `Exception`) with one of the following messages:

| Error Code | Message | When |
|------------|---------|------|
| `SetsUniqueSort` | `ListOfConditionsSetsMustHasUniqueSortValue` | Duplicate Sort in ConditionSets |
| `ConditionsUniqueSort` | `AnyListOfConditionsMustHasUniqueSortValue` | Duplicate Sort in Conditions |
| `SubConditionsGroupsUniqueSort` | `AnyListOfSubConditionsGroupsMustHasUniqueSortValue` | Duplicate Sort in SubConditionGroups |
| `RequiredIntersection` | `ConditionsSetOfIndex[1-N]MustHasIntersection` | Missing Intersection on set index 1+ |
| `InvalidField` | `ConditionMustHasValidFieldName` | Empty or invalid field name |
| `InvalidValue` | `ConditionValuesAreNullOrWhiteSpace` | Null/whitespace value |
| `RequiredValues` | `ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues` | In/NotIn with 0 values |
| `NotRequiredValues` | `ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues` | IsNull with values |
| `RequiredTwoValue` | `ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues` | Between without exactly 2 values |
| `RequiredOneValue(op)` | `ConditionWithOperator[{op}]MustHasOnlyOneValue` | Single-value operator with wrong count |
| `InvalidPageNumber` | `PageNumberMustBeGreaterThanZero` | PageNumber ≤ 0 |
| `InvalidPageSize` | `PageSizeMustBeGreaterThanZero` | PageSize ≤ 0 |
| `MustHaveFields` | `MustHasFields` | Empty fields list in Select |
| `InvalidFormat` | `InvalidFormat` | Value doesn't parse for declared DataType |
| `InvalidAlias` | `AggregationMustHasValidAlias` | Empty or dotted alias |
| `GroupByMustHaveFields` | `GroupByMustHasAtLeastOneField` | GroupBy with no fields |
| `GroupByFieldsMustBeUnique` | `GroupByFieldsMustBeUnique` | Duplicate GroupBy fields |
| `GroupByFieldCannotBeComplexType` | `GroupByFieldCannotBeComplexType` | Non-simple GroupBy field |
| `GroupByFieldCannotBeCollection` | `GroupByFieldCannotBeCollectionType` | Collection GroupBy field |
| `AggregationFieldMustBeSimpleType` | `AggregationFieldMustBeSimpleType` | Complex aggregation field |
| `AggregationFieldCannotBeCollection` | `AggregationFieldCannotBeCollectionType` | Collection aggregation field |
| `AggregationAliasesMustBeUnique` | `AggregationAliasesMustBeUnique` | Duplicate aliases |
| `AggregationAliasCannotBeGroupByField(alias)` | `AggregationAlias[{alias}]CannotBeUsedInGroupByFields` | Alias clashes with field |
| `UnsupportedAggregatorForType(agg, type)` | `Aggregator[{agg}]IsNotSupportedForFieldType[{type}]` | Invalid aggregator for type |
| `SummaryOrderFieldMustExistInGroupByOrAggregate(f)` | `SummaryOrderField[{f}]MustExistInGroupByFieldsOrAggregateByAliases` | Order on non-grouped field |
| `HavingFieldMustExistInAggregateByAlias(f)` | `HavingField[{f}]MustExistInAggregateByAliases` | Having references unknown alias |

---

## Breaking Changes & Known Limitations

### ⚠️ Breaking Points

1. **Parameterless Constructor Required for Select Projection**
   `Select<T>(fields)` requires `T` to have a parameterless (default) constructor. If `T` does not have one, a `LogicException` is thrown. Most EF Core entity classes have parameterless constructors by default.

2. **Segment Operations are Async-Only**
   `ToListAsync<T>(Segment)` is the only entry point for segment queries. There is no synchronous `ToList<T>(Segment)` variant. Each `ConditionSet` is materialized independently into memory, then set operations are performed in-memory.

3. **Case-Insensitive Operators use `.ToLower()`**
   All `I*` operators (e.g., `IContains`, `IEqual`) normalize both sides via `.ToLower()`. This works correctly with SQL Server (`COLLATE` is typically case-insensitive), but be aware of potential performance or behavior differences on case-sensitive database collations (e.g., PostgreSQL with `C` locale).

4. **Enum Filtering Requires String Storage**
   The `Enum` data type assumes enum values are stored as strings (not integers) in the database. If your database stores enums as integers, use `DataType.Number` instead.

5. **Having Clause Fields Reference Aliases, Not Entity Properties**
   In a `Summary`, the `Having.ConditionGroup.Conditions[].Field` must match an `AggregateBy.Alias`, not an entity property path.

6. **GroupBy Flattens Dotted Field Names in Results**
   Dotted `GroupBy` fields (e.g., `Category.Name`) produce flattened alias keys in the dynamic result objects (e.g., `CategoryName`). Order fields in `Summary.Orders` should use the dotted form; the library handles alias mapping internally.

7. **Collection Navigation Auto-Wraps with `.Any()`**
   When a condition's `Field` path traverses a collection property, the library automatically inserts `.Any()` lambdas. This means the filter checks if **any** item in the collection matches — there is no built-in `.All()` support.

8. **Thread-Safe Cache, But Configuration Changes are Eventually Consistent**
   `CacheExpose.Configure()` is thread-safe, but already-in-progress operations may use the previous configuration until they complete.

9. **`getQueryString` Parameter Requires EF Core Provider**
   Passing `getQueryString: true` to `ToList` / `ToListAsync` calls `.ToQueryString()` which requires an active EF Core database provider. It will fail on pure in-memory `IEnumerable<T>` calls (use the `IEnumerable` overloads which internally call `AsQueryable()` first, but `ToQueryString()` may not be supported).

10. **`SelectDynamic` / `FilterDynamic` / `ToListDynamic` / `ToListAsyncDynamic` Return Non-Generic Types**
    These methods return `IQueryable` or `FilterResult<dynamic>` instead of the strongly-typed equivalents. Downstream code must work with `dynamic` objects. Property names in the dynamic result follow these rules:
    - **Non-dotted paths** (`Name`, `Category`, `OrderItems`, …) are projected as-is — access them by their exact field name at runtime.
    - **Dotted paths through reference navigations** (e.g., `Category.Name`) produce **nested dynamic objects** reflecting the navigation hierarchy — access them as `result.Category.Name`, not as a flat `CategoryName`.
    - **Dotted paths through collection navigations** (e.g., `Category.Vendors.Id`) generate a `Select` lambda per collection segment — the result is a nested collection of dynamic objects accessible as `result.Category.Vendors[0].Id`.
    - **Multiple dotted fields** sharing the same root segment (e.g., `Category.Name` + `Category.Id`) are merged into a single nested object: `result.Category.Name` and `result.Category.Id`.
    - **Mixed whole-navigation + sub-field paths**: when both `"Category"` and `"Category.Name"` are requested, the sub-field projection takes precedence and `"Category"` is silently dropped.

11. **All Filter Extensions Apply Order and Page Before the Select Projection**
    All Filter extensions — both typed (`Filter<T>`, `ToList<T>(Filter)`, `ToListAsync<T>(Filter)`) and dynamic (`FilterDynamic<T>`, `ToListDynamic<T>`, `ToListAsyncDynamic<T>`) — apply ordering and pagination on the typed `IQueryable<T>` **before** the select projection. This ensures that field names referenced in `orders` always resolve against the original entity type `T`, regardless of which fields are projected.

---

## License

Free Forever — **Copyright © Sajjad H. Al-Khafaji**

Repository: [https://github.com/Sajadh92/DynamicWhere.ex](https://github.com/Sajadh92/DynamicWhere.ex)