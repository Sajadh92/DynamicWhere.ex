using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>SelectTestController</c> — the typed <c>Select&lt;T&gt;</c>
/// projection and the dynamic <c>SelectDynamic&lt;T&gt;</c> projection.
/// </summary>
public class SelectTests : SalesTestBase
{
    public SelectTests(SalesFixture fixture) : base(fixture) { }

    private Product Pro(List<Product> projected) => projected.Single(p => p.Id == SalesSeed.ProductPro);

    #region Select<T> — typed projection

    [Fact]
    public void SelectScalars()
    {
        var result = Products.Select(["Id", "Name", "Price", "Rating", "IsActive"]).ToList();

        Assert.Equal(8, result.Count);

        Product pro = Pro(result);
        Assert.Equal("Pro", pro.Name);
        Assert.Equal(150.00m, pro.Price);
        Assert.Equal(4.5, pro.Rating);
        Assert.True(pro.IsActive);

        // Fields that were not requested stay at their default.
        Assert.Null(pro.Description);
        Assert.Equal(default, pro.CreatedAt);
        Assert.Equal(0, pro.StockQuantity);
    }

    [Fact]
    public void SelectNestedReference()
    {
        var result = Products.Select(["Id", "Name", "Category.Name"]).ToList();

        Product pro = Pro(result);
        Assert.Equal("Smartphones", pro.Category!.Name);

        // The nested entity's Id is included automatically; its other fields are not.
        Assert.Equal(SalesSeed.Smartphones, pro.Category.Id);
        Assert.Null(pro.Category.Description);

        // A product with no category still materialises a placeholder rather than null, so callers
        // never hit a NullReferenceException. Its members hold the entity's own defaults — Name is
        // declared as `= string.Empty`, so it comes back empty rather than null.
        Product widget = result.Single(p => p.Id == SalesSeed.ProductWidget);
        Assert.NotNull(widget.Category);
        Assert.Equal(string.Empty, widget.Category.Name);
        Assert.Equal(Guid.Empty, widget.Category.Id);
    }

    [Fact]
    public void SelectNestedCollection()
    {
        var result = Products.Select(["Id", "Name", "OrderItems.Quantity"]).ToList();

        Product pro = Pro(result);
        Assert.Equal([2], pro.OrderItems.Select(i => i.Quantity));

        // "Laptop Pro" was never ordered — the projected collection is empty, not null.
        Product laptop = result.Single(p => p.Id == SalesSeed.ProductLaptopPro);
        Assert.Empty(laptop.OrderItems);
    }

    [Fact]
    public void SelectWholeNavigation()
    {
        var result = Products.Select(["Id", "Name", "Category"]).ToList();

        Product pro = Pro(result);
        Assert.Equal("Smartphones", pro.Category!.Name);
        Assert.Equal(2, pro.Category.DisplayOrder);
        Assert.True(pro.Category.IsActive);
    }

    [Fact]
    public void SelectComplexMultiPath()
    {
        var result = Products.Select(["Id", "Name", "Price", "Tags", "Category.Name", "OrderItems.Quantity"]).ToList();

        Product pro = Pro(result);
        Assert.Equal("Pro", pro.Name);
        Assert.Equal(150.00m, pro.Price);
        Assert.Equal(["wireless", "flagship"], pro.Tags);
        Assert.Equal("Smartphones", pro.Category!.Name);
        Assert.Equal([2], pro.OrderItems.Select(i => i.Quantity));
    }

    [Fact]
    public void SelectNullableScalars()
    {
        var result = Products.Select(["Id", "Description", "UpdatedAt", "ManufactureDate", "AvailableFrom"]).ToList();

        Product pro = Pro(result);
        Assert.Equal("Flagship handset", pro.Description);
        Assert.Equal(new DateTime(2024, 6, 1), pro.UpdatedAt);
        Assert.Equal(new DateOnly(2024, 2, 1), pro.ManufactureDate);
        Assert.Equal(new TimeOnly(9, 0), pro.AvailableFrom);

        // Nulls survive the projection.
        Product lower = result.Single(p => p.Id == SalesSeed.ProductProLower);
        Assert.Null(lower.Description);
        Assert.Null(lower.UpdatedAt);
        Assert.Null(lower.ManufactureDate);
        Assert.Null(lower.AvailableFrom);
    }

