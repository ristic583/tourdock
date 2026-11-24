"use client";

import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import { Popover as PopoverPrimitive } from "radix-ui";
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";

import {
  addDays,
  addMonths,
  formatDayLabel,
  formatMonthTitle,
  formatPickedDate,
  inRange,
  isIsoDate,
  monthGrid,
  startOfMonth,
  todayIso,
  type IsoDate,
} from "@/lib/calendar-dates";
import { cn } from "@/lib/utils";

const WEEKDAYS = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

type DatePickerProps = {
  id?: string;
  /** Field name for screen readers ("Valid from"); the button's text is the picked date. */
  label?: string;
  /** Submits the value as a hidden input, for plain HTML forms (search, filters). */
  name?: string;
  /** Controlled value ("YYYY-MM-DD" or ""). Leave undefined and use `defaultValue` for an uncontrolled picker. */
  value?: string;
  defaultValue?: string;
  onChange?: (value: string) => void;
  min?: string;
  max?: string;
  placeholder?: string;
  /** Offers "Clear" in the calendar; for optional dates. */
  clearable?: boolean;
  /** Days before today (the viewer's) can't be picked. */
  disablePast?: boolean;
  /** "field" looks like the other inputs; "bare" blends into a container such as the search bar. */
  variant?: "field" | "bare";
  className?: string;
  /** Shows the error border; describe the error with `aria-describedby`. */
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
};

/**
 * A date field in the site's own style: a button showing "Sat 14 Nov 2026" (no "dd. mm. yyyy." placeholder) that
 * opens a month calendar. Values stay plain ISO dates, so forms and the API are unchanged. Keyboard: arrows move by
 * day/week, Page Up/Down by month, Home/End to the week's ends, Enter picks, Escape closes.
 */
