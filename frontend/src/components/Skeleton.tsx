import type { ReactNode } from 'react';

/**
 * Grey placeholders shown while data loads, laid out at the size of the real content so the page doesn't jump
 * when it arrives (library 9.7 "Loading, errors and empty results"). Hidden from screen readers, which hear
 * the "Loading…" label instead.
 */
export function Skeleton({ className }: { className?: string }) {
  return <span className={`skeleton${className ? ` ${className}` : ''}`} aria-hidden="true" />;
}

function Loading({ label, children }: { label: string; children: ReactNode }) {
  return <div role="status" aria-label={label} aria-busy="true">{children}</div>;
}

/** Same grid as the real slot buttons. */
export function SlotsSkeleton({ count = 12 }: { count?: number }) {
  return (
    <Loading label="Loading times…">
      <div className="slots">
        {Array.from({ length: count }, (_, i) => <Skeleton key={i} className="slot-skeleton" />)}
      </div>
    </Loading>
  );
}

/** Same rows as the real booking list. */
export function BookingsSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <Loading label="Loading bookings…">
      <ul className="bookings">
        {Array.from({ length: rows }, (_, i) => (
          <li key={i} className="booking">
            <div className="skeleton-lines">
              <Skeleton className="line wide" />
              <Skeleton className="line" />
            </div>
          </li>
        ))}
      </ul>
    </Loading>
  );
}

/** Same cards as the resources page. */
export function CardsSkeleton({ count = 3 }: { count?: number }) {
  return (
    <Loading label="Loading…">
      <Skeleton className="line title" />
      <Skeleton className="line wide" />
      <div className="cards">
        {Array.from({ length: count }, (_, i) => <Skeleton key={i} className="card-skeleton" />)}
      </div>
    </Loading>
  );
}