    [Fact]
    public void SelectCollectionMultipleFields()
    {
        var result = Products.Select(["Id", "OrderItems.Quantity", "OrderItems.UnitPrice", "OrderItems.Discount"]).ToList();

        OrderItem item = Pro(result).OrderItems.Single();
        Assert.Equal(2, item.Quantity);
        Assert.Equal(150.00m, item.UnitPrice);
        Assert.Equal(0m, item.Discount);

        // TotalPrice was not requested.
        Assert.Equal(0m, item.TotalPrice);
    }

    [Fact]
    public void SelectWholeCollection()
    {
        var result = Products.Select(["Id", "Name", "OrderItems"]).ToList();

        OrderItem item = Pro(result).OrderItems.Single();
        Assert.Equal(2, item.Quantity);
        Assert.Equal(300.00m, item.TotalPrice);
    }

    [Fact]
    public void SelectNullableNavigationProperties()
    {
        var result = Products.Select(["Id", "Category.Name", "Category.Description"]).ToList();

        Assert.Equal("Mobile phones", Pro(result).Category!.Description);

        // The Laptops category has a null description.
        Product book = result.Single(p => p.Id == SalesSeed.ProductProBook);
        Assert.Equal("Laptops", book.Category!.Name);
        Assert.Null(book.Category.Description);
    }

    [Fact]
    public void SelectReviewsCollection()
    {
        var result = Products.Select(["Id", "Reviews.Rating", "Reviews.Title"]).ToList();

        Product pro = Pro(result);
        Assert.Equal([4, 5], pro.Reviews.Select(r => r.Rating).OrderBy(r => r));
        Assert.Contains(pro.Reviews, r => r.Title == "Great phone");

        // Products without reviews project an empty collection.
        Assert.Empty(result.Single(p => p.Id == SalesSeed.ProductWidget).Reviews);
    }

    [Fact]
    public void SelectReviewsNullableField()
    {
        var result = Products.Select(["Id", "Reviews.UpdatedAt", "Reviews.IsVerifiedPurchase"]).ToList();

        var reviews = Pro(result).Reviews.OrderBy(r => r.Id).ToList();
        Assert.Equal(new DateTime(2024, 4, 10), reviews[0].UpdatedAt);
        Assert.True(reviews[0].IsVerifiedPurchase);
        Assert.Null(reviews[1].UpdatedAt);
        Assert.False(reviews[1].IsVerifiedPurchase);
    }

    [Fact]
    public void SelectNullableValueTypeNavigation()
    {
        var result = Products.Select(["Id", "Category.Id", "Category.IsActive", "Category.DisplayOrder"]).ToList();

        Product pro = Pro(result);
        Assert.Equal(SalesSeed.Smartphones, pro.Category!.Id);
        Assert.True(pro.Category.IsActive);
        Assert.Equal(2, pro.Category.DisplayOrder);

        // Product with a null category must not throw when the value types materialise.
        Product widget = result.Single(p => p.Id == SalesSeed.ProductWidget);
        Assert.Equal(Guid.Empty, widget.Category!.Id);
    }

    [Fact]
    public void SelectWholeReviews()
    {
        var result = Products.Select(["Id", "Name", "Reviews"]).ToList();

        Product pro = Pro(result);
        Assert.Equal(2, pro.Reviews.Count);
        Assert.Contains(pro.Reviews, r => r.Content == "Fast and sharp");
    }

    [Fact]
    public void SelectTagsList()
    {
        var result = Products.Select(["Id", "Name", "Tags"]).ToList();

        Assert.Equal(["wireless", "flagship"], Pro(result).Tags);
        Assert.Empty(result.Single(p => p.Id == SalesSeed.ProductBasic).Tags);
    }

    [Fact]
    public void SelectTwoCollections()
    {
        var result = Products.Select(["Id", "Reviews.Rating", "OrderItems.Quantity"]).ToList();

        Product pro = Pro(result);
        Assert.Equal(2, pro.Reviews.Count);
        Assert.Single(pro.OrderItems);
    }

