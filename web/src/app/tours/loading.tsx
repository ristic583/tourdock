import { LoadingPage, Skeleton, TourCardSkeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Finding tours…" className="sm:pt-10">
      <Skeleton className="h-[72px] w-full rounded-2xl" />
      <div className="mt-4 grid grid-cols-[minmax(0,1fr)] gap-4 lg:mt-8 lg:flex lg:items-start lg:gap-8">
        <Skeleton className="hidden h-[420px] flex-[0_0_300px] rounded-card lg:block" />
        <div className="min-w-0 flex-1">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="mt-2 mb-5 h-4 w-72" />
          <div className="grid grid-cols-[repeat(auto-fill,minmax(270px,1fr))] gap-[22px]">
            {Array.from({ length: 6 }, (_, index) => <TourCardSkeleton key={index} />)}
          </div>
        </div>
      </div>
    </LoadingPage>
  );
}
