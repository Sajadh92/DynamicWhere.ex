namespace DynamicWhere.ex.Policies.AspNetCore;

/// <summary>
/// How the administrative surface is mounted, and who may reach it.
/// </summary>
/// <remarks>
/// The authorization policy names are not optional, and that is the point. <c>POST /rules</c>
/// changes what every caller in the application may see, so a surface that mounts without being
/// told who may reach it is one that ships open the first time somebody copies the one-line example
/// from a readme.
/// </remarks>
public sealed class DwPolicyAdminOptions
{
    /// <summary>Where the endpoints are mounted.</summary>
    public string RoutePrefix { get; set; } = "/dw-policies";

    /// <summary>
    /// The ASP.NET authorization policy a caller must satisfy to read: schema, rules, explain,
    /// simulate and health.
    /// </summary>
    public string? ReadPolicy { get; set; }

    /// <summary>
    /// The ASP.NET authorization policy a caller must satisfy to write: upserting and deleting
    /// rules.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReadPolicy"/> rather than defaulting to it. The population who may
    /// inspect a policy is not the population who may change one, and a write policy that silently
    /// inherited the read one would grant every reader the ability to rewrite the application's
    /// access control.
    /// </remarks>
    public string? WritePolicy { get; set; }

    /// <summary>
    /// True to mount the endpoints with no authorization at all.
    /// </summary>
    /// <remarks>
    /// Has to be written by hand, and should only ever be true where something in front of the
    /// application is doing the authorizing. It exists so that "no policy" is a decision somebody
    /// made rather than a field somebody forgot.
    /// </remarks>
    public bool AllowAnonymousAccess { get; set; }

    /// <summary>Which claims describe the caller a simulation or an explanation is run for.</summary>
    public DwClaimsOptions Claims { get; } = new();

    /// <summary>
    /// Refuses a configuration that would mount the surface without deciding who may reach it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no posture was chosen.</exception>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(RoutePrefix))
        {
            throw new InvalidOperationException("The administrative surface requires a route prefix.");
        }

        if (AllowAnonymousAccess)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ReadPolicy) || string.IsNullOrWhiteSpace(WritePolicy))
        {
            throw new InvalidOperationException(
                "The policy administration endpoints refuse to map without an authorization policy. "
                + "Set both ReadPolicy and WritePolicy to the names of policies this application "
                + "registers, or set AllowAnonymousAccess deliberately if something in front of the "
                + "application authorizes instead. POST /rules changes what every caller may see, so "
                + "there is no safe default to fall back to.");
        }
    }
}
