using System.Globalization;
using System.Text;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Audit;

/// <summary>
/// Records a refused guarded query on the caller's audit buffer, when the posture audits refusals.
/// </summary>
/// <remarks>
/// Called from an exception filter at every guarded entry point, so a refusal raised anywhere beneath
/// one — the gate, resolution, a store, a transform — is written without each of those learning about
/// the audit. The refusal is never changed or swallowed. A buffer with no room left records nothing:
/// the query is being refused either way, and refusing it again for the audit would only replace the
/// reason the caller needed.
/// </remarks>
internal static class RefusalAudit
{
    /// <summary>The most characters of a field path an event records.</summary>
    private const int MaxRecordedPath = 256;

    internal static void Record(DwPolicyContext context, DwPolicyOptions options, Type entityType, PolicyException refusal)
    {
        if (!options.AuditRefusals || refusal.Audited)
        {
            return;
        }

        refusal.Audited = true;

        string path = Recordable(refusal.AuditPath ?? refusal.FieldPath);

        DwAuditEvent recorded = new(
            DateTimeOffset.UtcNow,
            entityType.FullName ?? entityType.Name,
            string.IsNullOrWhiteSpace(path) ? "*" : path,
            refusal.Feature,
            PolicyEffect.Deny,
            context.Subjects,
            context.Purpose,
            refusal.Tier,
            dryRun: false,
            refusal.ErrorCode);

        context.TryRecordAudit(recorded, options.Caps.MaxAuditEvents);
    }

    /// <summary>
    /// A path fit to store: cut to <see cref="MaxRecordedPath"/> characters, with every control
    /// character escaped.
    /// </summary>
    /// <remarks>
    /// Under the strict tier a name that matches nothing is recorded as the caller sent it, so this is
    /// text a caller wrote. A line break in it would forge a second entry in any log written one event
    /// per line, and a name a megabyte long would be kept whole for as long as the audit is.
    /// </remarks>
    private static string Recordable(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        string cut = path!;

        if (cut.Length > MaxRecordedPath)
        {
            // Never between the two halves of a surrogate pair, which would store half a character.
            int length = char.IsHighSurrogate(cut[MaxRecordedPath - 1]) ? MaxRecordedPath - 1 : MaxRecordedPath;

            cut = cut.Substring(0, length) + "…";
        }

        if (!cut.Any(char.IsControl))
        {
            return cut;
        }

        StringBuilder escaped = new(cut.Length + 16);

        foreach (char character in cut)
        {
            if (char.IsControl(character))
            {
                escaped.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }
}
