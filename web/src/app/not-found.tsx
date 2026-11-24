import Link from "next/link";

import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <div className="mx-auto flex max-w-page flex-col items-start px-4 py-20 sm:px-6">
      <p className="label-caps">404</p>
      <h1 className="mt-2 text-[34px] font-bold">This page drifted off.</h1>
      <p className="mt-2 text-muted-foreground">We couldn&apos;t find what you were looking for.</p>
      <Button asChild className="mt-6">
        <Link href="/">Back to tours</Link>
      </Button>
    </div>
  );
}
