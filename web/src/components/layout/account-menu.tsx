"use client";

import { Building2, CalendarCheck, LogOut, ShieldCheck, UserRound } from "lucide-react";
import Link from "next/link";
import { useTransition } from "react";

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { logout } from "@/lib/auth/actions";

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const letters = parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[parts.length - 1][0];
  return letters.toUpperCase();
}

export function AccountMenu({ displayName, email, isAdmin = false }: { displayName: string; email: string; isAdmin?: boolean }) {
  const [pending, startTransition] = useTransition();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        className="inline-flex min-h-10 cursor-pointer items-center gap-2 rounded-full border border-line bg-surface py-1 pr-1.5 pl-3.5 text-sm hover:border-muted-foreground"
      >
        {/* The visible name is the button's name (WCAG 2.5.3); the initials are decoration. */}
        <span className="max-w-40 truncate max-sm:sr-only">{displayName}</span>
        <span className="sr-only">, account menu</span>
        <span aria-hidden className="grid size-[30px] place-items-center rounded-full bg-primary-soft text-[13px] font-bold text-primary">
          {initials(displayName)}
        </span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60 rounded-control">
        <DropdownMenuLabel className="font-normal">
          <span className="block truncate font-semibold">{displayName}</span>
          <span className="block truncate text-xs text-muted-foreground">{email}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/bookings">
            <CalendarCheck /> My bookings
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link href="/agency">
            <Building2 /> Agency back office
          </Link>
        </DropdownMenuItem>
        {isAdmin && (
          <DropdownMenuItem asChild>
            <Link href="/admin/agencies">
              <ShieldCheck /> Admin
            </Link>
          </DropdownMenuItem>
        )}
        <DropdownMenuItem asChild>
          <Link href="/account">
            <UserRound /> Account
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled={pending} onSelect={() => startTransition(() => logout())}>
          <LogOut /> {pending ? "Signing out…" : "Sign out"}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
