using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;

namespace DynamicWhere.Tests;

/// <summary>What validating a field path leaves behind in the reflection cache.</summary>
public class PathValidationCacheTests
{
    private sealed class Probe
    {
        public int Id { get; set; }
    }

    [Fact]
    public void A_path_that_fails_validation_leaves_no_access_record()
    {
        // A failing path adds no cache entry for eviction to remove, so an access record for it stayed
        // for the life of the process: a caller sending invented names grew the process without limit.
        string invented = "NoSuchMember_" + Guid.NewGuid().ToString("N");
        (Type, string) key = (typeof(Probe), invented);

        Assert.Throws<LogicException>(() => CacheReflection.ValidatePropertyPath(typeof(Probe), invented));

        Assert.False(CacheDatabase.PropertyPathAccessTime.ContainsKey(key));
        Assert.False(CacheDatabase.PropertyPathAccessCount.ContainsKey(key));
    }

    [Fact]
    public void A_path_that_validates_is_still_tracked()
    {
        string path = "Id";

        Assert.Equal("Id", CacheReflection.ValidatePropertyPath(typeof(Probe), path));

        Assert.True(
            CacheDatabase.PropertyPathAccessTime.ContainsKey((typeof(Probe), path))
            || CacheDatabase.PropertyPathAccessCount.ContainsKey((typeof(Probe), path)));
    }
}
