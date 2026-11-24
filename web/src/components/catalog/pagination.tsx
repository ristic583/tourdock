import { ChevronLeft, ChevronRight } from "lucide-react";
import Link from "next/link";

import { pageList, searchHref, type CatalogSearch } from "@/lib/catalog-search";
import { cn } from "@/lib/utils";

const box = "inline-grid h-11 min-w-11 place-items-center rounded-control border px-2 font-semibold";

export function Pagination({ search, totalPages }: { search: CatalogSearch; totalPages: number }) {
  if (totalPages <= 1) return null;
  const page = Math.min(search.page, totalPages);
  return (
    <nav aria-label="Pagination" className="mt-9 flex flex-wrap items-center justify-center gap-2">
      {page > 1 ? (
        <Link href={searchHref(search, { page: page - 1 })} className={cn(box, "border-line bg-surface text-ink hover:border-muted-foreground")} aria-label="Previous page"><ChevronLeft className="size-4" /></Link>
      ) : null}
      {pageList(page, totalPages).map((item, index) =>
        item === "gap" ? (
          <span key={`gap-${index}`} className="px-1 text-muted-foreground" aria-hidden>…</span>
        ) : (
          <Link
            key={item}
            href={searchHref(search, { page: item })}
            aria-current={item === page ? "page" : undefined}
            aria-label={`Page ${item}`}
            className={cn(box, item === page ? "border-ink bg-ink text-bg" : "border-line bg-surface text-ink hover:border-muted-foreground")}
          >
            {item}
          </Link>
        ),
      )}
      {page < totalPages ? (
        <Link href={searchHref(search, { page: page + 1 })} className={cn(box, "border-line bg-surface text-ink hover:border-muted-foreground")} aria-label="Next page"><ChevronRight className="size-4" /></Link>
      ) : null}
    </nav>
  );
}
