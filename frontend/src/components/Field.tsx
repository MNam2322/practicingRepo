import { useId, type ReactElement, cloneElement } from 'react';

type Control = ReactElement<{ id?: string; 'aria-invalid'?: boolean; 'aria-describedby'?: string }>;

/**
 * A label above one input or select. The control gets an id, and an error message is linked to it with
 * aria-describedby so screen readers read it with the field, and announced as it appears.
 */
export function Field({ label, error, hint, children }: { label: string; error?: string | null; hint?: string; children: Control }) {
  const id = useId();
  const messageId = `${id}-message`;
  const control = cloneElement(children, {
    id,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': error || hint ? messageId : undefined,
  });
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {control}
      {/* role="alert": a screen reader says the error when it appears, not only when the field is focused. */}
      {error ? <span id={messageId} className="field-error" role="alert">{error}</span>
        : hint ? <span id={messageId} className="muted small">{hint}</span> : null}
    </div>
  );
}