    [Fact]
    public void SelectRejectsEmptyFieldList()
    {
        var ex = Assert.Throws<LogicException>(() => Products.Select([]).ToList());

        Assert.Contains("MustHasFields", ex.Message);
    }

    [Fact]
    public void SelectRejectsUnknownField() =>
        Assert.Throws<LogicException>(() => Products.Select(["Nope"]).ToList());

    #endregion

    #region SelectDynamic<T> — dynamic projection

    private List<dynamic> Dyn(params string[] fields) =>
        Products.SelectDynamic(fields.ToList()).ToDynamicList();

    [Fact]
    public void SelectDynamicScalars()
    {
        var result = Dyn("Id", "Name", "Price", "Rating");

        Assert.Equal(8, result.Count);

        dynamic pro = result.Single(r => r.Id == SalesSeed.ProductPro);
        Assert.Equal("Pro", (string)pro.Name);
        Assert.Equal(150.00m, (decimal)pro.Price);
        Assert.Equal(4.5, (double)pro.Rating);
    }

    [Fact]
    public void SelectDynamicNestedReference()
    {
        dynamic pro = Dyn("Id", "Name", "Category.Name").Single(r => r.Id == SalesSeed.ProductPro);

        // The dotted path becomes a nested object rather than a flattened key.
        Assert.Equal("Smartphones", (string)pro.Category.Name);
    }

    [Fact]
    public void SelectDynamicNestedCollection()
    {
        dynamic pro = Dyn("Id", "OrderItems.Quantity").Single(r => r.Id == SalesSeed.ProductPro);

        var quantities = ((IEnumerable<dynamic>)pro.OrderItems).Select(i => (int)i.Quantity).ToList();
        Assert.Equal([2], quantities);
    }

    [Fact]
    public void SelectDynamicMergedFields()
    {
        dynamic pro = Dyn("Id", "Category.Name", "Category.Id").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Smartphones", (string)pro.Category.Name);
        Assert.Equal(SalesSeed.Smartphones, (Guid)pro.Category.Id);
    }

    [Fact]
    public void SelectDynamicWholeNavigation()
    {
        dynamic pro = Dyn("Id", "Category").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Smartphones", (string)pro.Category.Name);
    }

    [Fact]
    public void SelectDynamicNullableScalars()
    {
        var result = Dyn("Id", "Description", "UpdatedAt");

        dynamic pro = result.Single(r => r.Id == SalesSeed.ProductPro);
        Assert.Equal("Flagship handset", (string)pro.Description);

        dynamic lower = result.Single(r => r.Id == SalesSeed.ProductProLower);
        Assert.Null(lower.Description);
        Assert.Null(lower.UpdatedAt);
    }

    [Fact]
    public void SelectDynamicCollectionMultipleFields()
    {
        dynamic pro = Dyn("Id", "OrderItems.Quantity", "OrderItems.UnitPrice").Single(r => r.Id == SalesSeed.ProductPro);

        dynamic item = ((IEnumerable<dynamic>)pro.OrderItems).Single();
        Assert.Equal(2, (int)item.Quantity);
        Assert.Equal(150.00m, (decimal)item.UnitPrice);
    }

    [Fact]
    public void SelectDynamicWholeCollection()
    {
        dynamic pro = Dyn("Id", "OrderItems").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Single((IEnumerable<dynamic>)pro.OrderItems);
    }

    [Fact]
    public void SelectDynamicNullableNavigationProperties()
    {
        dynamic book = Dyn("Id", "Category.Name", "Category.Description").Single(r => r.Id == SalesSeed.ProductProBook);

        Assert.Equal("Laptops", (string)book.Category.Name);
        Assert.Null(book.Category.Description);
    }

    [Fact]
    public void SelectDynamicReviewsCollection()
    {
        dynamic pro = Dyn("Id", "Reviews.Rating", "Reviews.Title").Single(r => r.Id == SalesSeed.ProductPro);

        var ratings = ((IEnumerable<dynamic>)pro.Reviews).Select(r => (int)r.Rating).OrderBy(r => r).ToList();
        Assert.Equal([4, 5], ratings);
    }

