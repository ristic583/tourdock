import type { ReactNode } from "react";

export function PageHeader({ eyebrow, title, children }: { eyebrow?: string; title: string; children?: ReactNode }) {
  return (
    <div className="mb-8">
      {eyebrow ? <p className="mb-2 text-sm font-semibold tracking-[0.04em] text-primary uppercase">{eyebrow}</p> : null}
      <h1 className="text-[34px] leading-tight font-bold sm:text-[42px]">{title}</h1>
      {children ? <div className="mt-2 max-w-2xl text-muted-foreground">{children}</div> : null}
    </div>
  );
}
