"use client";

import { Check } from "lucide-react";
import { createContext, useContext, useState, type ReactNode } from "react";

const ToastContext = createContext<(message: string) => void>(() => {});

/** A dismissible confirmation above the bookings list (for example after a cancellation). */
export function BookingToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string>();
  return (
    <ToastContext value={setMessage}>
      <div role="status" aria-live="polite">
        {message && (
          <div className="mb-[18px] flex items-center gap-3 rounded-[14px] bg-primary-soft px-[18px] py-3.5">
            <Check className="size-4 flex-none text-primary" aria-hidden />
            <span className="flex-1">{message}</span>
            <button type="button" className="min-h-9 cursor-pointer rounded-control px-2.5 text-sm font-semibold hover:bg-surface" onClick={() => setMessage(undefined)}>Dismiss</button>
          </div>
        )}
      </div>
      {children}
    </ToastContext>
  );
}

export function useBookingToast(): (message: string) => void {
  return useContext(ToastContext);
}
