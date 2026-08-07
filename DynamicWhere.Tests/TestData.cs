namespace DynamicWhere.Tests;

/// <summary>
/// Builds the object graph shared by the ordering tests.
/// </summary>
/// <remarks>
/// Tag values per post — <c>Post 30</c> deliberately has no tags so the empty-collection path is covered:
/// <list type="bullet">
///   <item><description>Post 10 (Blog 1) — zebra/5 (bob), mango/3 (ann) → min "mango"/3, max "zebra"/5</description></item>
///   <item><description>Post 20 (Blog 2) — apple/9 (carl) → min and max "apple"/9</description></item>
///   <item><description>Post 30 (Blog 3) — no tags → min and max null (0 for <c>Score</c>)</description></item>
/// </list>
/// </remarks>
internal static class TestData
{
    /// <summary>
    /// Three blogs, each owning exactly one post.
    /// </summary>
    public static List<Blog> Blogs()
    {
        Author ann = new() { Id = 1, Name = "ann" };
        Author bob = new() { Id = 2, Name = "bob" };
        Author carl = new() { Id = 3, Name = "carl" };

        List<Post> posts = Posts();

        return
        [
            new Blog { Id = 1, Name = "alpha", OwnerId = ann.Id, Owner = ann, Posts = [posts[0]], Labels = ["q", "a"] },
            new Blog { Id = 2, Name = "beta", OwnerId = carl.Id, Owner = carl, Posts = [posts[1]], Labels = ["m"] },
            new Blog { Id = 3, Name = "gamma", OwnerId = bob.Id, Owner = bob, Posts = [posts[2]], Labels = [] }
        ];
    }

    /// <summary>
    /// Three posts. Post 30 carries an empty tag collection.
    /// </summary>
    public static List<Post> Posts()
    {
        Author ann = new() { Id = 1, Name = "ann" };
        Author bob = new() { Id = 2, Name = "bob" };
        Author carl = new() { Id = 3, Name = "carl" };

        return
        [
            new Post
            {
                Id = 10, Name = "p-ten", BlogId = 1,
                Tags =
                [
                    new Tag { Id = 1, Value = "zebra", Score = 5, PostId = 10, AuthorId = bob.Id, Author = bob },
                    new Tag { Id = 2, Value = "mango", Score = 3, PostId = 10, AuthorId = ann.Id, Author = ann }
                ]
            },
            new Post
            {
                Id = 20, Name = "p-twenty", BlogId = 2,
                Tags =
                [
                    new Tag { Id = 3, Value = "apple", Score = 9, PostId = 20, AuthorId = carl.Id, Author = carl }
                ]
            },
            new Post { Id = 30, Name = "p-thirty", BlogId = 3, Tags = [] }
        ];
    }

    /// <summary>
    /// The three authors, for seeding the relational store.
    /// </summary>
    public static List<Author> Authors() =>
    [
        new Author { Id = 1, Name = "ann" },
        new Author { Id = 2, Name = "bob" },
        new Author { Id = 3, Name = "carl" }
    ];
}
