import { LoadingPage, RowsSkeleton, Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Loading…" className="pb-20">
      <Skeleton className="h-3.5 w-16" />
      <Skeleton className="mt-2 h-9 w-48" />
      <Skeleton className="mt-5 mb-5 h-10 w-80" />
      <RowsSkeleton rows={6} />
    </LoadingPage>
  );
}
