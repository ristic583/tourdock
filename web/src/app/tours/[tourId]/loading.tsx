import { LoadingPage, Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Loading the tour…" className="pt-6 pb-20">
      <Skeleton className="h-4 w-40" />
      <Skeleton className="mt-4 h-10 w-3/4 max-w-[560px]" />
      <Skeleton className="mt-3 h-5 w-64" />
      <Skeleton className="mt-6 h-[260px] w-full rounded-hero sm:h-[420px]" />
      <div className="mt-6 grid grid-cols-[minmax(0,1fr)] gap-8 lg:mt-9 lg:flex lg:items-start lg:gap-10">
        <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          <Skeleton className="h-[84px] w-full rounded-card" />
          <div className="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-[18px]">
            {Array.from({ length: 4 }, (_, index) => <Skeleton key={index} className="h-12" />)}
          </div>
          <Skeleton className="mt-4 h-6 w-48" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-11/12" />
          <Skeleton className="h-4 w-4/5" />
        </div>
        <Skeleton className="h-[460px] flex-[1_1_340px] rounded-card lg:max-w-[400px]" />
      </div>
    </LoadingPage>
  );
}
