using System.Globalization;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A row with one number a caller may filter on and one they may not.</summary>
    public class Rd8Priced
    {
        public int Id { get; set; }

        public decimal Price { get; set; }

        public int? Stock { get; set; }

        [DwDenied]
        public decimal Cost { get; set; }
    }

    /// <summary>
    /// Under a policy a number value is read as it is without one: what the parser cannot read, or
    /// cannot compare with the member, is a format error in both tiers, and the gate still answers
    /// for a denied member before any value is read.
    /// </summary>
    public sealed class Rd8NumberValueTests
    {
        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyQueryable<Rd8Priced> Guarded(DwTier tier) =>
            new[] { new Rd8Priced { Id = 1, Price = 1.5m, Stock = 3, Cost = 1m } }.AsQueryable().ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, object value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                {
                    new Condition { Sort = 1, Field = field, DataType = DataType.Number, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        [Theory]
        [InlineData(DwTier.Strict, "Price", "1,5")]
        [InlineData(DwTier.Convenience, "Price", "1,5")]
        [InlineData(DwTier.Strict, "Price", "NaN")]
        [InlineData(DwTier.Convenience, "Price", "NaN")]
        [InlineData(DwTier.Strict, "Price", "1e5")]
        [InlineData(DwTier.Convenience, "Price", "1e5")]
        [InlineData(DwTier.Strict, "Stock", "1.5")]
        [InlineData(DwTier.Convenience, "Stock", "1.5")]
        public void A_value_the_parser_refuses_is_a_format_error_in_both_tiers(DwTier tier, string field, string value)
        {
            LogicException refusal = Assert.Throws<LogicException>(() => Guarded(tier).ToList(Where(field, value)));

            Assert.Equal(ErrorCode.InvalidFormat, refusal.Message);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_value_the_parser_reads_runs_on_any_host(DwTier tier)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                Assert.Equal(1, Assert.Single(Guarded(tier).ToList(Where("Price", "1.5")).Data!).Id);
                Assert.Equal(1, Assert.Single(Guarded(tier).ToList(Where("Stock", 3)).Data!).Id);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        /// <summary>The gate decides before a value is read, so a malformed value learns nothing about a denied member.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_denied_member_is_refused_before_its_value_is_read(DwTier tier)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guarded(tier).ToList(Where("Cost", "NaN")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }
    }
}
