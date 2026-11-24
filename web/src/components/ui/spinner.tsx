import { Loader2 } from "lucide-react";

import { cn } from "@/lib/utils";

/** Small rotating indicator for pending work. Decorative: the surrounding text says what is happening. */
export function Spinner({ className }: { className?: string }) {
  return <Loader2 aria-hidden className={cn("size-4 animate-spin motion-reduce:animate-[spin_1.5s_linear_infinite]", className)} />;
}
