import type { ReactNode } from "react";

import { LogoMark } from "@/components/brand/logo";

export function AuthCard({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="mx-auto flex w-full max-w-page justify-center px-4 py-12 sm:px-6 sm:py-20">
      <div className="w-full max-w-[420px] rounded-hero border border-line bg-surface p-6 shadow-card sm:p-8">
        <LogoMark size={40} />
        <h1 className="mt-5 text-[28px] leading-tight font-bold">{title}</h1>
        <p className="mt-1.5 mb-6 text-muted-foreground">{subtitle}</p>
        {children}
      </div>
    </div>
  );
}
