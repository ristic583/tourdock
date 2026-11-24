"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/utils";

const LINKS = [
  { href: "/", label: "Explore" },
  { href: "/bookings", label: "My bookings" },
  { href: "/agency", label: "For agencies" },
];

export function MainNav() {
  const pathname = usePathname();
  return (
    <nav aria-label="Main" className="hidden items-center gap-[22px] text-[15px] font-medium md:flex">
      {LINKS.map((link) => {
        const active = link.href === "/" ? pathname === "/" : pathname.startsWith(link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            aria-current={active ? "page" : undefined}
            className={cn("transition-colors hover:text-ink", active ? "text-ink" : "text-muted-foreground")}
          >
            {link.label}
          </Link>
        );
      })}
    </nav>
  );
}
