using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests
{
    public readonly record struct ZvText(string Ar, string En);

    /// <summary>A struct holding a struct, a collection and a class navigation.</summary>
    public struct ZvCard
    {
        public ZvText Title { get; set; }

        public string Code { get; set; }

        public List<string> Tags { get; set; }

        public ZvOwner? Owner { get; set; }

        /// <summary>Read-only, so no projection can set it.</summary>
        public int Length => Code?.Length ?? 0;
    }

    public class ZvOwner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZvPerson
    {
        public int Id { get; set; }

        public string NameAr { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public string? AliasAr { get; set; }

        public DateTime Born { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    public class ZvRow
    {
        public int Id { get; set; }

        public ZvText Name { get; set; }

        public ZvText? Alias { get; set; }

        public ZvCard Card { get; set; }

        public DateTime Born { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    /// <summary>A struct with a member no caller may see, beside one anybody may.</summary>
    public struct ZvSecretPair
    {
        public string Shown { get; set; }

        [DwDenied]
        public string Hidden { get; set; }
    }

    public class ZvSecretRow
    {
        public int Id { get; set; }

        public ZvSecretPair Pair { get; set; }
    }

    public sealed class ZvContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZvContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZvPerson> People => Set<ZvPerson>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// A selection naming a path beneath a member whose type is a value. The typed projection skipped
    /// every such member, so it came back as its default with nothing said: <c>Name.Ar</c> over a
    /// struct returned an empty name, guarded or not.
    /// </summary>
    public sealed class ValueMemberSelectTests : IDisposable
    {
        private static readonly DateTime FirstBorn = new(1990, 5, 17);

        private readonly SqliteConnection _connection;
        private readonly ZvContext _db;

        public ValueMemberSelectTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZvContext(_connection);
            _db.Database.EnsureCreated();
            _db.People.Add(new ZvPerson { NameAr = "مالية", NameEn = "Finance", AliasAr = "م", Born = FirstBorn, Code = "FIN" });
            _db.People.Add(new ZvPerson { NameAr = "صحة", NameEn = "Health", AliasAr = null, Born = new DateTime(1985, 1, 2), Code = "HLT" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private IQueryable<ZvRow> Initialized() =>
            _db.People.OrderBy(p => p.Id).Select(p => new ZvRow
            {
                Id = p.Id,
                Name = new ZvText { Ar = p.NameAr, En = p.NameEn },
                Alias = p.AliasAr == null ? null : new ZvText { Ar = p.AliasAr, En = p.NameEn },
                Card = new ZvCard
                {
                    Title = new ZvText { Ar = p.NameAr, En = p.NameEn },
                    Code = p.Code,
                    Tags = new List<string> { p.Code }
                },
                Born = p.Born,
                Code = p.Code
            });

        private IQueryable<ZvRow> Constructed() =>
            _db.People.OrderBy(p => p.Id).Select(p => new ZvRow { Id = p.Id, Name = new ZvText(p.NameAr, p.NameEn) });

        private static List<ZvRow> Pick(IQueryable<ZvRow> rows, params string[] fields) =>
            rows.Select(fields.ToList()).ToList();

        [Fact]
        public void A_member_of_a_struct_comes_back_with_its_value()
        {
            List<ZvRow> rows = Pick(Initialized(), "Name.Ar");

            Assert.Equal(new[] { "مالية", "صحة" }, rows.Select(row => row.Name.Ar));
            Assert.All(rows, row => Assert.Null(row.Name.En));
        }

        [Fact]
        public void Two_members_of_a_struct_come_back_together()
        {
            List<ZvRow> rows = Pick(Initialized(), "Name.Ar", "Name.En");

            Assert.Equal(new[] { "Finance", "Health" }, rows.Select(row => row.Name.En));
            Assert.Equal(new[] { "مالية", "صحة" }, rows.Select(row => row.Name.Ar));
        }

        [Fact]
        public void A_struct_its_constructor_built_is_read_on_the_client()
        {
            List<ZvRow> rows = Pick(Constructed(), "Name.En");

            Assert.Equal(new[] { "Finance", "Health" }, rows.Select(row => row.Name.En));
        }

        [Fact]
        public void A_nullable_struct_is_built_where_it_has_a_value_and_left_null_where_it_has_none()
        {
            // A nullable struct's members are named through Value, as the path validator reads them.
            List<ZvRow> rows = Pick(Initialized(), "Alias.Value.Ar");

            Assert.Equal("م", rows[0].Alias!.Value.Ar);
            Assert.Null(rows[0].Alias!.Value.En);
            Assert.Null(rows[1].Alias);
        }

        [Theory]
        [InlineData("Alias.Value")]
        [InlineData("Alias.HasValue")]
        public void Naming_a_nullable_struct_whole_or_only_its_presence_leaves_it_unbound_as_before(string field)
        {
            // Value names the whole struct, and the policy names the struct's members, not Value's.
            List<ZvRow> rows = Pick(Initialized(), field, "Id");

            Assert.Null(rows[0].Alias);
            Assert.Equal(1, rows[0].Id);
        }

        [Fact]
        public void A_struct_inside_a_struct_is_built_level_by_level()
        {
            List<ZvRow> rows = Pick(Initialized(), "Card.Title.En", "Card.Code");

            Assert.Equal("Finance", rows[0].Card.Title.En);
            Assert.Null(rows[0].Card.Title.Ar);
            Assert.Equal("FIN", rows[0].Card.Code);
        }

        [Fact]
        public void A_collection_inside_a_struct_is_carried()
        {
            List<ZvRow> rows = Pick(Initialized(), "Card.Tags");

            Assert.Equal(new[] { "FIN" }, rows[0].Card.Tags);
        }

        [Fact]
        public void A_member_no_projection_can_set_stays_as_it_was()
        {
            // Length is read-only; the struct is built with what can be set and the rest keeps its default.
            List<ZvRow> rows = Pick(Initialized(), "Card.Length", "Card.Code");

            Assert.Equal("FIN", rows[0].Card.Code);
            Assert.Equal(3, rows[0].Card.Length);
        }

        [Fact]
        public void A_class_navigation_inside_a_struct_stays_unbound_and_its_siblings_are_built()
        {
            List<ZvRow> rows = Pick(Initialized(), "Card.Owner.Name", "Card.Code");

            Assert.Equal("FIN", rows[0].Card.Code);
            Assert.Null(rows[0].Card.Owner);
        }

        [Theory]
        [InlineData("Card.Owner.Name")]
        [InlineData("Card.Length")]
        public void A_struct_nothing_named_in_can_be_set_is_left_unbound_rather_than_failing(string field)
        {
            // Read-only, or a class navigation the struct cannot hand to EF.Property: nothing to bind.
            List<ZvRow> rows = Pick(Initialized(), field, "Id");

            Assert.Null(rows[0].Card.Code);
            Assert.Equal(1, rows[0].Id);
        }

        [Fact]
        public void A_path_beneath_a_framework_type_leaves_the_member_unbound()
        {
            // Born.Year is not a value a typed row can hold apart from the date, and the date whole is
            // more than the path names: a policy allowing Born.Year alone would hand back the day.
            List<ZvRow> rows = Pick(Initialized(), "Born.Year", "Code.Length", "Id");

            Assert.Equal(default, rows[0].Born);
            Assert.NotEqual("FIN", rows[0].Code);
            Assert.Equal(1, rows[0].Id);
        }

        [Fact]
        public void Rows_in_memory_are_read_the_same_way()
        {
            List<ZvRow> source = new()
            {
                new ZvRow { Id = 1, Name = new ZvText("a", "b"), Born = FirstBorn },
            };

            List<ZvRow> rows = source.AsQueryable().Select(new List<string> { "Name.Ar" }).ToList();

            Assert.Equal("a", rows[0].Name.Ar);
            Assert.Null(rows[0].Name.En);
        }

        [Fact]
        public void The_filter_terminal_selects_through_a_struct_as_well()
        {
            FilterResult<ZvRow> result = Initialized().ToList(new Filter { Selects = new List<string> { "Name.Ar" } });

            Assert.Equal("مالية", result.Data[0].Name.Ar);
        }

        // ---- under a policy ------------------------------------------------------------------------------

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_guarded_selection_through_a_struct_returns_the_value(DwTier tier)
        {
            FilterResult<ZvRow> result = Guard(Initialized(), tier)
                .ToList(new Filter { Selects = new List<string> { "Name.Ar" } });

            Assert.Equal("مالية", result.Data[0].Name.Ar);
        }

        [Fact]
        public void A_sibling_of_a_denied_struct_member_comes_back_without_it()
        {
            List<ZvSecretRow> source = new()
            {
                new ZvSecretRow { Id = 1, Pair = new ZvSecretPair { Shown = "open", Hidden = "secret" } }
            };

            FilterResult<ZvSecretRow> result = Guard(source.AsQueryable(), DwTier.Convenience)
                .ToList(new Filter { Selects = new List<string> { "Pair.Shown" } });

            Assert.Equal("open", result.Data[0].Pair.Shown);
            Assert.Null(result.Data[0].Pair.Hidden);
        }

        [Fact]
        public void The_denied_struct_member_itself_is_refused()
        {
            List<ZvSecretRow> source = new()
            {
                new ZvSecretRow { Id = 1, Pair = new ZvSecretPair { Shown = "open", Hidden = "secret" } }
            };

            Assert.Throws<PolicyException>(() => Guard(source.AsQueryable(), DwTier.Strict)
                .ToList(new Filter { Selects = new List<string> { "Pair.Hidden" } }));
        }
    }
}
