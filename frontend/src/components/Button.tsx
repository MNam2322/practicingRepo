import type { ButtonHTMLAttributes } from 'react';

type Variant = 'primary' | 'ghost' | 'danger';

/** A real <button>. `type` defaults to "button" so it never submits a form by accident. */
export function Button({ variant = 'primary', type = 'button', className, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant }) {
  const classes = ['btn', variant === 'primary' ? '' : variant, className ?? ''].filter(Boolean).join(' ');
  return <button type={type} className={classes} {...rest} />;
}
