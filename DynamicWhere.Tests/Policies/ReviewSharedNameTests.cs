using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ other declarations: models

    /// <summary>A base type whose denied member a subtype overrides with a covariant return type (C# 9).</summary>
    public class ZxCovBase
    {
        protected ZxBaseCard? CardValue;

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public virtual ZxBaseCard? Card => CardValue;

        public void Put(ZxBaseCard card) => CardValue = card;
    }

    public class ZxCovDerived : ZxCovBase
    {
        public override ZxDerivedCard? Card => CardValue as ZxDerivedCard;
    }

    /// <summary>The same with a scalar beneath, the denial on the abstract base declaration.</summary>
    public abstract class ZxCovAbstract
    {
        public int Id { get; set; }

        [DwDenied]
        public abstract ZxBaseCard? Card { get; }
    }

    public class ZxCovConcrete : ZxCovAbstract
    {
        public ZxDerivedCard? Held { private get; set; }

        public override ZxDerivedCard? Card => Held;
    }

    // ---- a default interface member that a derived interface overrides explicitly, with the denial there ----

    public interface IZxCoded
    {
        string? Code { get; }
    }

    public interface IZxSecretCoded : IZxCoded
    {
        string? Raw { get; }

        [DwDenied]
        string? IZxCoded.Code => Raw;
    }

    public class ZxCodedImpl : IZxSecretCoded
    {
        public string? Raw { get; set; }
    }

    public class ZxCodedRow
    {
        public int Id { get; set; }

        public IZxCoded? Coded { get; set; }
    }

    // ---- T itself hides a base member with new, typed as an object holding a denied field ----

    public class ZxHideBase
    {
        public int Id { get; set; }

        public string? Info { get; set; }
    }

    public class ZxHideDerived : ZxHideBase
    {
        public new ZxPayCard? Info { get; set; }
    }

    /// <summary>The same beside a top-level denial, so a projection is built.</summary>
    public class ZxHideDerivedDenied : ZxHideBase
    {
        [DwDenied]
        public string? Ssn { get; set; }

        public new ZxPayCard? Info { get; set; }
    }

    // ============================================================================ other declarations: tests

    public sealed class ReviewSharedNameTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewSharedNameTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Zx_D1_a_covariant_override_keeps_the_base_declarations_denial()
        {
            Assert.NotEmpty(ZxKit.Denials(typeof(ZxCovDerived), "Card"));
            Assert.NotEmpty(ZxKit.Denials(typeof(ZxCovConcrete), "Card"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_D1_rows_of_the_covariant_subtype_do_not_return_the_denied_member(DwTier tier)
        {
            ZxCovDerived row = new() { Id = 1, Name = "r1" };
            row.Put(new ZxDerivedCard { Label = "covariant-card-secret" });
            ZxCovConcrete concrete = new() { Id = 2, Held = new ZxDerivedCard { Label = "abstract-covariant-secret" } };

            PolicyQueryable<ZxCovDerived> guarded = ZxKit.Guard(new[] { row }.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            PolicyQueryable<ZxCovConcrete> guarded2 = ZxKit.Guard(new[] { concrete }.AsQueryable(), tier);
            object? data2 = ZxKit.SafeRead(() => guarded2.ToList(new Filter()).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data2)} trace=[{ZxKit.Trace(guarded2)}]");

            Assert.False(ZxKit.Holds(data, "covariant-card-secret"));
            Assert.False(ZxKit.Holds(data2, "abstract-covariant-secret"));
        }

        [Fact]
        public void Zx_D4_a_denial_on_a_derived_interfaces_explicit_default_implementation()
        {
            ZxCodedRow[] rows = { new() { Id = 1, Coded = new ZxCodedImpl { Raw = "dim-code-secret" } } };

            _out.WriteLine("runs: " + ((IZxCoded)rows[0].Coded!).Code);
            _out.WriteLine("Coded.Code denials: " + ZxKit.Denials(typeof(ZxCodedRow), "Coded.Code").Count());

            string where = ZxKit.Code(() => ZxKit.Guard(rows.AsQueryable()).ToList(ZxKit.Where("Coded.Code", "dim-code-secret")));
            object? data = ZxKit.SafeRead(() => ZxKit.Guard(rows.AsQueryable()).ToList(new Filter()).Data);

            _out.WriteLine($"where={where} sent={ZxKit.Json(data)}");

            Assert.NotEqual("ran", where);
            Assert.False(ZxKit.Holds(data, "dim-code-secret") && ZxKit.Json(data).Contains("dim-code-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_D5_a_row_type_that_hides_a_value_member_with_an_object_holding_a_denied_field(DwTier tier)
        {
            PropertyInfo[] infos = typeof(ZxHideDerived).GetProperties().Where(p => p.Name == "Info").ToArray();

            _out.WriteLine("GetProperties order: " + string.Join(", ", infos.Select(p => $"{p.DeclaringType!.Name}.{p.Name}:{p.PropertyType.Name}")));
            _out.WriteLine("Info.Pan denials: " + ZxKit.Denials(typeof(ZxHideDerived), "Info.Pan").Count());

            ZxHideDerived[] rows = { new() { Id = 1, Info = new ZxPayCard { Label = "visa", Pan = "hide-pan-secret" } } };
            ZxHideDerivedDenied[] denied = { new() { Id = 2, Ssn = "hide-ssn", Info = new ZxPayCard { Label = "visa", Pan = "hide-pan-secret-2" } } };

            PolicyQueryable<ZxHideDerived> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(new Filter()).Data);
            object? named = ZxKit.SafeRead(() => ZxKit.Guard(rows.AsQueryable(), tier).ToList(ZxKit.Selecting("Id", "Info")).Data);
            PolicyQueryable<ZxHideDerivedDenied> guarded2 = ZxKit.Guard(denied.AsQueryable(), tier);
            object? data2 = ZxKit.SafeRead(() => guarded2.ToList(new Filter()).Data);

            _out.WriteLine($"{tier} none: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");
            _out.WriteLine($"{tier} named: sent={ZxKit.Json(named)}");
            _out.WriteLine($"{tier} projected: sent={ZxKit.Json(data2)} trace=[{ZxKit.Trace(guarded2)}]");

            Assert.False(ZxKit.Holds(data, "hide-pan-secret"));
            Assert.False(ZxKit.Holds(named, "hide-pan-secret"));
            Assert.False(ZxKit.Holds(data2, "hide-pan-secret-2"));
        }
    }
}
