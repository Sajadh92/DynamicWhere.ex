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

    private sealed class Timed
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// A last-access time is right to the second. Writing it on every read put every thread querying
    /// one entity type in a queue for the same entry's lock, and eviction only asks which entries are
    /// oldest.
    /// </summary>
    [Fact]
    public void A_read_refreshes_a_last_access_time_only_once_it_is_a_second_old()
    {
        if (CacheReflection.GetCacheConfigOptions().EvictionStrategy
            != DynamicWhere.ex.Optimization.Cache.Enums.CacheEvictionStrategy.LRU)
        {
            return;
        }

        (Type, string) key = (typeof(Timed), "Id");

        CacheReflection.ValidatePropertyPath(typeof(Timed), "Id");

        long first = CacheDatabase.PropertyPathAccessTime[key];

        CacheReflection.ValidatePropertyPath(typeof(Timed), "Id");

        Assert.Equal(first, CacheDatabase.PropertyPathAccessTime[key]);

        // As though it had last been read two seconds ago.
        long stale = first - (2 * CacheDatabase.LruResolutionTicks);

        CacheDatabase.PropertyPathAccessTime[key] = stale;

        CacheReflection.ValidatePropertyPath(typeof(Timed), "Id");

        Assert.True(CacheDatabase.PropertyPathAccessTime[key] > stale + CacheDatabase.LruResolutionTicks);
    }

    /// <summary>
    /// The configuration in force is a copy nobody outside holds, so a caller editing what they were
    /// handed, going in or coming out, changes nothing a lookup reads.
    /// </summary>
    [Fact]
    public void The_configuration_a_caller_holds_is_never_the_one_in_force()
    {
        DynamicWhere.ex.Optimization.Cache.Config.CacheOptions before = CacheReflection.GetCacheConfigOptions();

        DynamicWhere.ex.Optimization.Cache.Config.CacheOptions handed = CacheReflection.GetCacheConfigOptions();

        handed.MaxCacheSize = before.MaxCacheSize + 123;

        Assert.Equal(before.MaxCacheSize, CacheReflection.GetCacheConfigOptions().MaxCacheSize);
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
