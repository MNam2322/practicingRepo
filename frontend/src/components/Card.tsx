import type { ReactNode } from 'react';

/** A surface with a border and radius. With `href` the whole card is one link. */
export function Card({ href, as: Tag = 'div', className, children, ...aria }: {
  href?: string;
  as?: 'div' | 'section' | 'aside' | 'li';
  className?: string;
  children: ReactNode;
  'aria-live'?: 'polite' | 'off';
  'aria-label'?: string;
}) {
  const classes = `card${className ? ` ${className}` : ''}`;
  if (href) return <a className={classes} href={href} {...aria}>{children}</a>;
  return <Tag className={classes} {...aria}>{children}</Tag>;
}
