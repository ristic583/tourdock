import { Suspense } from "react";

import { PageHeader } from "@/components/layout/page-header";
import { requireUser } from "@/lib/auth/dal";

/** Placeholder for protected sections that later segments fill in. Still checks the session. */
export function ComingSoon({ path, eyebrow, title, text }: { path: string; eyebrow: string; title: string; text: string }) {
  return (
    <div className="mx-auto max-w-page px-4 py-10 sm:px-6 sm:py-14">
      <PageHeader eyebrow={eyebrow} title={title} />
      <Suspense fallback={<div className="h-32 animate-pulse rounded-card bg-surface-2" />}>
        <Guarded path={path} text={text} />
      </Suspense>
    </div>
  );
}

async function Guarded({ path, text }: { path: string; text: string }) {
  await requireUser(path);
  return (
    <div className="rounded-card border border-dashed border-line bg-surface p-8 text-muted-foreground">{text}</div>
  );
}
