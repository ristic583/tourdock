"use client";

import { CalendarCheck, Search, UserRound } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/utils";

const TABS = [
  { href: "/", label: "Explore", icon: Search },
  { href: "/bookings", label: "Bookings", icon: CalendarCheck },
  { href: "/account", label: "Profile", icon: UserRound },
];

/** Bottom navigation on phones (the header nav is hidden below the md breakpoint). */
export function MobileTabBar() {
  const pathname = usePathname();
  return (
    <nav
      aria-label="App"
      className="fixed inset-x-0 bottom-0 z-40 flex justify-around border-t border-line bg-surface px-2 pt-2 pb-[max(14px,env(safe-area-inset-bottom))] md:hidden"
    >
      {TABS.map(({ href, label, icon: Icon }) => {
        const active = href === "/" ? pathname === "/" : pathname.startsWith(href);
        return (
          <Link
            key={href}
            href={href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex min-h-11 min-w-16 flex-col items-center justify-center gap-0.5 text-[11px] font-semibold",
              active ? "text-primary" : "text-muted-foreground",
            )}
          >
            <Icon className="size-[22px]" aria-hidden />
            {label}
          </Link>
        );
      })}
    </nav>
  );
}
