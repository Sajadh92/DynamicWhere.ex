using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>WhereTestController</c>: one test per operator per
/// <see cref="DataType"/>, the <c>Condition.Values</c> shapes, and the <see cref="ConditionGroup"/>
/// combinations.
/// </summary>
/// <remarks>
/// Product names are chosen so case-sensitive and case-insensitive operators return different sets:
/// "Pro", "pro", "ProBook", "Laptop Pro", "Ultra", "Basic", "Widget", "Gadget pro".
/// Every operator except <c>IsNull</c>/<c>IsNotNull</c> is null-guarded by the builder, so rows with a
/// null field never match a negated operator.
/// </remarks>
public class WhereTests : SalesTestBase
{
    public WhereTests(SalesFixture fixture) : base(fixture) { }

    private static Condition Cond(string field, DataType type, Operator op, params object[] values) =>
        new() { Sort = 1, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    /// <summary>Names matching the condition, sorted in memory so the comparer never reaches SQL.</summary>
    private string[] ProductNames(Condition condition) =>
        Products.Where(condition).Select(p => p.Name).ToList().OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string[] Sorted(params string[] names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Runs the condition under LINQ to Objects, where the generated expression carries exact .NET
    /// string semantics with no provider collation involved.
    /// </summary>
    private static int InMemoryCount(Condition condition) =>
        SalesSeed.Products().AsQueryable().Where(condition).Count();

    /// <summary>
    /// Asserts a text operator's row count both in memory (the library's own semantics) and against
    /// SQLite (what the provider's collation actually does with the translated expression).
    /// </summary>
    private void AssertText(Operator op, string value, int inMemory, int onSqlite)
    {
        Condition condition = Cond("Name", DataType.Text, op, value);

        Assert.Equal(inMemory, InMemoryCount(condition));
        Assert.Equal(onSqlite, Products.Where(condition).Count());
    }

    #region Text operators — case-sensitive

    // Contains keeps .NET semantics on SQLite: EF translates it to instr(), which is case-sensitive.
    [Fact]
    public void TextContains() => AssertText(Operator.Contains, "Pro", inMemory: 3, onSqlite: 3);

    [Fact]
    public void TextNotContains() => AssertText(Operator.NotContains, "Pro", inMemory: 5, onSqlite: 5);

    // Equality compares with SQLite's default BINARY collation — also case-sensitive.
    [Fact]
    public void TextEqual() =>
        Assert.Equal(["Pro"], ProductNames(Cond("Name", DataType.Text, Operator.Equal, "Pro")));

    [Fact]
    public void TextNotEqual() => AssertText(Operator.NotEqual, "Pro", inMemory: 7, onSqlite: 7);

    // StartsWith/EndsWith translate to LIKE, and SQLite's LIKE is case-insensitive for ASCII.
    // "pro" and "Gadget pro" therefore match here but not under LINQ to Objects. On SQL Server the
    // outcome follows the column collation instead. Use IStartsWith/IEndsWith when the intent is
    // case-insensitive, and expect provider-dependent results from the case-sensitive variants.
    [Fact]
    public void TextStartsWith() => AssertText(Operator.StartsWith, "Pro", inMemory: 2, onSqlite: 3);

    [Fact]
    public void TextEndsWith() => AssertText(Operator.EndsWith, "Pro", inMemory: 2, onSqlite: 4);

    [Fact]
    public void TextNotStartsWith() => AssertText(Operator.NotStartsWith, "Pro", inMemory: 6, onSqlite: 5);

    [Fact]
    public void TextNotEndsWith() => AssertText(Operator.NotEndsWith, "Pro", inMemory: 6, onSqlite: 4);

    [Fact]
    public void TextIn() =>
        Assert.Equal(Sorted("Basic", "Pro", "Ultra"),
            ProductNames(Cond("Name", DataType.Text, Operator.In, "Pro", "Ultra", "Basic")));

    [Fact]
    public void TextNotIn() =>
        Assert.Equal(6, ProductNames(Cond("Name", DataType.Text, Operator.NotIn, "Pro", "Ultra")).Length);

    #endregion

    #region Text operators — case-insensitive

    // The I* operators lower both sides in the expression itself, so they agree everywhere.
    [Fact]
    public void TextIContains() => AssertText(Operator.IContains, "pro", inMemory: 5, onSqlite: 5);

    [Fact]
    public void TextIEqual() => AssertText(Operator.IEqual, "pro", inMemory: 2, onSqlite: 2);

    [Fact]
    public void TextIStartsWith() => AssertText(Operator.IStartsWith, "pro", inMemory: 3, onSqlite: 3);

    [Fact]
    public void TextIEndsWith() => AssertText(Operator.IEndsWith, "pro", inMemory: 4, onSqlite: 4);

    [Fact]
    public void TextINotContains() => AssertText(Operator.INotContains, "pro", inMemory: 3, onSqlite: 3);

    [Fact]
    public void TextINotEqual() => AssertText(Operator.INotEqual, "pro", inMemory: 6, onSqlite: 6);

    [Fact]
    public void TextINotStartsWith() => AssertText(Operator.INotStartsWith, "pro", inMemory: 5, onSqlite: 5);

    [Fact]
    public void TextINotEndsWith() => AssertText(Operator.INotEndsWith, "pro", inMemory: 4, onSqlite: 4);

    [Fact]
    public void TextIIn() =>
        Assert.Equal(4, ProductNames(Cond("Name", DataType.Text, Operator.IIn, "pro", "ultra", "basic")).Length);

    [Fact]
    public void TextINotIn() =>
        Assert.Equal(5, ProductNames(Cond("Name", DataType.Text, Operator.INotIn, "pro", "ultra")).Length);

    #endregion

    #region Text null checks

    [Fact]
    public void TextIsNull() =>
        Assert.Equal(3, Products.Where(Cond("Description", DataType.Text, Operator.IsNull)).Count());

    [Fact]
    public void TextIsNotNull() =>
        Assert.Equal(5, Products.Where(Cond("Description", DataType.Text, Operator.IsNotNull)).Count());

    #endregion

    #region Number operators

    [Fact]
    public void NumberGreaterThan() =>
        Assert.Equal(5, Products.Where(Cond("Price", DataType.Number, Operator.GreaterThan, "50")).Count());

    [Fact]
    public void NumberGreaterThanOrEqual() =>
        Assert.Equal(6, Products.Where(Cond("Rating", DataType.Number, Operator.GreaterThanOrEqual, "3.5")).Count());

    [Fact]
    public void NumberLessThan() =>
        Assert.Equal(4, Products.Where(Cond("Price", DataType.Number, Operator.LessThan, "100")).Count());

    [Fact]
    public void NumberLessThanOrEqual() =>
        Assert.Equal(6, Products.Where(Cond("StockQuantity", DataType.Number, Operator.LessThanOrEqual, "50")).Count());

    [Fact]
    public void NumberBetween() =>
        Assert.Equal(3, Products.Where(Cond("Price", DataType.Number, Operator.Between, "20", "200")).Count());

    [Fact]
    public void NumberNotBetween() =>
        Assert.Equal(5, Products.Where(Cond("Price", DataType.Number, Operator.NotBetween, "20", "200")).Count());

    [Fact]
    public void NumberEqual() =>
        Assert.Equal(2, Products.Where(Cond("StockQuantity", DataType.Number, Operator.Equal, "0")).Count());

    [Fact]
    public void NumberNotEqual() =>
        Assert.Equal(6, Products.Where(Cond("StockQuantity", DataType.Number, Operator.NotEqual, "0")).Count());

    [Fact]
    public void NumberIn() =>
        Assert.Equal(5, Products.Where(Cond("StockQuantity", DataType.Number, Operator.In, "0", "10", "50", "100")).Count());

    #endregion

    #region Guid operators

    [Fact]
    public void GuidIsNull() =>
        Assert.Equal(["Widget"], Products.Where(Cond("CategoryId", DataType.Guid, Operator.IsNull)).Select(p => p.Name).ToArray());

    [Fact]
    public void GuidIsNotNull() =>
        Assert.Equal(7, Products.Where(Cond("CategoryId", DataType.Guid, Operator.IsNotNull)).Count());

    [Fact]
    public void GuidEqual() =>
        Assert.Equal(2, Products.Where(Cond("CategoryId", DataType.Guid, Operator.Equal, SalesSeed.Smartphones.ToString())).Count());

    #endregion

    #region Boolean operators

    [Fact]
    public void BooleanEqualTrue() =>
        Assert.Equal(6, Products.Where(Cond("IsActive", DataType.Boolean, Operator.Equal, "true")).Count());

    [Fact]
    public void BooleanEqualFalse() =>
        Assert.Equal(2, Products.Where(Cond("IsActive", DataType.Boolean, Operator.Equal, "false")).Count());

    [Fact]
    public void BooleanNotEqual() =>
        Assert.Equal(2, Products.Where(Cond("IsActive", DataType.Boolean, Operator.NotEqual, "true")).Count());

    #endregion

    #region DateTime and Date operators

    [Fact]
    public void DateTimeGreaterThanOrEqual() =>
        Assert.Equal(4, Products.Where(Cond("CreatedAt", DataType.DateTime, Operator.GreaterThanOrEqual, "2024-01-01T00:00:00")).Count());

    [Fact]
    public void DateTimeBetween() =>
        Assert.Equal(6, Products.Where(Cond("CreatedAt", DataType.DateTime, Operator.Between, "2023-01-01T00:00:00", "2024-12-31T23:59:59")).Count());

    [Fact]
    public void DateTimeIsNull() =>
        Assert.Equal(3, Products.Where(Cond("UpdatedAt", DataType.DateTime, Operator.IsNull)).Count());

    [Fact]
    public void DateTimeIsNotNull() =>
        Assert.Equal(5, Products.Where(Cond("UpdatedAt", DataType.DateTime, Operator.IsNotNull)).Count());

    [Fact]
    public void DateGreaterThan() =>
        Assert.Equal(4, Orders.Where(Cond("OrderDate", DataType.Date, Operator.GreaterThan, "2023-06-01")).Count());

    #endregion

    #region Enum operators

    [Fact]
    public void EnumEqual() =>
        Assert.Equal(2, Orders.Where(Cond("Status", DataType.Enum, Operator.Equal, "Pending")).Count());

    [Fact]
    public void EnumIn() =>
        Assert.Equal(4, Orders.Where(Cond("Status", DataType.Enum, Operator.In, "Pending", "Processing", "Shipped")).Count());

    [Fact]
    public void EnumNotEqual() =>
        Assert.Equal(5, Orders.Where(Cond("Status", DataType.Enum, Operator.NotEqual, "Cancelled")).Count());

    #endregion

    #region Nested property paths

    [Fact]
    public void NestedOwnedTypeProperty() =>
        Assert.Equal(3, Orders.Where(Cond("ShippingAddress.Country", DataType.Text, Operator.IEqual, "USA")).Count());

    [Fact]
    public void NestedReferenceNavigationProperty() =>
        Assert.Equal(2, Products.Where(Cond("Category.Name", DataType.Text, Operator.Equal, "Smartphones")).Count());

    [Fact]
    public void NestedCollectionNavigationWrapsInAny()
    {
        // Orders whose line items include the "Pro" product — auto-wrapped in .Any().
        var result = Orders.Where(Cond("OrderItems.Product.Name", DataType.Text, Operator.Equal, "Pro")).ToList();

        Assert.Equal(["ORD-1001"], result.Select(o => o.OrderNumber));
    }

    #endregion

    #region Condition.Values shapes — raw JSON types alongside strings

    [Fact]
    public void ValuesRawInt() =>
        Assert.Equal(1, Products.Where(Cond("StockQuantity", DataType.Number, Operator.GreaterThan, 100)).Count());

    [Fact]
    public void ValuesRawDouble() =>
        Assert.Equal(4, Products.Where(Cond("Price", DataType.Number, Operator.LessThanOrEqual, 99.99)).Count());

    [Fact]
    public void ValuesRawBetween() =>
        Assert.Equal(5, Products.Where(Cond("Price", DataType.Number, Operator.Between, 10, 500.5)).Count());

    [Fact]
    public void ValuesRawIn() =>
        Assert.Equal(4, Products.Where(Cond("StockQuantity", DataType.Number, Operator.In, 1, 5, 10, 50)).Count());

    [Fact]
    public void ValuesRawBoolTrue() =>
        Assert.Equal(6, Products.Where(Cond("IsActive", DataType.Boolean, Operator.Equal, true)).Count());

    [Fact]
    public void ValuesRawBoolFalse() =>
        Assert.Equal(2, Products.Where(Cond("IsActive", DataType.Boolean, Operator.Equal, false)).Count());

    [Fact]
    public void ValuesMixedRawAndString() =>
        Assert.Equal(4, Products.Where(Cond("StockQuantity", DataType.Number, Operator.Between, 10, "500")).Count());

    [Fact]
    public void ValuesLegacyStringNumber() =>
        Assert.Equal(1, Products.Where(Cond("StockQuantity", DataType.Number, Operator.GreaterThan, "100")).Count());

    [Fact]
    public void ValuesLegacyStringBool() =>
        Assert.Equal(6, Products.Where(Cond("IsActive", DataType.Boolean, Operator.Equal, "true")).Count());

    #endregion

    #region ConditionGroup combinations

    [Fact]
    public void GroupAnd()
    {
        // Price 20–200 AND Rating >= 3.5 AND IsActive → "Pro" (150/4.5), "pro" (45.50/3.5), "Ultra" (75.25/4.0).
        var group = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions =
            [
                Cond("Price", DataType.Number, Operator.Between, "20", "200"),
                new Condition { Sort = 2, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["3.5"] },
                new Condition { Sort = 3, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
            ]
        };

        Assert.Equal(Sorted("Pro", "pro", "Ultra"), Products.Where(group).Select(p => p.Name).ToList().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void GroupOr()
    {
        // Budget (< 20) OR Premium (> 500) OR Top-Rated (>= 4.8).
        var group = new ConditionGroup
        {
            Connector = Connector.Or,
            Conditions =
            [
                Cond("Price", DataType.Number, Operator.LessThan, "20"),
                new Condition { Sort = 2, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["500"] },
                new Condition { Sort = 3, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.8"] }
            ]
        };

        // Basic 19.99, Widget 5.00, ProBook 999.99/4.8, Gadget pro 550.75/5.0
        Assert.Equal(4, Products.Where(group).Count());
    }

    [Fact]
    public void GroupNested()
    {
        // IsActive AND (Price < 30 OR Price > 200)
        var group = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = [Cond("IsActive", DataType.Boolean, Operator.Equal, "true")],
            SubConditionGroups =
            [
                new ConditionGroup
                {
                    Sort = 1,
                    Connector = Connector.Or,
                    Conditions =
                    [
                        Cond("Price", DataType.Number, Operator.LessThan, "30"),
                        new Condition { Sort = 2, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["200"] }
                    ]
                }
            ]
        };

        // Active: Pro 150, pro 45.50, ProBook 999.99, Ultra 75.25, Widget 5.00, Gadget pro 550.75
        // of which < 30 or > 200: ProBook, Widget, Gadget pro
        Assert.Equal(Sorted("Gadget pro", "ProBook", "Widget"),
            Products.Where(group).Select(p => p.Name).ToList().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void GroupDeepNested()
    {
        // IsActive AND ((Price > 100 AND Rating >= 4.5) OR StockQuantity == 0)
        var group = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = [Cond("IsActive", DataType.Boolean, Operator.Equal, "true")],
            SubConditionGroups =
            [
                new ConditionGroup
                {
                    Sort = 1,
                    Connector = Connector.Or,
                    Conditions = [Cond("StockQuantity", DataType.Number, Operator.Equal, "0")],
                    SubConditionGroups =
                    [
                        new ConditionGroup
                        {
                            Sort = 1,
                            Connector = Connector.And,
                            Conditions =
                            [
                                Cond("Price", DataType.Number, Operator.GreaterThan, "100"),
                                new Condition { Sort = 2, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.5"] }
                            ]
                        }
                    ]
                }
            ]
        };

        // Stock 0 and active: pro, Gadget pro. Price>100 && Rating>=4.5 and active: Pro, ProBook, Gadget pro.
        Assert.Equal(Sorted("Gadget pro", "Pro", "ProBook", "pro"),
            Products.Where(group).Select(p => p.Name).ToList().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void GroupCustomers()
    {
        // IsActive AND (FirstName starts with "J" OR TotalSpent > 1000)
        var group = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = [Cond("IsActive", DataType.Boolean, Operator.Equal, "true")],
            SubConditionGroups =
            [
                new ConditionGroup
                {
                    Sort = 1,
                    Connector = Connector.Or,
                    Conditions =
                    [
                        Cond("FirstName", DataType.Text, Operator.StartsWith, "J"),
                        new Condition { Sort = 2, Field = "TotalSpent", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["1000"] }
                    ]
                }
            ]
        };

        // Active customers: John, Jane, Alice. Of those, name starts with J or spent > 1000: John, Jane.
        Assert.Equal(Sorted("Jane", "John"),
            Customers.Where(group).Select(c => c.FirstName).ToList().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void GroupOrdersNestedAddress()
    {
        // IsPaid AND (Country = USA OR Country = Canada)
        var group = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = [Cond("IsPaid", DataType.Boolean, Operator.Equal, "true")],
            SubConditionGroups =
            [
                new ConditionGroup
                {
                    Sort = 1,
                    Connector = Connector.Or,
                    Conditions =
                    [
                        Cond("ShippingAddress.Country", DataType.Text, Operator.IEqual, "USA"),
                        new Condition { Sort = 2, Field = "ShippingAddress.Country", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["Canada"] }
                    ]
                }
            ]
        };

        // Paid: 1001 (USA), 1002 (USA), 1004 (Germany). USA/Canada of those: 1001, 1002.
        Assert.Equal(["ORD-1001", "ORD-1002"], Orders.Where(group).OrderBy(o => o.OrderNumber).Select(o => o.OrderNumber).ToList());
    }

    [Fact]
    public void EmptyConditionGroupReturnsEverything()
    {
        var group = new ConditionGroup { Connector = Connector.And, Conditions = [] };

        Assert.Equal(8, Products.Where(group).Count());
    }

    #endregion
}
