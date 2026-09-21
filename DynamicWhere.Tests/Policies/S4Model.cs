using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 4 (security). The model the S4 probes query.
    //
    // EF Core 6 compatible on purpose, so the floor leg runs every probe: no complex properties,
    // no ToJson, no primitive collections, no DateOnly/TimeOnly, no compiled model.
    // =============================================================================================

    /// <summary>An owned type with a getter over two of its own columns: no database computes it.</summary>
    public class S4Money
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public bool IsZero => Amount == 0m;
    }

    public class S4Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<S4Order> Orders { get; set; } = new();

        /// <summary>Unmapped: a getter over a column.</summary>
        [NotMapped]
        public string Handle => Name + "!";
    }

    public class S4Line
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public decimal Price { get; set; }
    }

    public class S4Order
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int Qty { get; set; }

        public int CustomerId { get; set; }

        public S4Customer Customer { get; set; } = null!;

        public S4Money Total { get; set; } = new();

        public List<S4Line> Lines { get; set; } = new();

        /// <summary>Denied outright, to prove the provider test gates nothing but Expresses.</summary>
        [DynamicWhere.ex.Policies.Attributes.DwDeny(DynamicWhere.ex.Policies.Enums.PolicyFeature.All)]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Unmapped: a getter over two columns.</summary>
        [NotMapped]
        public string Slug => Code + "-" + Id;
    }

    // ---- row types a caller projects into -------------------------------------------------------

    public class S4Nest
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;

        public S4Money Money { get; set; } = new();

        public bool Blank => A.Length == 0;
    }

    public class S4Row
    {
        public S4Row()
        {
        }

        public S4Row(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? Tag { get; set; }

        public S4Nest Nest { get; set; } = new();

        public S4Money Money { get; set; } = new();

        public S4Customer? Customer { get; set; }
    }

    /// <summary>A row whose members differ only in letter case, to probe path collisions.</summary>
    public class S4CaseRow
    {
        public int Id { get; set; }

        public S4Nest Value { get; set; } = new();

#pragma warning disable IDE1006
        public string value { get; set; } = string.Empty;
#pragma warning restore IDE1006
    }

    /// <summary>A row whose member names are prefixes of one another, to probe <c>Beneath</c>.</summary>
    public class S4PrefixRow
    {
        public int Id { get; set; }

        public S4Nest A { get; set; } = new();

        public string AB { get; set; } = string.Empty;
    }

    public sealed class S4Context : DbContext
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        private readonly Type? _replacementProvider;

        public S4Context(Microsoft.Data.Sqlite.SqliteConnection connection, Type? replacementProvider = null)
        {
            _connection = connection;
            _replacementProvider = replacementProvider;
        }

        public DbSet<S4Order> Orders => Set<S4Order>();

        public DbSet<S4Customer> Customers => Set<S4Customer>();

        public DbSet<S4Line> Lines => Set<S4Line>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite(_connection);

            if (_replacementProvider is not null)
            {
                // The documented EF Core extension point a host uses to put its own query provider
                // in place: ReplaceService<IAsyncQueryProvider, …>.
                typeof(DbContextOptionsBuilder)
                    .GetMethods()
                    .Single(m => m.Name == nameof(DbContextOptionsBuilder.ReplaceService)
                                 && m.GetGenericArguments().Length == 2
                                 && m.GetParameters().Length == 0)
                    .MakeGenericMethod(typeof(IAsyncQueryProvider), _replacementProvider)
                    .Invoke(options, null);
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<S4Order>().OwnsOne(order => order.Total);
            model.Entity<S4Order>().Ignore(order => order.Slug);
            model.Entity<S4Customer>().Ignore(customer => customer.Handle);
        }
    }

    /// <summary>
    /// A plain pass-through built the way a host builds one: derived from EF Core's own provider,
    /// registered through <c>ReplaceService&lt;IAsyncQueryProvider, …&gt;</c>, rewriting nothing.
    /// </summary>
    public sealed class S4PassThroughProvider : EntityQueryProvider
    {
        public S4PassThroughProvider(IQueryCompiler compiler)
            : base(compiler)
        {
        }
    }

    /// <summary>
    /// The base a spoofed provider inherits, so the spoof itself needs nothing but a name: every
    /// member forwards to the provider it stands in front of.
    /// </summary>
    public abstract class S4ForwardingProvider : IQueryProvider
    {
        public IQueryProvider Inner { get; set; } = null!;

        public IQueryable CreateQuery(Expression expression) => Inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new S4SpoofedQueryable<TElement>(this, Inner.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => Inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => Inner.Execute<TResult>(expression);
    }

    /// <summary>A queryable whose <c>Provider</c> is the spoof and whose rows are the inner one's.</summary>
    public sealed class S4SpoofedQueryable<T> : IQueryable<T>
    {
        private readonly IQueryable<T> _inner;

        public S4SpoofedQueryable(IQueryProvider provider, IQueryable<T> inner)
        {
            Provider = provider;
            _inner = inner;
        }

        public Type ElementType => typeof(T);

        public Expression Expression => _inner.Expression;

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
