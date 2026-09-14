namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The enforcement posture, fixed once at startup.
/// </summary>
public enum DwTier
{
    /// <summary>
    /// The caller is the application's own front end. A blocked sort or projection is dropped
    /// quietly; a blocked filter still throws, because dropping a filter widens the result set.
    /// </summary>
    Convenience = 0,

    /// <summary>
    /// The caller is hostile or semi-trusted. Every blocked request throws.
    /// </summary>
    Strict = 1
}
