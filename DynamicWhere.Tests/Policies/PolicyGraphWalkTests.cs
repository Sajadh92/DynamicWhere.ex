using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>A line whose cost is hidden.</summary>
internal class MaskedLine
{
    [DwMask(MaskStrategy.Full)]
    [DwNoOrder]
    public string Cost { get; set; } = string.Empty;
}

/// <summary>
/// A line with value equality, as a record has. Two distinct lines holding the same cost are equal
/// to each other, which is what makes reference identity load-bearing rather than incidental.
/// </summary>
internal record MaskedLineRecord
{
    [DwMask(MaskStrategy.Full)]
    [DwNoOrder]
    public string Cost { get; set; } = string.Empty;
}

/// <summary>An invoice whose lines are records.</summary>
internal class RecordInvoice
{
    public int Id { get; set; }

    public List<MaskedLineRecord> Lines { get; set; } = new();
}

/// <summary>An invoice reaching its lines through a list.</summary>
internal class ListInvoice
{
    public int Id { get; set; }

    public List<MaskedLine> Lines { get; set; } = new();
}

/// <summary>An invoice reaching its lines through an array.</summary>
internal class ArrayInvoice
{
    public int Id { get; set; }

    public MaskedLine[] Lines { get; set; } = Array.Empty<MaskedLine>();
}

/// <summary>A reference navigation that may be absent.</summary>
internal class OptionalHolder
{
    public MaskedLine? Line { get; set; }
}

/// <summary>A member with no setter, so a transformed value has nowhere to go.</summary>
internal class ReadOnlyHolder
{
    [DwMask(MaskStrategy.Full)]
    [DwNoOrder]
    public string Secret => "real";
}

/// <summary>
/// Covers the walk itself: the shapes a transformed value can hide behind, and the two ways a walk
/// could quietly leave one untransformed.
/// </summary>
/// <remarks>
/// The output-side counterpart of the navigation defects the input side produced five of. Every
/// shape that broke the attribute walker is exercised here against the result walker.
/// </remarks>
public class PolicyGraphWalkTests
{
    private static DwPolicyContext Caller() => new();

    private static DwPolicyOptions Options() => new() { HashSalt = "pepper" };

    private static TypePolicy PolicyFor<T>() =>
        new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() })
            .ResolveType(typeof(T), Caller());

    private static PolicyTrace Walk<T>(IEnumerable<object> rows, IReadOnlyCollection<string>? projected = null)
    {
        PolicyTrace trace = new(DwTier.Convenience, dryRun: false);

        GraphWalker.Apply(rows, PolicyFor<T>(), projected, Caller(), Options(), trace);

        return trace;
    }

    [Fact]
    public void A_transform_beneath_a_list_reaches_every_element()
    {
        ListInvoice invoice = new()
        {
            Lines = { new MaskedLine { Cost = "100" }, new MaskedLine { Cost = "2000" } }
        };

        Walk<ListInvoice>(new object[] { invoice });

        Assert.Equal("***", invoice.Lines[0].Cost);
        Assert.Equal("****", invoice.Lines[1].Cost);
    }

    [Fact]
    public void A_transform_beneath_an_array_reaches_every_element()
    {
        ArrayInvoice invoice = new()
        {
            Lines = new[] { new MaskedLine { Cost = "100" }, new MaskedLine { Cost = "2000" } }
        };

        Walk<ArrayInvoice>(new object[] { invoice });

        Assert.Equal("***", invoice.Lines[0].Cost);
        Assert.Equal("****", invoice.Lines[1].Cost);
    }

    [Fact]
    public void An_object_shared_by_two_rows_is_transformed_once()
    {
        // Masking twice is harmless; hashing a hash is not, and truncating a truncation is not.
        // Owners are therefore tracked by reference identity rather than by equality.
        MaskedLine shared = new() { Cost = "1234" };

        ListInvoice first = new() { Lines = { shared } };
        ListInvoice second = new() { Lines = { shared } };

        Walk<ListInvoice>(new object[] { first, second });

        Assert.Equal("****", shared.Cost);
    }

    [Fact]
    public void Two_equal_but_distinct_objects_are_both_transformed()
    {
        // The case reference identity exists for. Under value equality these two lines are the same
        // line, so a set keyed on equality keeps one of them and the other keeps its real value --
        // a row returned unmasked because another row happened to hold the same number.
        MaskedLineRecord first = new() { Cost = "1234" };
        MaskedLineRecord second = new() { Cost = "1234" };

        RecordInvoice invoice = new() { Lines = { first, second } };

        Walk<RecordInvoice>(new object[] { invoice });

        Assert.Equal("****", first.Cost);
        Assert.Equal("****", second.Cost);
    }

    [Fact]
    public void An_absent_navigation_is_skipped_rather_than_failing()
    {
        // There is no value beneath a null reference to leave untransformed, so nothing is wrong.
        OptionalHolder holder = new() { Line = null };

        Walk<OptionalHolder>(new object[] { holder });

        Assert.Null(holder.Line);
    }

    [Fact]
    public void A_present_navigation_beneath_an_absent_sibling_is_still_reached()
    {
        OptionalHolder absent = new() { Line = null };
        OptionalHolder present = new() { Line = new MaskedLine { Cost = "77" } };

        Walk<OptionalHolder>(new object[] { absent, present });

        Assert.Equal("**", present.Line!.Cost);
    }

    [Fact]
    public void A_path_the_projection_left_out_is_skipped()
    {
        // The value is not in the result at all, so there is nothing to transform and nothing to
        // leak. Looking for it would fail a query that is perfectly safe.
        ListInvoice invoice = new() { Lines = { new MaskedLine { Cost = "100" } } };

        Walk<ListInvoice>(new object[] { invoice }, new[] { "Id" });

        Assert.Equal("100", invoice.Lines[0].Cost);
    }

    [Fact]
    public void A_path_the_projection_includes_is_reached_through_its_parent()
    {
        ListInvoice invoice = new() { Lines = { new MaskedLine { Cost = "100" } } };

        Walk<ListInvoice>(new object[] { invoice }, new[] { "Lines" });

        Assert.Equal("***", invoice.Lines[0].Cost);
    }

    [Fact]
    public void A_member_that_cannot_be_written_fails_rather_than_emitting_the_real_value()
    {
        // A read-only member would otherwise be read, masked, and the mask discarded -- leaving the
        // real value in the result with nothing to show anything went wrong.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Walk<ReadOnlyHolder>(new object[] { new ReadOnlyHolder() }));

        Assert.Contains("no setter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_row_is_skipped()
    {
        Walk<ListInvoice>(new object?[] { null, new ListInvoice() }!);
    }

    [Fact]
    public void Every_path_that_was_transformed_is_recorded_once()
    {
        ListInvoice first = new() { Lines = { new MaskedLine { Cost = "1" } } };
        ListInvoice second = new() { Lines = { new MaskedLine { Cost = "2" } } };

        PolicyTrace trace = Walk<ListInvoice>(new object[] { first, second });

        Assert.Single(trace.Decisions, d => d.FieldPath == "Lines.Cost");
    }
}
