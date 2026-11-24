"use client";

import { AlertTriangle, CircleHelp } from "lucide-react";
import { AlertDialog as AlertDialogPrimitive } from "radix-ui";
import { useRef, useState, type ReactElement, type ReactNode } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/problem";
import { cn } from "@/lib/utils";

export type ConfirmTone = "danger" | "default";

/** Throw from onConfirm to show this exact message in the dialog (for results that aren't an ApiError, like server actions). */
export class ConfirmError extends Error {}

type Props = {
  /** The button that opens the dialog; rendered as is. */
  trigger: ReactElement;
  title: ReactNode;
  /** The consequence, in one or two sentences. */
  description: ReactNode;
  confirmLabel: string;
  /** Shown on the confirm button while the action runs, for example "Deleting…". */
  pendingLabel?: string;
  cancelLabel?: string;
  tone?: ConfirmTone;
  /** Extra fields between the text and the buttons, for example a reason. */
  children?: ReactNode;
  /** Keeps the confirm button disabled, for example until a reason is long enough. */
  confirmDisabled?: boolean;
  /** Runs on confirm. The dialog stays open with a spinner until it settles; a throw shows the error inside. */
  onConfirm: () => Promise<unknown>;
  /** Called after a successful confirm, once the dialog has closed. */
  onDone?: () => void;
  /** Called whenever the dialog closes without confirming, to reset extra fields. */
  onCancel?: () => void;
  errorFallback?: string;
};

/**
 * The one confirmation dialog for actions that are hard to undo or affect other people (cancel, delete, suspend, hide).
 * Built on Radix AlertDialog: focus is trapped and returned to the trigger, Escape cancels, clicking outside does not.
 * Danger dialogs focus Cancel first so Enter never deletes by accident; the others focus the confirm button.
 */
export function ConfirmDialog({
  trigger,
  title,
  description,
  confirmLabel,
  pendingLabel,
  cancelLabel = "Cancel",
  tone = "default",
  children,
  confirmDisabled = false,
  onConfirm,
  onDone,
  onCancel,
  errorFallback = "That didn't work. Please try again.",
}: Props) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const confirmRef = useRef<HTMLButtonElement>(null);
  const danger = tone === "danger";

  function change(next: boolean) {
    if (busy) return;
    setOpen(next);
    if (!next) {
      setError(undefined);
      onCancel?.();
    }
  }

  async function confirm() {
    setBusy(true);
    setError(undefined);
    try {
      await onConfirm();
      setOpen(false);
      onDone?.();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : caught instanceof ConfirmError ? caught.message : errorFallback);
    } finally {
      setBusy(false);
    }
  }

  const Icon = danger ? AlertTriangle : CircleHelp;

  return (
    <AlertDialogPrimitive.Root open={open} onOpenChange={change}>
      <AlertDialogPrimitive.Trigger asChild>{trigger}</AlertDialogPrimitive.Trigger>
      <AlertDialogPrimitive.Portal>
        <AlertDialogPrimitive.Overlay className="fixed inset-0 z-50 bg-[rgb(15_26_32/0.45)] data-[state=open]:animate-in data-[state=open]:fade-in-0" />
        <AlertDialogPrimitive.Content
          data-tone={tone}
          onOpenAutoFocus={(event) => {
            if (danger) return; // Radix focuses Cancel first.
            event.preventDefault();
            confirmRef.current?.focus();
          }}
          onEscapeKeyDown={(event) => busy && event.preventDefault()}
          className="fixed top-1/2 left-1/2 z-50 flex max-h-[calc(100dvh-32px)] w-[calc(100vw-32px)] max-w-[460px] -translate-x-1/2 -translate-y-1/2 flex-col gap-5 overflow-y-auto rounded-hero border border-line bg-surface p-6 shadow-card data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
        >
          <div className="flex items-start gap-4">
            <span aria-hidden className={cn("grid size-11 shrink-0 place-items-center rounded-full", danger ? "bg-urgent-soft text-urgent-ink" : "bg-primary-soft text-primary")}>
              <Icon className="size-5" />
            </span>
            <div className="min-w-0 pt-1">
              <AlertDialogPrimitive.Title className="text-lg font-bold leading-snug">{title}</AlertDialogPrimitive.Title>
              <AlertDialogPrimitive.Description className="mt-1.5 text-sm text-muted-foreground">{description}</AlertDialogPrimitive.Description>
            </div>
          </div>
          {children}
          <FormAlert message={error} />
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <AlertDialogPrimitive.Cancel asChild>
              <Button variant="outline" disabled={busy}>{cancelLabel}</Button>
            </AlertDialogPrimitive.Cancel>
            {/* Not AlertDialog.Action: that closes at once, and this one must wait for the result. */}
            <Button ref={confirmRef} variant={danger ? "urgent" : "default"} pending={busy} disabled={confirmDisabled} onClick={confirm}>
              {busy ? (pendingLabel ?? confirmLabel) : confirmLabel}
            </Button>
          </div>
        </AlertDialogPrimitive.Content>
      </AlertDialogPrimitive.Portal>
    </AlertDialogPrimitive.Root>
  );
}

/** A labelled reason field for dialogs that need one (kept in the audit log or shown to the other side). */
export function ReasonField({ id, label, value, onChange, rows = 3 }: { id: string; label: string; value: string; onChange: (value: string) => void; rows?: number }) {
  return (
    <label htmlFor={id} className="flex flex-col gap-1.5 text-sm font-semibold">
      {label}
      <textarea id={id} rows={rows} maxLength={500} value={value} onChange={(event) => onChange(event.target.value)} className="rounded-control border border-line bg-surface px-3 py-2 font-normal outline-none focus-visible:border-primary" />
      <span className="text-xs font-normal text-muted-foreground">At least 3 characters.</span>
    </label>
  );
}
