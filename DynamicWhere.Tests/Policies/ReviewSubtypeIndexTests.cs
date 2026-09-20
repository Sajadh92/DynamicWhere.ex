using System.Collections;
using System.Diagnostics;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ the subtype index: models

    public class ZvIntBox : ZvBox<int>
    {
    }

    public class ZvStringBox : ZvBox<string>
    {
    }

    public class ZvMidBox<T> : ZvBox<T>
    {
    }

    public class ZvLeafBox : ZvMidBox<int>
    {
    }

    /// <summary>An open generic type whose ancestor is closed: it can never be a ZvBox&lt;int&gt;.</summary>
    public class ZvFixedBox<TOther> : ZvBox<string>
    {
        [DwNoGroup]
        public override string? Code
        {
            get => base.Code;
            set => base.Code = value;
        }
    }

    public class ZvDerivedHolder<T> : ZvHolderImpl<T>
    {
    }

    /// <summary>The base type a late-loaded assembly derives from; nothing else in this assembly does.</summary>
    public class ZvLateBase
    {
        public int Id { get; set; }

        public virtual string? Code { get; set; }
    }

    public class ZvJobRow
    {
        public int Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public Exception? Error { get; set; }
    }

    // ============================================================================ the subtype index: tests

    public sealed class ReviewSubtypeIndexTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewSubtypeIndexTests(ITestOutputHelper output) => _out = output;

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static int Denials(Type type, string path, PolicyFeature feature) =>
            new AttributePolicyProvider().GetFragments(type, new DwPolicyContext())
                .Count(f => f.Effect == PolicyEffect.Deny && f.FieldPath == path && (f.Features & feature) != 0);

        // ------------------------------------------------------------------------------------------------ C1

        [Fact]
        public void Zv_C1_generic_subtypes_are_indexed_under_the_instantiations_they_can_be()
        {
            IReadOnlyList<Type> ofIntBox = KnownSubtypes.Of(typeof(ZvBox<int>));
            IReadOnlyList<Type> ofHolder = KnownSubtypes.Of(typeof(IZvHolder<int>));

            _out.WriteLine("Of(ZvBox<int>): " + string.Join(", ", ofIntBox.Select(t => t.Name)));
            _out.WriteLine("Of(IZvHolder<int>): " + string.Join(", ", ofHolder.Select(t => t.Name)));

            Assert.Contains(typeof(ZvIntBox), ofIntBox);
            Assert.Contains(typeof(ZvLeafBox), ofIntBox);
            Assert.Contains(typeof(ZvSecretBox<>), ofIntBox);
            Assert.Contains(typeof(ZvMidBox<>), ofIntBox);
            Assert.DoesNotContain(typeof(ZvStringBox), ofIntBox);

            Assert.Contains(typeof(ZvHolderImpl<>), ofHolder);
            Assert.Contains(typeof(ZvDerivedHolder<>), ofHolder);
            Assert.Contains(typeof(ZvGenPlainHolder<>), KnownSubtypes.Of(typeof(IZvPlainHolder)));
        }

        /// <summary>What the interface map says for an open generic implementation and a closed interface.</summary>
        [Fact]
        public void Zv_C1_the_interface_map_of_an_open_generic_implementation_for_a_closed_interface()
        {
            Exception? closed = Record.Exception(() => typeof(ZvHolderImpl<>).GetInterfaceMap(typeof(IZvHolder<int>)));
            Exception? open = Record.Exception(() => typeof(ZvHolderImpl<>).GetInterfaceMap(typeof(ZvHolderImpl<>).GetInterfaces()[0]));

            _out.WriteLine($"GetInterfaceMap(ZvHolderImpl<>, IZvHolder<int>): {closed?.GetType().Name ?? "ok"} {closed?.Message}");
            _out.WriteLine($"GetInterfaceMap(ZvHolderImpl<>, IZvHolder<T>): {open?.GetType().Name ?? "ok"}");

            Assert.NotNull(closed);
            Assert.Null(open);
        }

        /// <summary>
        /// An open generic type whose ancestor is a different closed instantiation is not a subtype of this one,
        /// so its override's denial does not reach a path no row of it can be on.
        /// </summary>
        [Fact]
        public void Zv_C1_an_open_generic_type_over_another_closed_instantiation_is_not_its_subtype()
        {
            _out.WriteLine("Of(ZvBox<int>) holds ZvFixedBox<>: " + KnownSubtypes.Of(typeof(ZvBox<int>)).Contains(typeof(ZvFixedBox<>)));
            _out.WriteLine("ZvBox<int>.Code group denials: " + Denials(typeof(ZvBox<int>), "Code", PolicyFeature.Group));
            _out.WriteLine("ZvBox<string>.Code group denials: " + Denials(typeof(ZvBox<string>), "Code", PolicyFeature.Group));

            // ZvFixedBox<T> derives from ZvBox<string> only; a ZvBox<int> is never one, and a ZvBox<string> can be.
            Assert.DoesNotContain(typeof(ZvFixedBox<>), KnownSubtypes.Of(typeof(ZvBox<int>)));
            Assert.Contains(typeof(ZvFixedBox<>), KnownSubtypes.Of(typeof(ZvBox<string>)));
        }
    }
}