    [Fact]
    public void SelectDynamicMixedNavigation()
    {
        dynamic pro = Dyn("Id", "Category.Name", "OrderItems.Quantity", "OrderItems.UnitPrice")
            .Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Smartphones", (string)pro.Category.Name);
        Assert.Single((IEnumerable<dynamic>)pro.OrderItems);
    }

    [Fact]
    public void SelectDynamicNullableValueTypeNavigation()
    {
        // np() wrapping keeps non-nullable value types behind a nullable navigation from throwing.
        var result = Dyn("Id", "Category.Id", "Category.IsActive", "Category.DisplayOrder");

        dynamic pro = result.Single(r => r.Id == SalesSeed.ProductPro);
        Assert.Equal(SalesSeed.Smartphones, (Guid)pro.Category.Id);
        Assert.True((bool)pro.Category.IsActive);
        Assert.Equal(2, (int)pro.Category.DisplayOrder);

        dynamic widget = result.Single(r => r.Id == SalesSeed.ProductWidget);
        Assert.Null(widget.Category.Id);
        Assert.Null(widget.Category.IsActive);
        Assert.Null(widget.Category.DisplayOrder);
    }

    [Fact]
    public void SelectDynamicCollectionNullableField()
    {
        dynamic pro = Dyn("Id", "Reviews.UpdatedAt", "Reviews.IsVerifiedPurchase").Single(r => r.Id == SalesSeed.ProductPro);

        var reviews = ((IEnumerable<dynamic>)pro.Reviews).ToList();
        Assert.Equal(2, reviews.Count);
        Assert.Contains(reviews, r => r.UpdatedAt == null);
        Assert.Contains(reviews, r => r.UpdatedAt != null);
    }

    [Fact]
    public void SelectDynamicWholeReviews()
    {
        dynamic pro = Dyn("Id", "Name", "Reviews").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal(2, ((IEnumerable<dynamic>)pro.Reviews).Count());
    }

    [Fact]
    public void SelectDynamicTagsList()
    {
        dynamic pro = Dyn("Id", "Name", "Tags").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal(["wireless", "flagship"], (IEnumerable<string>)pro.Tags);
    }

    [Fact]
    public void SelectDynamicTwoCollections()
    {
        dynamic pro = Dyn("Id", "Reviews.Rating", "OrderItems.Quantity").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal(2, ((IEnumerable<dynamic>)pro.Reviews).Count());
        Assert.Single((IEnumerable<dynamic>)pro.OrderItems);
    }

    [Fact]
    public void SelectDynamicDeepMerge()
    {
        dynamic pro = Dyn("Id", "Category.Name", "Category.Id", "Category.IsActive", "Category.DisplayOrder")
            .Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Smartphones", (string)pro.Category.Name);
        Assert.Equal(SalesSeed.Smartphones, (Guid)pro.Category.Id);
        Assert.True((bool)pro.Category.IsActive);
        Assert.Equal(2, (int)pro.Category.DisplayOrder);
    }

    [Fact]
    public void SelectDynamicAllScalars()
    {
        dynamic pro = Dyn("Id", "Name", "Description", "Price", "Rating", "StockQuantity", "IsActive", "CreatedAt", "UpdatedAt", "CategoryId")
            .Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Pro", (string)pro.Name);
        Assert.Equal(10, (int)pro.StockQuantity);
        Assert.Equal(new DateTime(2024, 3, 1), (DateTime)pro.CreatedAt);
        Assert.Equal(SalesSeed.Smartphones, (Guid)pro.CategoryId);
    }

    [Fact]
    public void SelectDynamicWholeNavigationIsDroppedWhenSubFieldRequested()
    {
        // "Category" and "Category.Name" collide; the sub-field projection wins.
        dynamic pro = Dyn("Id", "Category", "Category.Name").Single(r => r.Id == SalesSeed.ProductPro);

        Assert.Equal("Smartphones", (string)pro.Category.Name);
    }

    [Fact]
    public void SelectDynamicRejectsEmptyFieldList() =>
        Assert.Throws<LogicException>(() => Products.SelectDynamic([]).ToDynamicList());

    #endregion
}
