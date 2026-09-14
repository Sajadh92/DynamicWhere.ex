using System.Security.Claims;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.AspNetCore;

/// <summary>
/// Turns a <see cref="ClaimsPrincipal"/> into the caller a policy is resolved against.
/// </summary>
/// <remarks>
/// Design section 3.4 writes this as <c>DwPolicyContext.FromClaims(principal)</c>. It ships here
/// instead — as it says it should — and a static on a type in another assembly is not something C#
/// allows, so the shape is an adapter plus an extension method on the principal.
/// </remarks>
public static class DwClaimsAdapter
{
    /// <summary>
    /// Builds a context describing the caller, and prepares it against every configured store.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="options">Which claims describe what.</param>
    /// <param name="ct">Cancels the store reads.</param>
    /// <returns>A prepared context, ready for its first query.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the principal is not authenticated and the options do not allow it.
    /// </exception>
    /// <remarks>
    /// Prepared here rather than left to the caller, because a context that skipped preparation is
    /// refused by every store provider that sees it — so an adapter returning an unprepared one
    /// would hand back something that cannot be used, once per request, forever.
    /// </remarks>
    public static async ValueTask<DwPolicyContext> CreateContextAsync(
        ClaimsPrincipal principal, DwClaimsOptions options, CancellationToken ct = default) =>
        await DwPolicy.PrepareAsync(FromClaims(principal, options), ct).ConfigureAwait(false);

    /// <summary>
    /// Builds a context describing the caller, without preparing it.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="options">Which claims describe what.</param>
    /// <returns>The context.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the principal is not authenticated and the options do not allow it.
    /// </exception>
    /// <remarks>
    /// For a host with no policy store, where there is nothing to prepare. Anything reading a store
    /// should use <see cref="CreateContextAsync"/>.
    /// </remarks>
    public static DwPolicyContext FromClaims(ClaimsPrincipal principal, DwClaimsOptions options)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.AllowAnonymous && principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException(
                "The principal is not authenticated, so it carries no user and no role — every rule "
                + "written for a subject would silently fail to match and only global rules would "
                + "apply. Set DwClaimsOptions.AllowAnonymous if that is the posture you want.");
        }

        DwPolicyContext context = new() { Purpose = options.Purpose };

        Add(context, principal, options.UserClaimTypes, DwSubjectKind.User);
        Add(context, principal, options.RoleClaimTypes, DwSubjectKind.Role);
        Add(context, principal, options.TenantClaimTypes, DwSubjectKind.Tenant);

        foreach (KeyValuePair<string, DwSubjectKind> extra in options.SubjectClaimTypes)
        {
            Add(context, principal, new[] { extra.Key }, extra.Value);
        }

        foreach (KeyValuePair<string, string> value in options.ValueClaimTypes)
        {
            string? found = principal.FindFirst(value.Value)?.Value;

            // Absent rather than null. A forced predicate reading a key nothing supplies refuses the
            // query; one reading a key supplied as null would inject a comparison against nothing,
            // which matches no row and looks like an empty result rather than a misconfiguration.
            if (found is not null)
            {
                context.WithValue(value.Key, found);
            }
        }

        return context;
    }

    /// <summary>
    /// Adds every identity the caller holds under any of a set of claim types.
    /// </summary>
    /// <remarks>
    /// Every claim of every listed type, not the first of the first type that matches. A caller
    /// holding three roles holds three, and taking one would drop the denials attached to the other
    /// two — multi-role conflict resolves to the strictest, which needs all of them present to
    /// resolve at all.
    /// </remarks>
    private static void Add(
        DwPolicyContext context,
        ClaimsPrincipal principal,
        IEnumerable<string> claimTypes,
        DwSubjectKind kind)
    {
        foreach (string claimType in claimTypes)
        {
            foreach (Claim claim in principal.FindAll(claimType))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    context.WithSubject(kind, claim.Value);
                }
            }
        }
    }
}

/// <summary>
/// Convenience extensions over <see cref="DwClaimsAdapter"/>.
/// </summary>
public static class ClaimsPrincipalPolicyExtensions
{
    /// <summary>
    /// Builds and prepares a policy context for this principal.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="options">Which claims describe what.</param>
    /// <param name="ct">Cancels the store reads.</param>
    public static ValueTask<DwPolicyContext> ToPolicyContextAsync(
        this ClaimsPrincipal principal, DwClaimsOptions options, CancellationToken ct = default) =>
        DwClaimsAdapter.CreateContextAsync(principal, options, ct);
}
