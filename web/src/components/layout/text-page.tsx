import type { ReactNode } from "react";

/** A simple reading page (help, privacy): title, intro and sections of short paragraphs. */
export function TextPage({ eyebrow, title, intro, children }: { eyebrow: string; title: string; intro: string; children: ReactNode }) {
  return (
    <div className="mx-auto max-w-[760px] px-4 pt-10 pb-20 sm:px-6">
      <p className="label-caps">{eyebrow}</p>
      <h1 className="mt-1 text-[34px] leading-tight font-bold">{title}</h1>
      <p className="mt-3 text-lg text-muted-foreground">{intro}</p>
      <div className="mt-8 flex flex-col gap-8 [&_h2]:text-xl [&_h2]:font-bold [&_p]:mt-2 [&_p]:leading-[1.7] [&_ul]:mt-2 [&_ul]:list-disc [&_ul]:pl-5 [&_li]:mt-1 [&_p_a]:underline [&_p_a]:underline-offset-2">{children}</div>
    </div>
  );
}
