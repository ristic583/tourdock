"use client";

import { ChevronDown } from "lucide-react";
import { useId, useState, type ReactNode } from "react";

import { cn } from "@/lib/utils";

/**
 * Collapses its content behind a summary row on phones (the design's "Edit search" card); from `lg` up the content is
 * always shown and the summary row is hidden.
 */
export function MobileDisclosure({ summary, children, className }: { summary: ReactNode; children: ReactNode; className?: string }) {
  const id = useId();
  const [open, setOpen] = useState(false);
  return (
    <div className={className}>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen(!open)}
        className="flex w-full cursor-pointer items-center gap-3 rounded-card border border-line bg-surface px-4 py-3 text-left shadow-card lg:hidden"
      >
        <span className="min-w-0 flex-1">{summary}</span>
        <ChevronDown className={cn("size-5 flex-none text-muted-foreground transition-transform", open && "rotate-180")} aria-hidden />
      </button>
      <div id={id} className={cn(open ? "mt-3 block" : "hidden", "lg:mt-0 lg:block")}>
        {children}
      </div>
    </div>
  );
}
