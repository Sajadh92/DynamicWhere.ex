using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace DynamicWhere.API;

/// <summary>
/// The one authentication scheme this demo has: a header stands in for whatever a real deployment
/// would put in front of the policy administration endpoints.
/// </summary>
/// <remarks>
/// A scheme rather than a bare authorization assertion, for two reasons. Authorization that fails
/// with no scheme registered cannot challenge, so ASP.NET Core throws and the caller sees a 500
/// where a 401 belongs. And the policy layer reads the caller from the principal: an unauthenticated
/// request carries no user and no role, so every rule written for a subject would fail to match and
/// <c>DwClaimsAdapter</c> refuses it rather than resolving a caller who is nobody.
/// <para>
/// A deployment replaces this with its own scheme — JWT bearer, cookies, mutual TLS — and keeps the
/// shape: an authenticated principal carrying the role the authorization policies name.
/// </para>
/// </remarks>
public sealed class DemoAdminAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name, as registered and as named by the authorization policies.</summary>
    public const string SchemeName = "DwDemoAdmin";

    /// <summary>The role a caller presenting the header is given.</summary>
    public const string Role = "policy-admin";

    /// <summary>The header carrying the demo credential.</summary>
    public const string Header = "X-Dw-Admin";

    private const string Secret = "demo";

    public DemoAdminAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Authenticates a request carrying the demo header, and stays out of the way otherwise.
    /// </summary>
    /// <remarks>
    /// <see cref="AuthenticateResult.NoResult"/> rather than a failure for a request without the
    /// header: nothing was presented, so there is nothing to reject, and every other endpoint in
    /// this application is anonymous by design. A wrong value is a failure, because something was
    /// presented and it was not accepted.
    /// </remarks>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!string.Equals(value, Secret, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail($"The {Header} header is not valid."));
        }

        Claim[] claims =
        {
            new(ClaimTypes.NameIdentifier, "demo-operator"),
            new(ClaimTypes.Role, Role)
        };

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
