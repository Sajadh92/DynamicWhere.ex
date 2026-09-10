using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Supplies policy fragments for a type and a caller. Implemented once over reflection for
/// compile-time attributes and once over the policy store for runtime rules; the resolver merges
/// whatever it is given without knowing which is which.
/// </summary>
/// <remarks>
/// Implementations are called on the query path and must not perform I/O. A store-backed provider
/// reads an in-memory snapshot that a background refresh keeps current.
/// </remarks>
public interface IDwPolicyProvider
{
    /// <summary>
    /// Returns every fragment this provider has for the given type and caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="context">The caller.</param>
    IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context);
}
