"use client";

import { Button } from "@/components/ui/button";

export default function ErrorPage({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <div className="mx-auto flex max-w-page flex-col items-start px-4 py-20 sm:px-6">
      <p className="label-caps">Something went wrong</p>
      <h1 className="mt-2 text-[34px] font-bold">We hit a snag.</h1>
      <p className="mt-2 text-muted-foreground">Please try again. If it keeps happening, come back in a few minutes.</p>
      <Button className="mt-6" onClick={reset}>
        Try again
      </Button>
    </div>
  );
}
