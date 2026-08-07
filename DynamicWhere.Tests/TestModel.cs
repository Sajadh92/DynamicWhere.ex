using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

/// <summary>
/// Reference navigation target used to test paths that continue past a collection element
/// (e.g. <c>Tags.Author.Name</c>).
/// </summary>
public class Author
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

/// <summary>
/// Collection element carrying a value — the shape that triggered the original bug:
/// a <c>List&lt;Tag&gt;</c> ordered by <c>Tags.Value</c>.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    public string? Value { get; set; }

    /// <summary>Non-nullable value type, used to cover the empty-collection aggregate case.</summary>
    public int Score { get; set; }

    public int PostId { get; set; }

    public Post? Post { get; set; }

    public int? AuthorId { get; set; }

    public Author? Author { get; set; }
}

/// <summary>
/// Entity owning a collection of <see cref="Tag"/>.
/// </summary>
public class Post
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int BlogId { get; set; }

    public Blog? Blog { get; set; }

    public List<Tag> Tags { get; set; } = new();
}

/// <summary>
/// Root entity used for paths that cross two collection levels (<c>Posts.Tags.Value</c>).
/// </summary>
public class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? OwnerId { get; set; }

    public Author? Owner { get; set; }

    public List<Post> Posts { get; set; } = new();

    /// <summary>Collection of simple values; excluded from the EF model and tested in-memory only.</summary>
    public List<string> Labels { get; set; } = new();
}

/// <summary>
/// SQLite-backed context used to verify the generated ordering expressions translate to SQL.
/// </summary>
public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options) : base(options) { }

    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Author> Authors => Set<Author>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>().Ignore(b => b.Labels);
    }
}
