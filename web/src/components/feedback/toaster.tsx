"use client";

import { Check, X } from "lucide-react";
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";

type Toast = { id: number; message: string };

const ToastContext = createContext<(message: string) => void>(() => {});

const VISIBLE_MS = 5000;

/**
 * Short success notices after an action whose result isn't visible where the user is looking (for example an approved
 * agency leaving the "Pending" list). Bottom of the screen, above the mobile tab bar; read out politely.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const next = useRef(0);
  const show = useCallback((message: string) => {
    next.current += 1;
    const id = next.current;
    setToasts((current) => [...current.slice(-2), { id, message }]);
  }, []);
  const dismiss = useCallback((id: number) => setToasts((current) => current.filter((toast) => toast.id !== id)), []);

  return (
    <ToastContext value={show}>
      {children}
      <div role="status" aria-live="polite" className="pointer-events-none fixed inset-x-4 bottom-[92px] z-[60] flex flex-col items-center gap-2 md:right-6 md:bottom-6 md:left-auto md:items-end">
        {toasts.map((toast) => <ToastItem key={toast.id} toast={toast} onDismiss={dismiss} />)}
      </div>
    </ToastContext>
  );
}

function ToastItem({ toast, onDismiss }: { toast: Toast; onDismiss: (id: number) => void }) {
  useEffect(() => {
    const timer = window.setTimeout(() => onDismiss(toast.id), VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [toast.id, onDismiss]);
  return (
    <div className="pointer-events-auto flex w-full max-w-[420px] items-center gap-3 rounded-[14px] border border-line bg-surface py-3 pr-2 pl-4 text-sm shadow-card animate-in fade-in-0 slide-in-from-bottom-2">
      <span aria-hidden className="grid size-6 flex-none place-items-center rounded-full bg-primary text-primary-foreground"><Check className="size-3.5" /></span>
      <span className="flex-1">{toast.message}</span>
      <button type="button" onClick={() => onDismiss(toast.id)} className="grid size-9 cursor-pointer place-items-center rounded-full text-muted-foreground hover:bg-surface-2 hover:text-ink">
        <X className="size-4" aria-hidden />
        <span className="sr-only">Dismiss</span>
      </button>
    </div>
  );
}

export function useToast(): (message: string) => void {
  return useContext(ToastContext);
}
