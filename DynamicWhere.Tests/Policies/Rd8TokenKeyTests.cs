using System.Security.Cryptography;
using System.Text;
using DynamicWhere.ex.Policies.Tokens;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// A vault that holds a key stores a mapping under an HMAC of the value rather than a plain digest
    /// of it, so a copy of the store gives no value back.
    /// </summary>
    /// <remarks>
    /// A tokenized column is nearly always drawn from a small space: a phone number, a national
    /// identifier, a card number. Every value in such a space can be hashed, so a store keyed by a
    /// plain SHA-256 of the value is a copy of the column to whoever reads it.
    /// </remarks>
    public sealed class Rd8TokenKeyTests
    {
        private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] Other = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

        private static string Sha256(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        [Fact]
        public void A_keyed_key_holds_no_plain_digest_of_the_value()
        {
            string keyed = DwToken.KeyFor("Customer.Phone", "07701234567", Key);

            Assert.StartsWith("hmac:Customer.Phone:", keyed, StringComparison.Ordinal);
            Assert.Equal(64, keyed.Substring("hmac:Customer.Phone:".Length).Length);
            Assert.DoesNotContain(Sha256("07701234567"), keyed, StringComparison.Ordinal);
            Assert.NotEqual(DwToken.KeyFor("Customer.Phone", "07701234567"), keyed);
        }

        /// <summary>What the attack is: with no key, the digest of a guessed value is the stored key.</summary>
        [Fact]
        public void The_unkeyed_key_is_the_digest_anyone_can_compute_and_the_keyed_one_is_not()
        {
            string guess = "07701234567";

            Assert.Equal("Customer.Phone:" + Sha256(guess), DwToken.KeyFor("Customer.Phone", guess));

            // Everything an attacker holding the store can compute without the key.
            string[] computable =
            {
                Sha256(guess),
                Sha256("Customer.Phone" + guess),
                Sha256("Customer.Phone:" + guess),
                Sha256("Customer.Phone\0" + guess),
            };

            string keyed = DwToken.KeyFor("Customer.Phone", guess, Key);

            Assert.DoesNotContain(computable, digest => keyed.EndsWith(digest, StringComparison.Ordinal));
        }

        [Fact]
        public void The_same_key_gives_the_same_key_and_another_key_gives_another()
        {
            Assert.Equal(DwToken.KeyFor("s", "v", Key), DwToken.KeyFor("s", "v", (byte[])Key.Clone()));
            Assert.NotEqual(DwToken.KeyFor("s", "v", Key), DwToken.KeyFor("s", "v", Other));
            Assert.NotEqual(DwToken.KeyFor("s", "v", Key), DwToken.KeyFor("s", "w", Key));
        }

        /// <summary>The scope is inside the digest, so the store does not show that two fields hold one value.</summary>
        [Fact]
        public void One_value_in_two_scopes_shares_no_digest()
        {
            string first = DwToken.KeyFor("Customer.Phone", "07701234567", Key);
            string second = DwToken.KeyFor("Supplier.Phone", "07701234567", Key);

            Assert.NotEqual(first.Substring(first.Length - 64), second.Substring(second.Length - 64));

            // And where the scope ends and the value begins is part of what is hashed.
            string joined = DwToken.KeyFor("ab", "c", Key);
            string split = DwToken.KeyFor("a", "bc", Key);

            Assert.NotEqual(joined.Substring(joined.Length - 64), split.Substring(split.Length - 64));
        }

        [Fact]
        public void A_key_that_is_absent_or_short_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => DwToken.KeyFor("s", "v", null!));
            Assert.Throws<ArgumentException>(() => DwToken.KeyFor("s", "v", new byte[DwToken.MinimumKeyLength - 1]));
            Assert.NotNull(DwToken.KeyFor("s", "v", new byte[DwToken.MinimumKeyLength]));
            Assert.Throws<ArgumentException>(() => DwToken.KeyFor(" ", "v", Key));
            Assert.Throws<ArgumentNullException>(() => DwToken.KeyFor("s", null!, Key));
            Assert.Throws<ArgumentNullException>(() => DwToken.RequireKey(null!));
            Assert.Throws<ArgumentException>(() => DwToken.RequireKey(new byte[3]));
        }

        [Fact]
        public void A_vault_keeps_its_own_copy_of_the_key()
        {
            byte[] handed = (byte[])Key.Clone();
            byte[] kept = DwToken.RequireKey(handed);

            Array.Clear(handed);

            Assert.NotSame(handed, kept);
            Assert.Equal(Key, kept);
        }

        /// <summary>The process-scoped vault draws a key of its own, so nothing has to be configured for it.</summary>
        [Fact]
        public void The_in_memory_vault_holds_no_plain_digest()
        {
            InMemoryTokenVault vault = new();

            string token = vault.GetOrCreate("Customer.Phone", "07701234567");

            string held = Assert.Single(vault.Keys);

            Assert.StartsWith("hmac:Customer.Phone:", held, StringComparison.Ordinal);
            Assert.DoesNotContain(Sha256("07701234567"), held, StringComparison.Ordinal);
            Assert.Equal(token, vault.GetOrCreate("Customer.Phone", "07701234567"));

            // Two vaults are two keys: what one holds says nothing about the other.
            InMemoryTokenVault second = new();

            second.GetOrCreate("Customer.Phone", "07701234567");

            Assert.NotEqual(held, Assert.Single(second.Keys));
        }
    }
}
