import Link from "next/link";

import { cn } from "@/lib/utils";

const TABS = [
  { href: "/admin/agencies", label: "Agencies" },
  { href: "/admin/reviews", label: "Reviews" },
  { href: "/admin/users", label: "Users" },
  { href: "/admin/refunds", label: "Refunds" },
  { href: "/admin/audit-log", label: "Audit log" },
];

/** Switches between the admin areas. */
export function AdminTabs({ current }: { current: string }) {
  return (
    <nav aria-label="Admin areas" className="mt-3 flex gap-1 border-b border-line">
      {TABS.map((tab) => {
        const on = tab.href === current;
        return (
          <Link
            key={tab.href}
            href={tab.href}
            aria-current={on ? "page" : undefined}
            className={cn("-mb-px inline-flex min-h-11 items-center border-b-2 px-3.5 font-semibold", on ? "border-primary text-ink" : "border-transparent text-muted-foreground hover:text-ink")}
          >
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}
