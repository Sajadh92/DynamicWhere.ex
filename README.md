# DynamicWhere.ex

## Description

**DynamicWhere.ex** is a powerful and versatile library for dynamically creating complex filter expressions in Entity Framework Core applications. This library enhances your EF Core queries by providing support for creating dynamic filters using objects, making it easier to generate complex queries for your data.

With **DynamicWhere.ex**, you can easily define and apply conditions, logical connectors, and intersections to filter data from your database. It offers a flexible way to construct dynamic filter expressions for various data types, including text, numbers, booleans, and date-time values, using a wide range of logical operators.

## Installation

You can install the library via [NuGet](https://www.nuget.org/packages/DynamicWhere.ex/). Run the following command in your project's NuGet Package Manager console:

```bash
Install-Package DynamicWhere.ex
```

## Getting Started

Getting started with the library is straightforward. Simply install it via NuGet, and you're ready to create dynamic filter expressions for your Entity Framework Core queries.

## Usage

**DynamicWhere.ex** provides enums, classes, and extension methods to simplify the construction of dynamic filter expressions. You can define conditions, condition groups, and sets using objects, allowing you to specify complex filtering logic in a structured way.

### Enums

The **DynamicWhere.ex** library includes the following enums to support dynamic querying capabilities:

#### `DataType`

The `DataType` enum provides support for various data types when defining conditions in dynamic queries. It includes the following values:

- `Text`: Represents textual data.
- `Number`: Represents numeric data.
- `Boolean`: Represents boolean data.
- `DateTime`: Represents date and time data.

You can use the `DataType` enum to specify the data type for your dynamic query conditions, making it easier to work with different types of data.

#### `Operator`

The `Operator` enum offers a comprehensive set of logical comparison operators for constructing dynamic queries. It includes the following operators:

- `Equal`: Equality comparison.
- `NotEqual`: Inequality comparison.
- `GreaterThan`: Greater than comparison.
- `GreaterThanOrEqual`: Greater than or equal to comparison.
- `LessThan`: Less than comparison.
- `LessThanOrEqual`: Less than or equal to comparison.
- `Contains`: Substring containment check.
- `NotContains`: Negation of substring containment check.
- `StartsWith`: Prefix check.
- `NotStartsWith`: Negation of prefix check.
- `EndsWith`: Suffix check.
- `NotEndsWith`: Negation of suffix check.
- `In`: Membership check.
- `NotIn`: Negation of membership check.
- `Between`: Range comparison.
- `NotBetween`: Negation of range comparison.
- `IsNull`: Null check.
- `IsNotNull`: Negation of null check.

These operators can be used in combination with the library's extension methods to create complex dynamic queries with ease.

#### `Connector`

The `Connector` enum provides logical connectors that allow you to combine conditions within a query. It includes the following connectors:

- `And`: Represents the logical "AND" connector.
- `Or`: Represents the logical "OR" connector.

You can use these connectors to specify how conditions should be combined in your dynamic queries, enabling flexible query construction.

#### `Intersection`

The `Intersection` enum is used to define how multiple sets of conditions should be combined in a query. It includes the following intersection types:

- `Union`: Represents the union of sets, combining conditions with "OR" logic.
- `Intersect`: Represents the intersection of sets, combining conditions with "AND" logic.
- `Except`: Represents the difference between sets, excluding conditions that meet specified criteria.

These intersection types are valuable when you need to apply logical operations to multiple sets of conditions in your dynamic queries.

### Classes

The **DynamicWhere.ex** library includes the following classes that enable you to construct dynamic queries:

#### `Condition`

The `Condition` class represents an individual condition in a dynamic query. It contains the following properties:

- `Sort`: An integer that specifies the sort order of the condition.
- `Field`: A string that indicates the field or property to which the condition applies.
- `DataType`: An instance of the `DataType` enum, specifying the data type of the condition.
- `Operator`: An instance of the `Operator` enum, defining the logical comparison operator for the condition.
- `Values`: A list of strings representing the values to compare against. This list may contain one or more values, depending on the operator.

You can create instances of the `Condition` class to define individual conditions within your dynamic queries.

#### `ConditionGroup`

The `ConditionGroup` class represents a group of conditions that are logically combined within a dynamic query. It includes the following properties:

- `Sort`: An integer that determines the sort order of the condition group.
- `Connector`: An instance of the `Connector` enum, specifying how conditions within the group should be connected (using "AND" or "OR" logic).
- `Conditions`: A list of `Condition` objects representing the conditions within the group.
- `SubConditionGroups`: A list of `ConditionGroup` objects, allowing for nested groups of conditions.

You can use the `ConditionGroup` class to create complex conditions by combining multiple conditions and nesting groups as needed.

#### `ConditionSet`

The `ConditionSet` class represents a set of conditions in a dynamic query. It includes the following properties:

- `Sort`: An integer that defines the sort order of the condition set.
- `Intersection`: An instance of the `Intersection` enum, indicating how conditions within the set should be combined (union, intersection, or exclusion).
- `ConditionGroup`: An instance of the `ConditionGroup` class representing the group of conditions within the set.

You can use the `ConditionSet` class to create sets of conditions and specify how those conditions should be combined within your dynamic queries.

#### `Segment`

The `Segment` class serves as the top-level container for dynamic queries. It includes the following property:

- `ConditionSets`: A list of `ConditionSet` objects representing multiple sets of conditions within a query.

You can use the `Segment` class to organize and manage multiple sets of conditions in your dynamic queries.

These classes provide a powerful foundation for constructing dynamic queries using the **DynamicWhere.ex** library.

### Extension Methods

The **DynamicWhere.ex** library includes a set of extension methods that enhance the functionality of LINQ queries for dynamic querying purposes. Below are the public extension methods provided by the library:

#### `ToListAsync<T>(this IQueryable<T> query, Segment segment)`

This extension method allows you to execute an asynchronous query and return the results as a list based on the specified segment of conditions.

**Usage:**

```csharp
// Assuming you have an IQueryable<T> query and a Segment segment defined
List<T> results = await query.ToListAsync(segment);
```

**Documentation:**

- **Parameters:**
  - `query` (IQueryable<T>): The queryable source to execute the dynamic query on.
  - `segment` (Segment): The segment object containing sets of conditions for dynamic querying.

- **Return Value:**
  - `List<T>`: A list of query results that meet the conditions specified in the segment.

---

#### `Where<T>(this IQueryable<T> query, ConditionGroup group)`

This extension method allows you to filter an IQueryable based on a specified `ConditionGroup`.

**Usage:**

```csharp
// Assuming you have an IQueryable<T> query and a ConditionGroup conditionGroup defined
var filteredQuery = query.Where(conditionGroup);
```

**Documentation:**

- **Parameters:**
  - `query` (IQueryable<T>): The queryable source to filter.
  - `conditionGroup` (ConditionGroup): The condition group specifying the filtering conditions.

- **Return Value:**
  - `IQueryable<T>`: A new queryable instance with the specified conditions applied.

---

#### `Where<T>(this IQueryable<T> query, Condition condition)`

This extension method allows you to filter an IQueryable based on a single `Condition`.

**Usage:**

```csharp
// Assuming you have an IQueryable<T> query and a Condition condition defined
var filteredQuery = query.Where(condition);
```

**Documentation:**

- **Parameters:**
  - `query` (IQueryable<T>): The queryable source to filter.
  - `condition` (Condition): The single condition specifying the filtering criteria.

- **Return Value:**
  - `IQueryable<T>`: A new queryable instance with the specified condition applied.

These extension methods empower you to build dynamic queries by composing conditions and filter your data based on flexible criteria.

## JSON Examples

### Condition Example

```json
{
  "Sort": 1,
  "Field": "ProductName",
  "DataType": "Text",
  "Operator": "Contains",
  "Values": ["apple"]
}
```

### Condition Group Example 

* Condition Group with 3 Conditions and 2 Subgroups
* Each Subgroup with 2 Conditions

```json
{
  "Sort": 1,
  "Connector": "And",
  "Conditions": [
    {
      "Sort": 1,
      "Field": "Price",
      "DataType": "Number",
      "Operator": "GreaterThan",
      "Values": ["10"]
    },
    {
      "Sort": 2,
      "Field": "Category",
      "DataType": "Text",
      "Operator": "Equal",
      "Values": ["Electronics"]
    },
    {
      "Sort": 3,
      "Field": "InStock",
      "DataType": "Boolean",
      "Operator": "Equal",
      "Values": ["true"]
    }
  ],
  "SubConditionGroups": [
    {
      "Sort": 1,
      "Connector": "Or",
      "Conditions": [
        {
          "Sort": 1,
          "Field": "Discount",
          "DataType": "Number",
          "Operator": "GreaterThan",
          "Values": ["20"]
        },
        {
          "Sort": 2,
          "Field": "Brand",
          "DataType": "Text",
          "Operator": "Equal",
          "Values": ["Sony"]
        }
      ],
      "SubConditionGroups": []
    },
    {
      "Sort": 2,
      "Connector": "And",
      "Conditions": [
        {
          "Sort": 1,
          "Field": "ShippingDate",
          "DataType": "DateTime",
          "Operator": "GreaterThan",
          "Values": ["2023-01-01"]
        },
        {
          "Sort": 2,
          "Field": "ShippingDate",
          "DataType": "DateTime",
          "Operator": "LessThanOrEqual",
          "Values": ["2023-12-31"]
        }
      ],
      "SubConditionGroups": []
    }
  ]
}
```

### Segment Example 

* Segment with 2 Sets
* Each Set with a Condition Group
* Each Condition Group with 3 Conditions and 2 Subgroups
* Each Subgroup with 2 Conditions

```json
{
  "ConditionSets": [
    {
      "Sort": 1,
      "Intersection": "Union",
      "ConditionGroup": {
        "Sort": 1,
        "Connector": "Or",
        "Conditions": [
          {
            "Sort": 1,
            "Field": "Rating",
            "DataType": "Number",
            "Operator": "GreaterThan",
            "Values": ["4.5"]
          },
          {
            "Sort": 2,
            "Field": "Reviews",
            "DataType": "Number",
            "Operator": "GreaterThan",
            "Values": ["100"]
          },
          {
            "Sort": 3,
            "Field": "Discount",
            "DataType": "Number",
            "Operator": "GreaterThan",
            "Values": ["30"]
          }
        ],
        "SubConditionGroups": [
          {
            "Sort": 1,
            "Connector": "Or",
            "Conditions": [
              {
                "Sort": 1,
                "Field": "Brand",
                "DataType": "Text",
                "Operator": "Equal",
                "Values": ["Samsung"]
              },
              {
                "Sort": 2,
                "Field": "Brand",
                "DataType": "Text",
                "Operator": "Equal",
                "Values": ["LG"]
              }
            ],
            "SubConditionGroups": []
          },
          {
            "Sort": 2,
            "Connector": "And",
            "Conditions": [
              {
                "Sort": 1,
                "Field": "InStock",
                "DataType": "Boolean",
                "Operator": "Equal",
                "Values": ["true"]
              },
              {
                "Sort": 2,
                "Field": "Category",
                "DataType": "Text",
                "Operator": "Equal",
                "Values": ["Appliances"]
              }
            ],
            "SubConditionGroups": []
          }
        ]
      }
    },
    {
      "Sort": 2,
      "Intersection": "Intersect",
      "ConditionGroup": {
        "Sort": 1,
        "Connector": "And",
        "Conditions": [
          {
            "Sort": 1,
            "Field": "Color",
            "DataType": "Text",
            "Operator": "Equal",
            "Values": ["Red"]
          },
          {
            "Sort": 2,
            "Field": "Size",
            "DataType": "Text",
            "Operator": "Equal",
            "Values": ["Large"]
          },
          {
            "Sort": 3,
            "Field": "Weight",
            "DataType": "Number",
            "Operator": "LessThanOrEqual",
            "Values": ["5"]
          }
        ],
        "SubConditionGroups": [
          {
            "Sort": 1,
            "Connector": "Or",
            "Conditions": [
              {
                "Sort": 1,
                "Field": "Price",
                "DataType": "Number",
                "Operator": "LessThanOrEqual",
                "Values": ["50"]
              },
              {
                "Sort": 2,
                "Field": "Rating",
                "DataType": "Number",
                "Operator": "GreaterThan",
                "Values": ["4"]
              }
            ],
            "SubConditionGroups": []
          },
          {
            "Sort": 2,
            "Connector": "And",
            "Conditions": [
              {
                "Sort": 1,
                "Field": "Category",
                "DataType": "Text",
                "Operator": "Equal",
                "Values": ["Clothing"]
              },
              {
                "Sort": 2,
                "Field": "Season",
                "DataType": "Text",
                "Operator": "Equal",
                "Values": ["Summer"]
              }
            ],
            "SubConditionGroups": []
          }
        ]
      }
    }
  ]
}
```

These JSON examples demonstrate various scenarios of conditions, condition groups, and segments that can be used with the library.

## License

This library is released under a free and open-source license, allowing you to use it in your projects without restrictions.

## Changelog

- Version 1: Initial release

## Credits

This extension is built upon the foundation of [System.Linq.Dynamic.Core](https://github.com/StefH/System.Linq.Dynamic.Core), and we extend our appreciation to the authors and contributors of the original library for their valuable work.
