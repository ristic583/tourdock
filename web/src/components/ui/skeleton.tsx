import { cn } from "@/lib/utils";

/** Grey placeholder block with a soft pulse (still when reduced motion is on). */
export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={cn("animate-pulse rounded-control bg-surface-2 motion-reduce:animate-none", className)} />;
}

/** Wraps a route's skeleton: one polite "Loading…" for screen readers, the shapes are hidden from them. */
export function LoadingPage({ label, children, className }: { label: string; children: React.ReactNode; className?: string }) {
  return (
    <div role="status" aria-live="polite" className={cn("mx-auto max-w-page px-4 pt-8 pb-16 sm:px-6", className)}>
      <span className="sr-only">{label}</span>
      {children}
    </div>
  );
}

/** Same footprint as a tour card in the results grid. */
export function TourCardSkeleton() {
  return (
    <div aria-hidden className="flex flex-col overflow-hidden rounded-card border border-line bg-surface">
      <Skeleton className="h-[190px] rounded-none" />
      <div className="flex flex-col gap-2.5 px-[18px] pt-4 pb-[18px]">
        <Skeleton className="h-3.5 w-1/2" />
        <Skeleton className="h-5 w-4/5" />
        <Skeleton className="h-3.5 w-2/3" />
        <Skeleton className="mt-3 h-5 w-1/3" />
      </div>
    </div>
  );
}

/** A list of rows, for tables like bookings, agencies or the audit log. */
export function RowsSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div aria-hidden className="divide-y divide-line rounded-card border border-line bg-surface">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="flex items-center gap-4 px-4 py-4">
          <Skeleton className="size-11 shrink-0 rounded-[12px]" />
          <div className="flex flex-1 flex-col gap-2">
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="h-3.5 w-1/2" />
          </div>
          <Skeleton className="hidden h-9 w-24 sm:block" />
        </div>
      ))}
    </div>
  );
}
