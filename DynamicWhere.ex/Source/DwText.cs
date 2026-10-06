using System.Reflection;
using DynamicWhere.ex.Enums;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.ex.Source;

/// <summary>
/// How the case-insensitive text operators match, for the process.
/// </summary>
/// <remarks>
/// With nothing configured they match as they always have, by lowering both sides. A deployment on
/// PostgreSQL whose text columns carry a <c>pg_trgm</c> index chooses <see cref="TextMatching.ILike"/>, so
/// that <c>IContains</c> and the other pattern operators can use it.
/// </remarks>
public sealed class DwTextOptions
{
    private TextMatching _caseInsensitive = TextMatching.Lower;

    /// <summary>How <c>IContains</c>, <c>IStartsWith</c>, <c>IEndsWith</c> and their negations match.</summary>
    /// <exception cref="InvalidOperationException">Thrown when set after the options are configured.</exception>
    public TextMatching CaseInsensitive
    {
        get => _caseInsensitive;
        set
        {
            if (IsFrozen)
            {
                throw new InvalidOperationException("Text options cannot be changed once configured.");
            }

            _caseInsensitive = value;
        }
    }

    /// <summary>True once these options have been handed to <see cref="DwText.Configure(DwTextOptions)"/>.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// <c>NpgsqlDbFunctionsExtensions.ILike(DbFunctions, string, string, string)</c>, resolved when the options
    /// are frozen with <see cref="TextMatching.ILike"/>, and null otherwise.
    /// </summary>
    internal MethodInfo? ILike { get; private set; }

    /// <summary>
    /// Checks the options and prevents any further change.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="CaseInsensitive"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="TextMatching.ILike"/> is chosen and Npgsql's EF Core provider cannot be loaded.
    /// </exception>
    internal void Freeze()
    {
        if (IsFrozen)
        {
            return;
        }

        if (!Enum.IsDefined(typeof(TextMatching), _caseInsensitive))
        {
            throw new ArgumentException(
                $"'{(int)_caseInsensitive}' is not a TextMatching value. Choose Lower or ILike.",
                nameof(CaseInsensitive));
        }

        if (_caseInsensitive == TextMatching.ILike)
        {
            // Resolved here rather than on the first query, so a deployment missing the provider refuses to
            // start instead of failing the first search a user runs.
            ILike = InsensitiveLike.Resolve()
                ?? throw new InvalidOperationException(
                    "TextMatching.ILike translates through Npgsql's EF.Functions.ILike, and "
                    + "Npgsql.EntityFrameworkCore.PostgreSQL could not be loaded. Reference it, or keep "
                    + "TextMatching.Lower.");
        }

        IsFrozen = true;
    }
}

/// <summary>
/// Configures how the case-insensitive text operators match.
/// </summary>
/// <example>
/// <code>
/// // PostgreSQL with a pg_trgm index: IContains and the other pattern operators compile to ILIKE.
/// DwText.Configure(o => o.CaseInsensitive = TextMatching.ILike);
///
/// // Or from configuration:  "DynamicWhere": { "Text": { "CaseInsensitive": "ILike" } }
/// DwText.Configure(new DwTextOptions().Bind(configuration.GetSection("DynamicWhere:Text")));
/// </code>
/// </example>
public static class DwText
{
    private static readonly object ConfigureLock = new();
    private static volatile DwTextOptions _options = Frozen(new DwTextOptions());
    private static volatile bool _configured;

    /// <summary>The options in force. Frozen; with nothing configured they match by lowering both sides.</summary>
    public static DwTextOptions Options => _options;

    /// <summary>True once <see cref="Configure(DwTextOptions)"/> has been called.</summary>
    public static bool IsConfigured => _configured;

    /// <summary>Options set for one flow of execution, which <see cref="Current"/> reads before the process's.</summary>
    private static readonly AsyncLocal<DwTextOptions?> Scoped = new();

    /// <summary>The options a query composed here reads: a scope's inside <see cref="Use"/>, the process's otherwise.</summary>
    internal static DwTextOptions Current => Scoped.Value ?? _options;

