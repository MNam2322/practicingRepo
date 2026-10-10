import type { Resource, Venue } from '../../lib/api';
import { ADMIN_SECTIONS, type AdminSection } from '../../lib/router';
import { AuditAdmin } from './AuditAdmin';
import { ClosuresAdmin } from './ClosuresAdmin';
import { ResourcesAdmin } from './ResourcesAdmin';
import { RulesAdmin } from './RulesAdmin';

const LABEL: Record<AdminSection, string> = { resources: 'Courts and rooms', closures: 'Closures', rules: 'Booking rules', audit: 'Audit log' };

/** Venue set-up for admins. Every form shows the API's own field errors next to the fields; the API is the authority. */
export function AdminPage({ venue, resources, section }: { venue: Venue; resources: Resource[]; section: AdminSection }) {
  return (
    <>
      <title>{`Admin · ${LABEL[section]}`}</title>
      <h1>Admin</h1>
      <nav className="toggle admin-tabs" aria-label="Admin sections">
        {ADMIN_SECTIONS.map((s) => (
          <a key={s} href={`#/admin/${s}`} aria-current={s === section ? 'page' : undefined}>{LABEL[s]}</a>
        ))}
      </nav>
      {section === 'resources' && <ResourcesAdmin resources={resources} />}
      {section === 'closures' && <ClosuresAdmin venue={venue} resources={resources} />}
      {section === 'rules' && <RulesAdmin venue={venue} />}
      {section === 'audit' && <AuditAdmin />}
    </>
  );
}
