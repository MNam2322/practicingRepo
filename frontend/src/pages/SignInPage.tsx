import { useState, type FormEvent } from 'react';
import { ApiError, type SignedIn } from '../lib/api';
import { describeError, useSignIn, useSignUp } from '../lib/queries';
import { signInHref } from '../lib/router';
import { useToast } from '../lib/toast';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { Field } from '../components/Field';
import { ErrorNotice } from '../components/Notice';

/** The first message for a field, from a 400's field errors (keyed by JSON name, e.g. "password"). */
function fieldError(error: unknown, field: string): string | null {
  return error instanceof ApiError ? error.fieldErrors[field]?.[0] ?? null : null;
}

/** After signing in or up: say if bookings moved, then go back to where the person came from. */
function useWelcome(next: string) {
  const toast = useToast();
  return (signedIn: SignedIn) => {
    const moved = signedIn.movedBookings;
    toast({
      text: moved > 0
        ? `Signed in as ${signedIn.account.name}. Your ${moved === 1 ? 'booking was' : `${moved} bookings were`} moved to your account.`
        : `Signed in as ${signedIn.account.name}.`,
      link: moved > 0 ? { href: '#/mine', label: 'My bookings' } : undefined,
    });
    location.hash = next;
  };
}

export function SignInPage({ next }: { next: string }) {
  const signIn = useSignIn();
  const welcome = useWelcome(next);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  function submit(e: FormEvent) {
    e.preventDefault();
    signIn.mutate({ email: email.trim(), password }, { onSuccess: welcome });
  }

  // Wrong credentials are about the pair, not one field: shown above the form, never "no such email".
  const formError = signIn.isError && !(signIn.error instanceof ApiError && signIn.error.code === 'validation_failed') ? describeError(signIn.error) : null;

  return (
    <div className="auth">
      <title>Sign in</title>
      <h1>Sign in</h1>
      <p className="muted">Bookings you made on this device while signed out move to your account.</p>
      <Card as="section" className="auth-card">
        {formError && <ErrorNotice {...formError} />}
        <form onSubmit={submit} noValidate>
          <Field label="Email" error={fieldError(signIn.error, 'email')}>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required maxLength={254} />
          </Field>
          <Field label="Password" error={fieldError(signIn.error, 'password')}>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required maxLength={128} />
          </Field>
          <Button type="submit" disabled={signIn.isPending}>{signIn.isPending ? 'Signing in…' : 'Sign in'}</Button>
        </form>
      </Card>
      <p>New here? <a href={signInHref(next, 'signup')}>Create an account</a></p>
    </div>
  );
}

export function SignUpPage({ next }: { next: string }) {
  const signUp = useSignUp();
  const welcome = useWelcome(next);
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  function submit(e: FormEvent) {
    e.preventDefault();
    signUp.mutate({ email: email.trim(), password, name: name.trim() }, { onSuccess: welcome });
  }

  const error = signUp.error;
  const formError = signUp.isError && !(error instanceof ApiError && error.code === 'validation_failed') ? describeError(error) : null;

  return (
    <div className="auth">
      <title>Create an account</title>
      <h1>Create an account</h1>
      <p className="muted">See your bookings on any device. Bookings you made on this device move to your new account.</p>
      <Card as="section" className="auth-card">
        {formError && <ErrorNotice {...formError} />}
        <form onSubmit={submit} noValidate>
          <Field label="Name" error={fieldError(error, 'name')}>
            <input value={name} onChange={(e) => setName(e.target.value)} autoComplete="name" required maxLength={80} />
          </Field>
          <Field label="Email" error={fieldError(error, 'email')}>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required maxLength={254} />
          </Field>
          <Field label="Password" hint="At least 10 characters. A few words together works well." error={fieldError(error, 'password')}>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required minLength={10} maxLength={128} />
          </Field>
          <Button type="submit" disabled={signUp.isPending}>{signUp.isPending ? 'Creating…' : 'Create account'}</Button>
        </form>
      </Card>
      <p>Already have an account? <a href={signInHref(next)}>Sign in</a></p>
    </div>
  );
}