    /// <summary>
    /// Composes the queries of this flow of execution with <paramref name="options"/> instead of the process's,
    /// until the scope is disposed.
    /// </summary>
    /// <remarks>
    /// For tests. The process-wide choice is made once, so a test that chose <see cref="TextMatching.ILike"/>
    /// through <see cref="Configure(DwTextOptions)"/> would choose it for every other test in the run, on every
    /// database.
    /// </remarks>
    internal static Scope Use(DwTextOptions options)
    {
        options.Freeze();

        Scope scope = new(Scoped.Value);

        Scoped.Value = options;

        return scope;
    }

    /// <summary>Restores the options in force before <see cref="Use"/>.</summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly DwTextOptions? _previous;

        internal Scope(DwTextOptions? previous) => _previous = previous;

        public void Dispose() => Scoped.Value = _previous;
    }

    /// <summary>
    /// Sets how the case-insensitive text operators match, for the process. Call it at startup.
    /// </summary>
    /// <param name="options">The options. Frozen by this call.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <see cref="DwTextOptions.CaseInsensitive"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="TextMatching.ILike"/> is chosen and Npgsql's EF Core provider cannot be loaded,
    /// or on a second call asking for something other than what the first one set.
    /// </exception>
    /// <remarks>
    /// Once, because every query reads it without a lock: a choice that could change mid-flight would let two
    /// requests sent the same filter match differently. A second call asking for exactly what is in force does
    /// nothing, so a process that starts several hosts, as an integration suite with one
    /// <c>WebApplicationFactory</c> per test class does, can run the same startup code in each.
    /// </remarks>
    public static void Configure(DwTextOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Freeze();

        // A lock rather than a compare-and-swap: a second caller compares against what the first one set,
        // so the first one's options have to be in place before anyone can see it configured.
        lock (ConfigureLock)
        {
            if (_configured)
            {
                if (options.CaseInsensitive == _options.CaseInsensitive)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Text matching is already configured as {_options.CaseInsensitive}. DwText.Configure is "
                    + "called once, at startup; a second call may only repeat it.");
            }

            _options = options;
            _configured = true;
        }
    }

    /// <summary>
    /// Sets how the case-insensitive text operators match, from a callback. Call it at startup.
    /// </summary>
    /// <param name="configure">Fills in a fresh <see cref="DwTextOptions"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options are refused, as <see cref="Configure(DwTextOptions)"/> refuses them.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown as <see cref="Configure(DwTextOptions)"/> throws it.
    /// </exception>
    public static void Configure(Action<DwTextOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        DwTextOptions options = new();

        configure(options);

        Configure(options);
    }

    /// <summary>
    /// Reads text options from a configuration section, refusing any key it does not recognise.
    /// </summary>
    /// <param name="options">The options to fill in. Must not be frozen.</param>
    /// <param name="section">The section, e.g. <c>DynamicWhere:Text</c>. An absent one changes nothing.</param>
    /// <returns>The same instance, for chaining into <see cref="Configure(DwTextOptions)"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the options are frozen; when the section names a key nothing answers to, or a value that is
    /// not a <see cref="TextMatching"/> name, so a misspelling refuses to start rather than leaving the
    /// deployment on the default; or when a single value stands where the section belongs.
    /// </exception>
    public static DwTextOptions Bind(this DwTextOptions options, IConfiguration section)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        if (options.IsFrozen)
        {
            throw new InvalidOperationException("Text options cannot be changed once configured.");
        }

        // "DynamicWhere:Text": "ILike" binds nothing and raises nothing, which would leave the deployment on
        // the default exactly as a misspelt key would.
        if (section is IConfigurationSection { Value: not null } single)
        {
            throw new InvalidOperationException(
                $"'{single.Path}' is a section, not a single value. Write \"{single.Key}\": "
                + "{ \"CaseInsensitive\": \"ILike\" }, or "
                + $"{single.Path}:{nameof(DwTextOptions.CaseInsensitive)} as a key.");
        }

        section.Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);

        return options;
    }

    private static DwTextOptions Frozen(DwTextOptions options)
    {
        options.Freeze();

        return options;
    }
}
