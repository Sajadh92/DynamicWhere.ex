using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ variant generic interfaces: models

    public class ZxBaseCard
    {
        public string Label { get; set; } = string.Empty;
    }

    public class ZxDerivedCard : ZxBaseCard
    {
        public string Tier { get; set; } = string.Empty;
    }

    // ---- covariant, implemented only by an open generic class over a closed subtype argument -------------------

    /// <summary>A covariant interface: an IZxOpenVar&lt;ZxDerivedCard&gt; is an IZxOpenVar&lt;ZxBaseCard&gt;.</summary>
    public interface IZxOpenVar<out T>
    {
        T Value { get; }

        string? Code { get; }
    }

    /// <summary>Every instantiation of this class is an IZxOpenVar&lt;ZxBaseCard&gt; through covariance.</summary>
    public class ZxOpenVarImpl<TTag> : IZxOpenVar<ZxDerivedCard>
    {
        public ZxDerivedCard Value { get; set; } = new();

        [DwDenied]
        public string? Code { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    public class ZxOpenVarRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public IZxOpenVar<ZxBaseCard>? Tagged { get; set; }
    }

    // ---- covariant, implemented only by a CLOSED class over a subtype argument ----

    public interface IZxClosedVar<out T>
    {
        T Value { get; }

        string? Code { get; }
    }

    public class ZxClosedVarImpl : IZxClosedVar<ZxDerivedCard>
    {
        public ZxDerivedCard Value { get; set; } = new();

        [DwDenied]
        public string? Code { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    public class ZxClosedVarRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public IZxClosedVar<ZxBaseCard>? Tagged { get; set; }
    }

    // ---- contravariant, implemented by a closed class over a BASE argument ----

    public interface IZxSink<in T>
    {
        string? Code { get; }

        void Accept(T item);
    }

    public class ZxBaseSink : IZxSink<ZxBaseCard>
    {
        [DwDenied]
        public string? Code { get; set; }

        public void Accept(ZxBaseCard item)
        {
        }
    }

    public class ZxSinkRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public IZxSink<ZxDerivedCard>? Sink { get; set; }
    }

    // ---- controls: the member typed as the exact instantiation implemented ----

    public class ZxExactOpenRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public IZxOpenVar<ZxDerivedCard>? Tagged { get; set; }
    }

    // ============================================================================ variant generic interfaces: tests

    /// <summary>
    /// A member typed as a variant interface holds implementations of other instantiations, which the subtype index
    /// and the walker read as the runtime assigns them, covariant and contravariant alike.
    /// </summary>
    public sealed class ReviewVarianceTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewVarianceTests(ITestOutputHelper output) => _out = output;

        private static Summary GroupedBy(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { field },
                AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
            }
        };

        private void Report(string label, Type type)
        {
            _out.WriteLine($"{label}: Of({type.Name}<{type.GetGenericArguments()[0].Name}>) = "
                           + string.Join(", ", KnownSubtypes.Of(type).Select(t => t.Name)));
        }

        [Fact]
        public void Zx_V0_the_types_are_assignable_through_variance()
        {
            Assert.True(typeof(IZxOpenVar<ZxBaseCard>).IsAssignableFrom(typeof(ZxOpenVarImpl<int>)));
            Assert.True(typeof(IZxClosedVar<ZxBaseCard>).IsAssignableFrom(typeof(ZxClosedVarImpl)));
            Assert.True(typeof(IZxSink<ZxDerivedCard>).IsAssignableFrom(typeof(ZxBaseSink)));

            Exception? map = Record.Exception(() => typeof(ZxOpenVarImpl<>).GetInterfaceMap(typeof(IZxOpenVar<ZxBaseCard>)));
            Exception? closedMap = Record.Exception(() => typeof(ZxClosedVarImpl).GetInterfaceMap(typeof(IZxClosedVar<ZxBaseCard>)));

            _out.WriteLine($"GetInterfaceMap(ZxOpenVarImpl<>, IZxOpenVar<ZxBaseCard>): {map?.GetType().Name ?? "ok"}");
            _out.WriteLine($"GetInterfaceMap(ZxClosedVarImpl, IZxClosedVar<ZxBaseCard>): {closedMap?.GetType().Name ?? "ok"}");
        }

        // ------------------------------------------------------------------------ the subtype index

        [Fact]
        public void Zx_V1_open_the_index_lists_an_open_generic_implementation_over_a_subtype_argument()
        {
            Report("open", typeof(IZxOpenVar<ZxBaseCard>));

            Assert.Contains(typeof(ZxOpenVarImpl<>), KnownSubtypes.Of(typeof(IZxOpenVar<ZxBaseCard>)));
        }

        [Fact]
        public void Zx_V1_closed_the_index_lists_a_closed_implementation_over_a_subtype_argument()
        {
            Report("closed", typeof(IZxClosedVar<ZxBaseCard>));

            Assert.Contains(typeof(ZxClosedVarImpl), KnownSubtypes.Of(typeof(IZxClosedVar<ZxBaseCard>)));
        }

        [Fact]
        public void Zx_V1_contra_the_index_lists_a_contravariant_implementation()
        {
            Report("contra", typeof(IZxSink<ZxDerivedCard>));

            Assert.Contains(typeof(ZxBaseSink), KnownSubtypes.Of(typeof(IZxSink<ZxDerivedCard>)));
        }

        // ------------------------------------------------------------------------ the walker's fragment on the path

        [Fact]
        public void Zx_V2_control_the_exact_instantiation_carries_the_implementations_denial()
        {
            Assert.NotEmpty(ZxKit.Denials(typeof(ZxExactOpenRow), "Tagged.Code"));
        }

        [Fact]
        public void Zx_V2_open_the_covariant_path_carries_the_implementations_denial()
        {
            _out.WriteLine("open Tagged.Code denials: " + ZxKit.Denials(typeof(ZxOpenVarRow), "Tagged.Code").Count());

            Assert.NotEmpty(ZxKit.Denials(typeof(ZxOpenVarRow), "Tagged.Code"));
        }

        [Fact]
        public void Zx_V2_closed_the_covariant_path_carries_the_implementations_denial()
        {
            _out.WriteLine("closed Tagged.Code denials: " + ZxKit.Denials(typeof(ZxClosedVarRow), "Tagged.Code").Count());

            Assert.NotEmpty(ZxKit.Denials(typeof(ZxClosedVarRow), "Tagged.Code"));
        }

        [Fact]
        public void Zx_V2_contra_the_contravariant_path_carries_the_implementations_denial()
        {
            _out.WriteLine("contra Sink.Code denials: " + ZxKit.Denials(typeof(ZxSinkRow), "Sink.Code").Count());

            Assert.NotEmpty(ZxKit.Denials(typeof(ZxSinkRow), "Sink.Code"));
        }

        // ------------------------------------------------------------------------ where, group, order on the denied path

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V3_open_where_group_and_order_on_the_denied_code_are_refused(DwTier tier)
        {
            ZxOpenVarRow[] rows = { new() { Id = 1, Name = "r1", Tagged = new ZxOpenVarImpl<int> { Code = "open-code-secret" } } };

            string where = ZxKit.Code(() => ZxKit.Guard(rows.AsQueryable(), tier).ToList(ZxKit.Where("Tagged.Code", "open-code-secret")));
            object? grouped = null;
            string group = ZxKit.Code(() => grouped = ZxKit.Guard(rows.AsQueryable(), tier).ToList(GroupedBy("Tagged.Code")).Data);
            string order = ZxKit.Code(() => ZxKit.Guard(rows.AsQueryable(), tier).ToList(new Filter
            {
                Orders = new List<OrderBy> { new() { Field = "Tagged.Code" } }, Selects = new List<string> { "Id" }
            }));

            _out.WriteLine($"open {tier}: where={where} group={group} ({ZxKit.Json(grouped)}) order={order}");

            Assert.NotEqual("ran", where);
            Assert.NotEqual("ran", group);

            if (tier == DwTier.Strict)
            {
                Assert.NotEqual("ran", order);
            }
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V3_closed_where_group_and_order_on_the_denied_code_are_refused(DwTier tier)
        {
            ZxClosedVarRow[] rows = { new() { Id = 1, Name = "r1", Tagged = new ZxClosedVarImpl { Code = "closed-code-secret" } } };

            string where = ZxKit.Code(() => ZxKit.Guard(rows.AsQueryable(), tier).ToList(ZxKit.Where("Tagged.Code", "closed-code-secret")));
            object? grouped = null;
            string group = ZxKit.Code(() => grouped = ZxKit.Guard(rows.AsQueryable(), tier).ToList(GroupedBy("Tagged.Code")).Data);

            _out.WriteLine($"closed {tier}: where={where} group={group} ({ZxKit.Json(grouped)})");

            Assert.NotEqual("ran", where);
            Assert.NotEqual("ran", group);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V3_contra_where_on_the_denied_code_is_refused(DwTier tier)
        {
            ZxSinkRow[] rows = { new() { Id = 1, Name = "r1", Sink = new ZxBaseSink { Code = "contra-code-secret" } } };

            string where = ZxKit.Code(() => ZxKit.Guard(rows.AsQueryable(), tier).ToList(ZxKit.Where("Sink.Code", "contra-code-secret")));

            _out.WriteLine($"contra {tier}: where={where}");

            Assert.NotEqual("ran", where);
        }

        // ------------------------------------------------------------------------ rows returned with no projection asked for

        [Theory]
        [InlineData(DwTier.Strict, false)]
        [InlineData(DwTier.Convenience, false)]
        [InlineData(DwTier.Strict, true)]
        public void Zx_V4_open_rows_do_not_return_the_implementations_denied_members(DwTier tier, bool dynamic)
        {
            ZxOpenVarRow[] rows =
            {
                new() { Id = 1, Name = "r1", Tagged = new ZxOpenVarImpl<int> { Code = "open-code-secret", Secret = "open-own-secret" } }
            };

            PolicyQueryable<ZxOpenVarRow> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => dynamic ? guarded.ToListDynamic(new Filter()).Data : guarded.ToList(new Filter()).Data);

            _out.WriteLine($"open {tier} dynamic={dynamic}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "open-code-secret"));
            Assert.False(ZxKit.Holds(data, "open-own-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V4_closed_rows_do_not_return_the_implementations_denied_members(DwTier tier)
        {
            ZxClosedVarRow[] rows =
            {
                new() { Id = 1, Name = "r1", Tagged = new ZxClosedVarImpl { Code = "closed-code-secret", Secret = "closed-own-secret" } }
            };

            PolicyQueryable<ZxClosedVarRow> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"closed {tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "closed-code-secret"));
            Assert.False(ZxKit.Holds(data, "closed-own-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V4_contra_rows_do_not_return_the_implementations_denied_members(DwTier tier)
        {
            ZxSinkRow[] rows = { new() { Id = 1, Name = "r1", Sink = new ZxBaseSink { Code = "contra-code-secret" } } };

            PolicyQueryable<ZxSinkRow> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"contra {tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "contra-code-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_V4_control_rows_typed_as_the_exact_instantiation(DwTier tier)
        {
            ZxExactOpenRow[] rows =
            {
                new() { Id = 1, Name = "r1", Tagged = new ZxOpenVarImpl<int> { Code = "exact-code-secret", Secret = "exact-own-secret" } }
            };

            PolicyQueryable<ZxExactOpenRow> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"exact {tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "exact-code-secret"));
            Assert.False(ZxKit.Holds(data, "exact-own-secret"));
        }
    }
}
