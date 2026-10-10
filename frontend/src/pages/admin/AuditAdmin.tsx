import { describeError, useAuditLog } from '../../lib/queries';
import { ErrorNotice } from '../../components/Notice';
import { BookingsSkeleton } from '../../components/Skeleton';

/** Pretty JSON for the before/after details, or the raw text if it is not JSON. */
function pretty(json: string | null | undefined): string {
  if (!json) return '—';
  try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
}

/** Who changed what and when, newest first. Read-only: the database refuses changes to it. */
export function AuditAdmin() {
  const audit = useAuditLog();
  if (audit.isPending) return <BookingsSkeleton rows={4} />;
  if (audit.isError) return <ErrorNotice {...describeError(audit.error)} retry={() => void audit.refetch()} />;
  if (audit.data.length === 0) return <p className="muted">No changes recorded yet.</p>;
  return (
    <section aria-label="Audit log">
      <p className="muted small">The latest {audit.data.length} changes. Entries cannot be edited or deleted.</p>
      <div className="week-scroll" tabIndex={0} aria-label="Audit log entries">
        <table className="audit">
          <thead><tr><th scope="col">When</th><th scope="col">Who</th><th scope="col">What</th><th scope="col">Item</th><th scope="col">Details</th></tr></thead>
          <tbody>
            {audit.data.map((a) => (
              <tr key={a.id}>
                <td>{new Date(a.occurredAt).toLocaleString()}</td>
                <td>{a.actorEmail}</td>
                <td><code>{a.action}</code></td>
                <td>{a.entity} <span className="muted">{a.entityId}</span></td>
                <td>
                  <details>
                    <summary>Before / after</summary>
                    <div className="audit-diff">
                      <pre aria-label="Before">{pretty(a.before)}</pre>
                      <pre aria-label="After">{pretty(a.after)}</pre>
                    </div>
                    {a.requestId && <p className="small muted">Request <code>{a.requestId}</code></p>}
                  </details>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
