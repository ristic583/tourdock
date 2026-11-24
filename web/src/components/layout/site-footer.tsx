import Link from "next/link";

export function SiteFooter() {
  return (
    <footer className="border-t border-line">
      <div className="mx-auto flex max-w-page flex-wrap justify-between gap-6 px-4 py-7 text-sm text-muted-foreground sm:px-6">
        <span>© 2026 TourDock · portfolio demo, all content fictional</span>
        <span className="flex gap-5">
          <Link href="/agency" className="text-primary hover:text-primary-hover">
            List your tours
          </Link>
          <Link href="/ai" className="text-primary hover:text-primary-hover">
            Use with AI
          </Link>
          <Link href="/help" className="text-primary hover:text-primary-hover">
            Help
          </Link>
          <Link href="/privacy" className="text-primary hover:text-primary-hover">
            Privacy
          </Link>
        </span>
      </div>
    </footer>
  );
}
