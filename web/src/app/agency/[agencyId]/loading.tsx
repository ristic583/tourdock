import { RowsSkeleton, Skeleton } from "@/components/ui/skeleton";

/** Inside the agency layout (nav stays put); only the page area shows placeholders. */
export default function Loading() {
  return (
    <div role="status" aria-live="polite" className="flex flex-col gap-5">
      <span className="sr-only">Loading…</span>
      <Skeleton className="h-9 w-64" />
      <div className="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-4">
        {Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-[96px] rounded-card" />)}
      </div>
      <RowsSkeleton rows={4} />
    </div>
  );
}
