import { createContext, useContext } from 'react';

export type ToastKind = 'ok' | 'error';
export interface ToastInput { kind?: ToastKind; text: string; link?: { href: string; label: string } }
export interface Toast extends ToastInput { id: number }

/** Shows a short message in the corner. Shared client state: one Toaster near the top of the tree owns the list. */
export const ToastContext = createContext<(toast: ToastInput) => void>(() => {});

export function useToast() {
  return useContext(ToastContext);
}
