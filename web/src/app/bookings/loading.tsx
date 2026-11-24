import { LoadingPage, RowsSkeleton, Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Loading your bookings…">
      <Skeleton className="h-9 w-56" />
      <Skeleton className="mt-6 mb-5 h-10 w-72" />
      <RowsSkeleton rows={4} />
    </LoadingPage>
  );
}
