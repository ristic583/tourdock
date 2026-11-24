import { LoadingPage, Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Loading checkout…" className="max-w-[1040px]">
      <Skeleton className="h-9 w-48" />
      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="flex flex-col gap-4">
          <Skeleton className="h-[120px] rounded-card" />
          <Skeleton className="h-[180px] rounded-card" />
          <Skeleton className="h-14 rounded-[14px]" />
        </div>
        <Skeleton className="h-[320px] rounded-card" />
      </div>
    </LoadingPage>
  );
}
