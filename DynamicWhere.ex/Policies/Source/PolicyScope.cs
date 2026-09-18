using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// Tracks whether a policy context is in force on the current logical call, so that a type marked
/// <c>[DwEntity(RequirePolicy = true)]</c> can refuse to be read without one.
/// </summary>
/// <remarks>
/// Without this check the opt-in handle is a suggestion rather than a boundary: every field policy
/// on a type is bypassed by simply not calling <c>ApplyPolicy</c>, and nothing in a code review
/// distinguishes the query that forgot from the query that never needed one.
/// <para>
/// The context is ambient because the guarded path delegates to the very extension methods being
/// guarded. The alternative — splitting each of them into a checked public half and an unchecked
/// internal one — would rewrite most of a file this project otherwise leaves alone, and those
/// methods call one another internally, so the split would have to reach all the way down.
/// </para>
/// <para>
/// <see cref="AsyncLocal{T}"/> rather than <c>ThreadStatic</c>: a scope has to survive an
/// <c>await</c> that resumes on another thread, which the async query methods do. Values flow into
/// awaited work and not back out of it, and every scope is restored in a <c>finally</c>, so the
/// window in which the flag is set is exactly one call.
/// </para>
/// </remarks>
internal static class PolicyScope
{
    private static readonly AsyncLocal<DwPolicyContext?> Ambient = new();

    private static readonly AsyncLocal<PolicyTrace?> Names = new();

    /// <summary>The context in force, or null when the current call is unguarded.</summary>
    internal static DwPolicyContext? Current => Ambient.Value;

    /// <summary>
    /// Marks the current logical call as guarded until the returned scope is disposed.
    /// </summary>
    /// <param name="context">The caller.</param>
    /// <param name="trace">
    /// The record of the sanitization that produced the query, which knows the names the caller
    /// wrote. Null where there is none.
    /// </param>
    internal static Scope Enter(DwPolicyContext context, PolicyTrace? trace = null) => new(context, trace);

    /// <summary>
    /// The name the caller wrote for a canonical field path, inside a guarded call; the path itself
    /// anywhere else.
    /// </summary>
    /// <remarks>
    /// For a refusal the query engine raises after sanitization, such as a date it cannot read. By
    /// then an aliased field has been rewritten to its canonical path, and naming that path back to a
    /// caller who only ever used the alias would disclose the internal name the alias exists to hide.
    /// </remarks>
    internal static string Spoken(string fieldPath) => Names.Value?.Spoken(fieldPath) ?? fieldPath;

    /// <summary>
    /// Refuses to read a type that requires a policy context when none is in force.
    /// </summary>
    /// <typeparam name="T">The type being queried.</typeparam>
    /// <exception cref="PolicyException">
    /// Thrown when <typeparamref name="T"/> is marked <c>[DwEntity(RequirePolicy = true)]</c> and
    /// the call is not inside a policy scope.
    /// </exception>
    internal static void Require<T>()
    {
        if (Ambient.Value is not null)
        {
            return;
        }

        if (!RequirementCache<T>.Required)
        {
            return;
        }

        throw new PolicyException(
            PolicyErrorCode.PolicyRequired,
            typeof(T).Name,
            PolicyFeature.None,
            DwTier.Strict)
        {
            SourceOrigin = "DwEntityAttribute(RequirePolicy = true)"
        };
    }

    /// <summary>
    /// Reads the requirement once per type.
    /// </summary>
    /// <remarks>
    /// A static generic holds the answer for the life of the process without a dictionary lookup.
    /// This runs on every call into the library, guarded or not, so it has to cost nothing on the
    /// overwhelmingly common path where the type says nothing.
    /// </remarks>
    private static class RequirementCache<T>
    {
        internal static readonly bool Required =
            Attribute.GetCustomAttribute(typeof(T), typeof(DwEntityAttribute)) is DwEntityAttribute
            {
                RequirePolicy: true
            };
    }

    /// <summary>
    /// Holds a policy scope open, restoring whatever was in force when it is disposed.
    /// </summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly DwPolicyContext? _previous;
        private readonly PolicyTrace? _previousNames;

        internal Scope(DwPolicyContext context, PolicyTrace? trace)
        {
            _previous = Ambient.Value;
            _previousNames = Names.Value;
            Ambient.Value = context;
            Names.Value = trace;
        }

        /// <summary>Restores the previous scope.</summary>
        public void Dispose()
        {
            Ambient.Value = _previous;
            Names.Value = _previousNames;
        }
    }
}
