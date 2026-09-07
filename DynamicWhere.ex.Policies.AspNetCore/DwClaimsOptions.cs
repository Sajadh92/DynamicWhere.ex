using System.Security.Claims;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.AspNetCore;

/// <summary>
/// Which claims describe the caller, and which of them supply the ambient values a forced predicate
/// reads.
/// </summary>
/// <remarks>
/// Configurable rather than fixed because claim types are not standard in practice: one identity
/// provider spells the subject <c>sub</c> and another <c>nameidentifier</c>, and a tenant is
/// whatever the host decided to call it. Guessing wrong here does not fail loudly — it produces a
/// caller with fewer subjects than they have, which is a set of rules that silently do not apply.
/// </remarks>
public sealed class DwClaimsOptions
{
    /// <summary>The claim types naming the caller, tried in order.</summary>
    public IList<string> UserClaimTypes { get; } =
        new List<string> { ClaimTypes.NameIdentifier, "sub" };

    /// <summary>The claim types naming the caller's roles.</summary>
    public IList<string> RoleClaimTypes { get; } =
        new List<string> { ClaimTypes.Role, "role", "roles" };

    /// <summary>The claim types naming the caller's tenant.</summary>
    public IList<string> TenantClaimTypes { get; } =
        new List<string> { "tenant", "tenant_id", "tid" };

    /// <summary>Extra claim types, each mapped to the kind of subject it describes.</summary>
    /// <remarks>
    /// For a dimension this library does not name — a business unit, a region, a customer segment.
    /// Mapped to <see cref="DwSubjectKind.Custom"/> unless the host says otherwise.
    /// </remarks>
    public IDictionary<string, DwSubjectKind> SubjectClaimTypes { get; } =
        new Dictionary<string, DwSubjectKind>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ambient values, as a map from the key a <c>[DwForceWhere]</c> reads to the claim type that
    /// supplies it.
    /// </summary>
    /// <remarks>
    /// Failing to map one is fail-closed and loud: a forced predicate whose context value is
    /// missing refuses the query with <c>MissingContextValue</c> rather than quietly injecting
    /// nothing. That is the whole reason the value is read here rather than defaulted somewhere.
    /// </remarks>
    public IDictionary<string, string> ValueClaimTypes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True to build a context for a principal that is not authenticated.
    /// </summary>
    /// <remarks>
    /// False by default. An unauthenticated caller carries no user and no role, so every rule
    /// written for a subject silently fails to match and only global rules apply — which is a
    /// coherent posture for a public read surface and an accident everywhere else. Refusing by
    /// default means the accident is a startup-time decision rather than a quiet one.
    /// </remarks>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// The purpose to bind every context to, or null to leave it unset.
    /// </summary>
    /// <remarks>
    /// A constant rather than a claim: a purpose describes what a request is for, which the caller's
    /// identity does not know. A host serving several purposes sets it per request instead.
    /// </remarks>
    public string? Purpose { get; set; }
}
