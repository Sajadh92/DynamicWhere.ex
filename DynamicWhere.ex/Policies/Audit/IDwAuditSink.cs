namespace DynamicWhere.ex.Policies.Audit;

/// <summary>
/// Where audit events are written.
/// </summary>
/// <remarks>
/// Never called on the query path. Events accumulate on the caller's context while the query runs
/// — which is synchronous — and are drained once, afterwards, through
/// <c>DwPolicy.DrainAuditAsync</c>. The two alternatives were both worse for a security record:
/// a fire-and-forget call loses events on process exit and turns a failing sink into an unobserved
/// exception, and blocking the synchronous path on a sink puts I/O on the query path and deadlocks
/// in any host with a synchronization context.
/// <para>
/// An implementation is free to be slow. It is not free to swallow a failure: a drain that throws
/// leaves the unwritten events on the buffer, which is what lets a host retry. One that reports
/// success without writing loses them silently.
/// </para>
/// </remarks>
public interface IDwAuditSink
{
    /// <summary>
    /// Records one access.
    /// </summary>
    /// <param name="auditEvent">What happened.</param>
    /// <param name="ct">Cancels the write.</param>
    ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default);
}