export function DatePicker({
  id,
  label,
  name,
  value,
  defaultValue = "",
  onChange,
  min: minProp,
  max,
  placeholder = "Pick a date",
  clearable = false,
  disablePast = false,
  variant = "field",
  className,
  ...aria
}: DatePickerProps) {
  // Today is read only when the calendar opens: reading the clock while rendering would break prerendering
  // (Next.js refuses Date.now() there) and could differ between server and browser.
  const [today, setToday] = useState<IsoDate>();
  const min = disablePast && today && (!minProp || minProp < today) ? today : minProp;
  const [inner, setInner] = useState(defaultValue);
  const current = value ?? inner;
  const selected = isIsoDate(current) ? current : undefined;
  const [open, setOpen] = useState(false);
  const [focused, setFocused] = useState<IsoDate>(selected ?? "2000-01-01");
  const grid = useRef<HTMLDivElement>(null);

  function pick(next: string) {
    if (value === undefined) setInner(next);
    onChange?.(next);
    setOpen(false);
  }

  function openChange(next: boolean) {
    if (next) {
      const now = todayIso();
      const lowest = disablePast && (!minProp || minProp < now) ? now : minProp;
      setToday(now);
      setFocused(initialFocus(selected, lowest, now));
    }
    setOpen(next);
  }

  // Move DOM focus to the focused day whenever it changes while open (roving tabindex).
  useEffect(() => {
    if (!open) return;
    grid.current?.querySelector<HTMLButtonElement>(`[data-day="${focused}"]`)?.focus();
  }, [focused, open]);

  function onGridKey(event: KeyboardEvent<HTMLDivElement>) {
    const weekday = (new Date(`${focused}T00:00:00Z`).getUTCDay() + 6) % 7;
    const moves: Record<string, () => IsoDate> = {
      ArrowLeft: () => addDays(focused, -1),
      ArrowRight: () => addDays(focused, 1),
      ArrowUp: () => addDays(focused, -7),
      ArrowDown: () => addDays(focused, 7),
      Home: () => addDays(focused, -weekday),
      End: () => addDays(focused, 6 - weekday),
      PageUp: () => sameDayIn(focused, -1),
      PageDown: () => sameDayIn(focused, 1),
    };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    setFocused(move());
  }

  const month = startOfMonth(focused);
  const canGoBack = !min || startOfMonth(min) < month;
  const canGoForward = !max || addMonths(month, 1) <= max;

  return (
    <PopoverPrimitive.Root open={open} onOpenChange={openChange}>
      {name && <input type="hidden" name={name} value={selected ?? ""} />}
      <PopoverPrimitive.Trigger asChild>
        <button
          id={id}
          type="button"
          aria-haspopup="dialog"
          aria-label={label ? `${label}: ${selected ? formatPickedDate(selected) : placeholder}` : undefined}
          data-invalid={aria["aria-invalid"] ? "true" : undefined}
          aria-describedby={aria["aria-describedby"]}
          className={cn(
            "flex w-full min-w-0 cursor-pointer items-center gap-2 text-left outline-none",
            variant === "field" &&
              "h-11 rounded-control border border-line bg-surface px-3 text-[15px] transition-colors focus-visible:border-primary focus-visible:outline-2 focus-visible:outline-primary data-[invalid=true]:border-urgent-ink",
            variant === "bare" && "p-0 text-base font-medium",
            className,
          )}
        >
          {variant === "field" && <CalendarDays className="size-4 flex-none text-muted-foreground" aria-hidden />}
          <span className={cn("truncate", selected ? "text-ink" : "font-normal text-muted-foreground")}>
            {selected ? formatPickedDate(selected) : placeholder}
          </span>
        </button>
      </PopoverPrimitive.Trigger>
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={8}
          collisionPadding={12}
          aria-label="Choose a date"
          onOpenAutoFocus={(event) => event.preventDefault()}
          className="z-50 w-[296px] rounded-card border border-line bg-surface p-3 text-ink shadow-card data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
        >
          <div className="mb-2 flex items-center justify-between">
            <MonthButton label="Previous month" disabled={!canGoBack} onClick={() => setFocused(sameDayIn(focused, -1))}><ChevronLeft className="size-4" /></MonthButton>
            <p className="text-sm font-semibold" aria-live="polite">{formatMonthTitle(month)}</p>
            <MonthButton label="Next month" disabled={!canGoForward} onClick={() => setFocused(sameDayIn(focused, 1))}><ChevronRight className="size-4" /></MonthButton>
          </div>
          <div className="grid grid-cols-7 text-center text-[11px] font-semibold text-muted-foreground" aria-hidden>
            {WEEKDAYS.map((day) => <span key={day} className="py-1">{day}</span>)}
          </div>
          <div ref={grid} role="grid" aria-label={formatMonthTitle(month)} onKeyDown={onGridKey} className="grid grid-cols-7 gap-0.5">
            {monthGrid(month).map((day) => {
              const outside = !day.startsWith(month.slice(0, 7));
              const enabled = inRange(day, min, max);
              const isSelected = day === selected;
              return (
                <button
                  key={day}
                  type="button"
                  role="gridcell"
                  data-day={day}
                  tabIndex={day === focused ? 0 : -1}
                  disabled={!enabled}
                  aria-selected={isSelected}
                  aria-current={day === today ? "date" : undefined}
                  aria-label={formatDayLabel(day)}
                  onClick={() => pick(day)}
                  className={cn(
                    "flex h-9 items-center justify-center rounded-[10px] text-sm tabular-nums outline-none transition-colors",
                    "hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-30 disabled:hover:bg-transparent",
                    outside && "text-muted-foreground",
                    day === today && !isSelected && "font-bold text-primary",
                    isSelected && "bg-primary font-semibold text-primary-foreground hover:bg-primary-hover",
                  )}
                >
                  {Number(day.slice(8))}
                </button>
              );
            })}
          </div>
          <div className="mt-2 flex items-center justify-between border-t border-line pt-2 text-sm">
            {clearable ? (
              <button type="button" className="rounded-sm px-2 py-1 font-semibold text-muted-foreground hover:text-ink" onClick={() => pick("")}>Clear</button>
            ) : <span />}
            {today && inRange(today, min, max) && (
              <button type="button" className="rounded-sm px-2 py-1 font-semibold text-primary hover:text-primary-hover" onClick={() => pick(today)}>Today</button>
            )}
          </div>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}

function MonthButton({ label, disabled, onClick, children }: { label: string; disabled: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button type="button" aria-label={label} disabled={disabled} onClick={onClick} className="flex size-8 items-center justify-center rounded-full text-ink hover:bg-surface-2 disabled:opacity-30">
      {children}
    </button>
  );
}

function initialFocus(selected: IsoDate | undefined, min: string | undefined, today: IsoDate): IsoDate {
  if (selected) return selected;
  return min && isIsoDate(min) && min > today ? min : today;
}

/** The same day of the month `months` away, clamped to that month's length (31 Jan → 28 Feb). */
function sameDayIn(value: IsoDate, months: number): IsoDate {
  const first = addMonths(value, months);
  const lastDay = addDays(addMonths(first, 1), -1);
  const day = Math.min(Number(value.slice(8)), Number(lastDay.slice(8)));
  return `${first.slice(0, 8)}${String(day).padStart(2, "0")}`;
}
