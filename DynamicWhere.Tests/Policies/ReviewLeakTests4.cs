using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    // ---- P10b: an interface declares the member; the implementing class denies it -------------------------

    public interface IZrAccount
    {
        int Id { get; }

        string Holder { get; }

        string? Iban { get; }
    }

    public class ZrAccountEntity : IZrAccount
    {
        public int Id { get; set; }

        public string Holder { get; set; } = string.Empty;

        [DwDenied]
        public string? Iban { get; set; }
    }

    public class ZrStatement
    {
        public int Id { get; set; }

        public IZrAccount? Account { get; set; }
    }

    public sealed class ZrProbeContext4 : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZrProbeContext4(SqliteConnection connection) => _connection = connection;

        public DbSet<ZrAccountEntity> Accounts => Set<ZrAccountEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class ReviewLeakTests4 : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext4 _db;

        public ReviewLeakTests4()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZrProbeContext4(_connection);
            _db.Database.EnsureCreated();
            _db.Accounts.Add(new ZrAccountEntity { Holder = "H", Iban = "iban-secret" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static bool Leaks(Func<IEnumerable<object?>> read)
        {
            try
            {
                return read().Any(row => row is ZrAccountEntity { Iban: "iban-secret" }
                                         || row is ZrStatement { Account: ZrAccountEntity { Iban: "iban-secret" } });
            }
            catch (PolicyException refusal) when (refusal.ErrorCode is PolicyErrorCode.FieldDeniedForSelect or PolicyErrorCode.AllSelectsDenied)
            {
                return false;
            }
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10b_rows_in_memory_read_through_the_interface_that_declares_the_denied_member(DwTier tier)
        {
            IZrAccount[] rows = { new ZrAccountEntity { Id = 1, Holder = "H", Iban = "iban-secret" } };

            Assert.False(Leaks(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10b_an_entity_query_read_through_the_interface(DwTier tier)
        {
            IQueryable<IZrAccount> source = _db.Accounts;

            Exception? unguarded = Record.Exception(() => source.AsNoTracking().ToList());

            if (unguarded is not null)
            {
                // EF Core cannot run the query through the interface at all.
                return;
            }

            Assert.False(Leaks(() => Guard(source, tier).ToList(new Filter()).Data));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P10b_a_member_typed_as_the_interface(DwTier tier)
        {
            ZrStatement[] rows = { new() { Id = 1, Account = new ZrAccountEntity { Id = 2, Holder = "H", Iban = "iban-secret" } } };

            Assert.False(Leaks(() => Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data));
            Assert.False(Leaks(() => Guard(rows.AsQueryable(), tier).ToList(new Filter { Selects = new List<string> { "Id", "Account" } }).Data));
        }

        [Fact]
        public void Zr_P10b_control_the_class_itself_is_policed()
        {
            ZrAccountEntity[] rows = { new() { Id = 1, Holder = "H", Iban = "iban-secret" } };

            Assert.False(Leaks(() => Guard(rows.AsQueryable()).ToList(new Filter()).Data));
        }
    }
}
