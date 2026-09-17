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
    internal static void Record(DwPolicyContext context, DwPolicyOptions options, Type entityType, PolicyException refusal)
    {
        if (!options.AuditRefusals || refusal.Audited)
        {
            return;
        }

        refusal.Audited = true;

        string path = refusal.AuditPath ?? refusal.FieldPath;

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
}
