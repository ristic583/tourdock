"use client";

import { CalendarDays, LayoutGrid, Map, MessageSquareText, Ship, Ticket } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";

import type { MyAgency } from "@/lib/api/contracts";
import { cn } from "@/lib/utils";

const SECTIONS = [
  { href: "", label: "Overview", icon: LayoutGrid },
  { href: "/calendar", label: "Calendar", icon: CalendarDays },
  { href: "/tours", label: "Tours", icon: Map },
  { href: "/vehicles", label: "Vehicles", icon: Ship },
  { href: "/bookings", label: "Bookings", icon: Ticket },
  { href: "/reviews", label: "Reviews", icon: MessageSquareText },
];

function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((word) => word[0]!.toUpperCase()).join("");
}

/** Back-office navigation: agency switcher and sections. A row of tabs on small screens, a sidebar on large ones. */
export function AgencyNav({ agency, agencies }: { agency: MyAgency; agencies: MyAgency[] }) {
  const pathname = usePathname();
  const router = useRouter();
  const base = `/agency/${agency.id}`;
  return (
    <aside aria-label="Agency navigation" className="flex min-w-0 flex-col gap-4 lg:sticky lg:top-24 lg:h-fit">
      <div className="flex items-center gap-3 rounded-card border border-line bg-surface p-3">
        <span className="grid size-10 flex-none place-items-center rounded-control bg-primary-soft font-display font-bold text-primary" aria-hidden>{initials(agency.name)}</span>
        {agencies.length > 1 ? (
          <label className="min-w-0 flex-1">
            <span className="sr-only">Switch agency</span>
            <select
              value={agency.id}
              onChange={(event) => router.push(`/agency/${event.target.value}`)}
              className="w-full truncate rounded-control border border-line bg-surface px-2 py-1.5 font-semibold"
            >
              {agencies.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
          </label>
        ) : (
          <div className="min-w-0">
            <p className="truncate font-semibold">{agency.name}</p>
            <p className="text-[13px] text-muted-foreground">{agency.myRole}</p>
          </div>
        )}
      </div>
      <nav aria-label="Sections" className="flex gap-1 overflow-x-auto lg:flex-col">
        {SECTIONS.map(({ href, label, icon: Icon }) => {
          const target = `${base}${href}`;
          const on = href ? pathname.startsWith(target) : pathname === base;
          return (
            <Link
              key={label}
              href={target}
              aria-current={on ? "page" : undefined}
              className={cn(
                "flex min-h-11 items-center gap-2.5 rounded-control px-3 font-medium whitespace-nowrap",
                on ? "bg-primary-soft text-primary" : "text-muted-foreground hover:bg-surface-2 hover:text-ink",
              )}
            >
              <Icon className="size-[18px]" aria-hidden /> {label}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
