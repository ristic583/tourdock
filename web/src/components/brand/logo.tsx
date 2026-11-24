import { cn } from "@/lib/utils";

/** The TourDock mark: a dock "T" (pier over a post) above a wave line, in a teal tile. */
export function LogoMark({ size = 32, className }: { size?: number; className?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" className={cn("shrink-0", className)}>
      <rect width="32" height="32" rx="9" className="fill-primary" />
      <path
        d="M9 10h14M16 10v9"
        className="stroke-primary-foreground"
        strokeWidth="2.6"
        strokeLinecap="round"
        fill="none"
      />
      <path
        d="M7 23.5c2.2-1.6 4.4-1.6 6.6 0s4.4 1.6 6.6 0 4.4-1.6 6.6 0"
        className="stroke-primary-foreground"
        strokeWidth="2.2"
        strokeLinecap="round"
        fill="none"
      />
    </svg>
  );
}

export function Logo({ className }: { className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <LogoMark />
      <span className="font-display text-[21px] font-bold tracking-[-0.02em]">TourDock</span>
    </span>
  );
}
