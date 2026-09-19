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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// A member that no declaration a row can run through the queried path denies stays filterable and is returned:
// a sibling implementer's denial, a private or static member of the same name, an explicit implementation.

namespace DynamicWhere.Tests.Policies
{
    // ---- D1: one implementer of a widely shared interface denies its implementation -------------------------

    public interface IZydAuditable
    {
        string? CreatedBy { get; }
    }

    public class ZydInvoice : IZydAuditable
    {
        public int Id { get; set; }

        public string? CreatedBy { get; set; }
    }

    public class ZydPayslip : IZydAuditable
    {
        public int Id { get; set; }

        [DwDenied]
        public string? CreatedBy { get; set; }
    }

    // ---- D2: a subclass hides the member with a private one -----------------------------------------------

    public class ZydNoteDto
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    public class ZydRedactedNoteDto : ZydNoteDto
    {
        [DwDenied]
        private new string Text { get; set; } = string.Empty;

        public void Seal(string text) => Text = text;
    }

    // ---- D3: a subclass declares a static member of the same name -------------------------------------------

    public class ZydRateDto
    {
        public int Id { get; set; }

        public decimal Rate { get; set; }
    }

    public class ZydRateDefaultsDto : ZydRateDto
    {
        [DwDenied]
        public static new decimal Rate { get; set; }
    }

    // ---- D4: a subclass implements an interface explicitly with a denied member of the same name -----------

    public interface IZydCoded
    {
        string Code { get; }
    }

    public class ZydBadgeDto
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    public class ZydSecretBadgeDto : ZydBadgeDto, IZydCoded
    {
        [DwDenied]
        string IZydCoded.Code => "explicit-secret";
    }

    // ---- D5: a class with its own public member and an explicit implementation of a denying interface ------

    public interface IZydSerialed
    {
        [DwDenied]
        string Serial { get; }
    }

    public class ZydDevice : IZydSerialed
    {
        public int Id { get; set; }

        public string Serial { get; set; } = string.Empty;

        string IZydSerialed.Serial => "device-internal-serial";
    }

    // ---- D6: a sibling variant hides the member with new to withhold it in its own shape (documented limit) --

    public class ZydEmployeeDto
    {
        public int Id { get; set; }

        public decimal Salary { get; set; }
    }

    public class ZydPublicEmployeeDto : ZydEmployeeDto
    {
        [DwDenied]
        public new decimal Salary { get; set; }
    }

    // ---- D7: an entity, and a generic interface one unrelated generic class implements over another argument --

    public interface IZydKeyed<TKey>
    {
        TKey Key { get; }
    }

    public class ZydTicket : IZydKeyed<int>
    {
        public int Id { get; set; }

        public int Key { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    public class ZydVaultEntry<T> : IZydKeyed<string>
    {
        [DwDenied]
        public string Key { get; set; } = string.Empty;

        public T? Value { get; set; }
    }

    public sealed class ZydContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZydContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZydInvoice> Invoices => Set<ZydInvoice>();

        public DbSet<ZydTicket> Tickets => Set<ZydTicket>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class ReviewDeclarationPrecisionTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZydContext _db;

        public ReviewDeclarationPrecisionTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZydContext(_connection);
            _db.Database.EnsureCreated();
            _db.Invoices.Add(new ZydInvoice { CreatedBy = "alice" });
            _db.Tickets.Add(new ZydTicket { Key = 7, Title = "T" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field,
                        DataType = decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _) ? DataType.Number : DataType.Text,
                        Operator = Operator.Equal,
                        Values = { value }
                    }
                }
            }
        };

        /// <summary>Filters on the member, then reads the rows whole; logs both and returns them.</summary>
        private (string Filtered, List<T> Rows, PolicyQueryable<T> Guarded) Probe<T>(string label, IQueryable<T> source, string field, string value)
            where T : class
        {
            string filtered;

            try
            {
                filtered = "ran rows=" + Guard(source).ToList(Where(field, value)).Data.Count;
            }
            catch (PolicyException refusal)
            {
                filtered = refusal.ErrorCode.ToString();
            }

            PolicyQueryable<T> guarded = Guard(source);
            List<T> rows = guarded.ToList(new Filter()).Data;

            ZyLog.Write(_out, $"D {label}: Where({field})={filtered}; whole: dropped=[{ZyLog.Decisions(guarded)}]");

            return (filtered, rows, guarded);
        }

        [Fact]
        public void Zy_D1_an_entity_implementing_a_shared_interface_is_not_denied_by_a_sibling_implementer()
        {
            (string filtered, List<ZydInvoice> rows, PolicyQueryable<ZydInvoice> guarded) =
                Probe("D1 entity beside a denying sibling implementer", _db.Invoices, "CreatedBy", "alice");

            Assert.Equal("ran rows=1", filtered);
            Assert.Equal("alice", rows.Single().CreatedBy);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }

        [Fact]
        public void Zy_D2_a_private_member_a_subclass_hides_with_is_never_run_through_the_base_path()
        {
            ZydNoteDto[] notes = { new() { Id = 1, Text = "hello" } };

            (string filtered, List<ZydNoteDto> rows, PolicyQueryable<ZydNoteDto> guarded) =
                Probe("D2 private new in a subclass", notes.AsQueryable(), "Text", "hello");

            Assert.Equal("ran rows=1", filtered);
            Assert.Equal("hello", rows.Single().Text);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }

        [Fact]
        public void Zy_D3_a_static_member_of_the_same_name_is_not_a_declaration_a_row_runs()
        {
            ZydRateDto[] rates = { new() { Id = 1, Rate = 2.5m } };

            (string filtered, List<ZydRateDto> rows, PolicyQueryable<ZydRateDto> guarded) =
                Probe("D3 static new in a subclass", rates.AsQueryable(), "Rate", "2.5");

            Assert.StartsWith("ran", filtered);
            Assert.Equal(2.5m, rows.Single().Rate);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }

        [Fact]
        public void Zy_D4_an_explicit_implementation_in_a_subclass_does_not_deny_the_base_member()
        {
            ZydBadgeDto[] badges = { new() { Id = 1, Code = "B1" } };

            (string filtered, List<ZydBadgeDto> rows, PolicyQueryable<ZydBadgeDto> guarded) =
                Probe("D4 explicit implementation in a subclass", badges.AsQueryable(), "Code", "B1");

            Assert.Equal("ran rows=1", filtered);
            Assert.Equal("B1", rows.Single().Code);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }

        [Fact]
        public void Zy_D5_a_public_member_beside_an_explicit_implementation_of_a_denying_interface()
        {
            ZydDevice[] devices = { new() { Id = 1, Serial = "S1" } };

            (string filtered, List<ZydDevice> rows, PolicyQueryable<ZydDevice> guarded) =
                Probe("D5 public member beside an explicit denying implementation", devices.AsQueryable(), "Serial", "S1");

            Assert.Equal("ran rows=1", filtered);
            Assert.Equal("S1", rows.Single().Serial);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }

        [Fact]
        public void Zy_D7_an_entity_implementing_a_generic_interface_is_not_denied_by_an_open_generic_over_another_argument()
        {
            (string filtered, List<ZydTicket> rows, PolicyQueryable<ZydTicket> guarded) =
                Probe("D7 entity beside an open generic implementer of IZydKeyed<string>", _db.Tickets, "Title", "T");

            Assert.Equal("ran rows=1", filtered);
            Assert.Equal(7, rows.Single().Key);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
        }
    }
}
