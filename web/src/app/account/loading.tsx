import { LoadingPage, Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <LoadingPage label="Loading your account…" className="max-w-[720px]">
      <Skeleton className="h-9 w-48" />
      <Skeleton className="mt-6 h-[220px] rounded-card" />
    </LoadingPage>
  );
}
