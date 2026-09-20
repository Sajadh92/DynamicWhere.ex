using System.ComponentModel.DataAnnotations.Schema;
using DynamicWhere.ex.Policies.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 5 (documentation against code). The model the Dv5 probes query.
    //
    // EF Core 6 compatible on purpose, so the floor leg runs every probe: no complex properties,
    // no ToJson, no primitive collections, no DateOnly/TimeOnly, no compiled model.
    // =============================================================================================

    /// <summary>An owned type with a getter over two of its own columns: no database computes it.</summary>
    public class Dv5Money
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        public bool IsZero => Amount == 0m;
    }

    public class Dv5Line
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public decimal Price { get; set; }
    }

    public class Dv5Order
    {
        public int Id { get; set; }

        /// <summary>Audited, so a refusal that records no event can be told from one that does.</summary>
        [DwAudit]
        public string Code { get; set; } = string.Empty;

        public int Qty { get; set; }

        public Dv5Money Total { get; set; } = new();

        public List<Dv5Line> Lines { get; set; } = new();

        /// <summary>Unmapped: a getter over two columns, which no database computes.</summary>
        [NotMapped]
        public string Slug => Code + "-" + Id;
    }

    // ---- row types a caller projects into -------------------------------------------------------

    public class Dv5Nest
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;

        public bool Blank => A.Length == 0;
    }

    public class Dv5LineRow
    {
        public int Id { get; set; }

        public decimal Price { get; set; }
    }

    public class Dv5Row
    {
        public Dv5Row()
        {
        }

        /// <summary>The constructor the ctor-with-arguments probes call.</summary>
        public Dv5Row(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public Dv5Nest Nest { get; set; } = new();

        public Dv5Money Money { get; set; } = new();

        public List<Dv5LineRow> Lines { get; set; } = new();
    }

    /// <summary>A value a condition carries, so a clone's sharing of it can be seen by reference.</summary>
    public sealed class Dv5Box
    {
        public Dv5Box(string text) => Text = text;

        public string Text { get; }

        public override string ToString() => Text;
    }

    // ---- the audited entity ----------------------------------------------------------------------

    /// <summary>Two audited fields, so one use fills a one-slot buffer and the next hits the cap.</summary>
    public class Dv5Watched
    {
        public int Id { get; set; }

        [DwAudit]
        public string First { get; set; } = string.Empty;

        [DwAudit]
        public string Second { get; set; } = string.Empty;

        [DwAudit]
        public int Amount { get; set; }

        /// <summary>Not audited, and denied for filtering: the refusal a real denial gives.</summary>
        [DwDeny(DynamicWhere.ex.Policies.Enums.PolicyFeature.Where)]
        public string Sealed { get; set; } = string.Empty;
    }

    public sealed class Dv5WatchedContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public Dv5WatchedContext(SqliteConnection connection) => _connection = connection;

        public DbSet<Dv5Watched> Rows => Set<Dv5Watched>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class Dv5Context : DbContext
    {
        private readonly SqliteConnection _connection;
        private readonly Type? _replacedProvider;
        private readonly bool _replacePreprocessor;

        public Dv5Context(
            SqliteConnection connection,
            Type? replacedProvider = null,
            bool replacePreprocessor = false)
        {
            _connection = connection;
            _replacedProvider = replacedProvider;
            _replacePreprocessor = replacePreprocessor;
        }

        public DbSet<Dv5Order> Orders => Set<Dv5Order>();

        public DbSet<Dv5Line> Lines => Set<Dv5Line>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite(_connection);

            if (_replacedProvider is not null)
            {
                // The documented EF Core extension point a host uses to put its own query provider
                // in place: ReplaceService<IAsyncQueryProvider, …>.
                typeof(DbContextOptionsBuilder)
                    .GetMethods()
                    .Single(m => m.Name == nameof(DbContextOptionsBuilder.ReplaceService)
                                 && m.GetGenericArguments().Length == 2
                                 && m.GetParameters().Length == 0)
                    .MakeGenericMethod(typeof(IAsyncQueryProvider), _replacedProvider)
                    .Invoke(options, null);
            }

            if (_replacePreprocessor)
            {
                // A rewrite *inside* EF Core's own pipeline: the query is preprocessed by a type of
                // the host's, and EF Core's own provider stays in front of it.
                options.ReplaceService<IQueryTranslationPreprocessorFactory, Dv5PreprocessorFactory>();
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Dv5Order>().OwnsOne(order => order.Total);
            model.Entity<Dv5Order>().Ignore(order => order.Slug);
        }
    }

    /// <summary>A host's own preprocessor factory, registered inside EF Core's pipeline.</summary>
    public sealed class Dv5PreprocessorFactory
        : Microsoft.EntityFrameworkCore.Query.Internal.RelationalQueryTranslationPreprocessorFactory
    {
        public Dv5PreprocessorFactory(
            QueryTranslationPreprocessorDependencies dependencies,
            RelationalQueryTranslationPreprocessorDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }
    }

    /// <summary>
    /// A plain pass-through built the way a host builds one: derived from EF Core's own provider,
    /// registered through <c>ReplaceService&lt;IAsyncQueryProvider, …&gt;</c>, rewriting nothing.
    /// </summary>
    public sealed class Dv5PassThroughProvider : Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider
    {
        public Dv5PassThroughProvider(Microsoft.EntityFrameworkCore.Query.Internal.IQueryCompiler compiler)
            : base(compiler)
        {
        }
    }
}
