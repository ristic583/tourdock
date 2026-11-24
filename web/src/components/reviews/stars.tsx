"use client";

import { Star } from "lucide-react";
import { useId } from "react";

import { ratingWords } from "@/lib/reviews";
import { cn } from "@/lib/utils";

/** Read-only stars. The number is announced to screen readers; the icons are decoration. */
export function Stars({ rating, size = "sm", className }: { rating: number; size?: "sm" | "md"; className?: string }) {
  const full = Math.round(rating);
  return (
    <span className={cn("inline-flex items-center gap-0.5", className)} role="img" aria-label={`${rating} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => (
        <Star key={n} aria-hidden className={cn(size === "md" ? "size-5" : "size-4", n <= full ? "fill-star text-star" : "text-line")} />
      ))}
    </span>
  );
}

/** Star picker as a radio group: works with arrow keys and screen readers like any set of radio buttons. */
export function StarInput({ value, onChange, disabled }: { value: number; onChange: (rating: number) => void; disabled?: boolean }) {
  const name = useId();
  return (
    <fieldset className="flex flex-col gap-1.5" disabled={disabled}>
      <legend className="text-sm font-semibold">Your rating</legend>
      <div className="flex items-center gap-1">
        {[1, 2, 3, 4, 5].map((n) => (
          <label key={n} className="cursor-pointer rounded-control p-0.5 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-primary">
            <input type="radio" name={name} value={n} checked={value === n} onChange={() => onChange(n)} className="sr-only" />
            <span className="sr-only">{n} {n === 1 ? "star" : "stars"} ({ratingWords(n)})</span>
            <Star aria-hidden className={cn("size-8 transition-colors", n <= value ? "fill-star text-star" : "text-line hover:text-star")} />
          </label>
        ))}
        <span className="ml-2 text-sm text-muted-foreground" aria-hidden>{value ? ratingWords(value) : "Pick 1 to 5 stars"}</span>
      </div>
    </fieldset>
  );
}
