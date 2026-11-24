import { cva, type VariantProps } from "class-variance-authority";
import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

const pill = cva(
  "inline-flex h-[26px] items-center gap-1.5 rounded-full px-2.5 text-xs font-bold whitespace-nowrap before:size-1.5 before:rounded-full before:bg-current before:content-['']",
  {
    variants: {
      tone: {
        pending: "bg-[var(--status-pending-bg)] text-[var(--status-pending-ink)]",
        confirmed: "bg-[var(--status-confirmed-bg)] text-[var(--status-confirmed-ink)]",
        completed: "bg-[var(--status-completed-bg)] text-[var(--status-completed-ink)]",
        cancelled: "bg-[var(--status-cancelled-bg)] text-[var(--status-cancelled-ink)]",
        expired: "bg-[var(--status-expired-bg)] text-[var(--status-expired-ink)]",
      },
    },
    defaultVariants: { tone: "expired" },
  },
);

export type StatusTone = NonNullable<VariantProps<typeof pill>["tone"]>;

export function StatusPill({ tone, children, className }: { tone: StatusTone; children: ReactNode; className?: string }) {
  return <span className={cn(pill({ tone }), className)}>{children}</span>;
}
