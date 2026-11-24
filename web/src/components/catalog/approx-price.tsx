import type { Money } from "@/lib/api/contracts";
import { convert, ratesDateLabel, type DisplayRate } from "@/lib/currency";
import { formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

/** "≈ $50.24" next to a EUR price, or nothing when no display currency or rate is set. Works on server and client. */
export function ApproxPrice({ money, rate, className }: { money: Money | null | undefined; rate: DisplayRate | null | undefined; className?: string }) {
  const converted = convert(money, rate ?? null);
  if (!converted) return null;
  return (
    <span className={cn("text-[13px] text-muted-foreground", className)}>
      <span aria-hidden>≈ </span>
      <span className="sr-only">approximately </span>
      {formatMoney(converted)}
    </span>
  );
}

/** Says what the approximate prices are based on, and that the charge is in EUR. */
export function RatesNote({ rate, className }: { rate: DisplayRate | null | undefined; className?: string }) {
  if (!rate) return null;
  const date = ratesDateLabel(rate.date);
  return (
    <p className={cn("text-xs text-muted-foreground", className)}>
      {rate.currency} prices are approximate ({rate.stale ? `last available ECB rate, ${date}` : `ECB rate of ${date}`}). You pay in EUR.
    </p>
  );
}
