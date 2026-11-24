import type { Metadata } from "next";

import { AdminTabs } from "@/components/admin/admin-tabs";
import { ConfirmUserEmailForm } from "@/components/admin/confirm-user-email-form";
import { requireAdmin } from "@/lib/auth/dal";

export const metadata: Metadata = { title: "Users · Admin", robots: { index: false } };
export const instant = false;

export default async function AdminUsersPage() {
  await requireAdmin("/admin/users");
  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-20 sm:px-6">
      <p className="label-caps">Admin</p>
      <h1 className="mt-1 text-[32px] font-bold">Users</h1>
      <AdminTabs current="/admin/users" />
      <section aria-labelledby="confirm-heading" className="mt-6 flex max-w-[560px] flex-col gap-3 rounded-card border border-line bg-surface p-6">
        <h2 id="confirm-heading" className="text-xl font-bold">Confirm an email address</h2>
        <p className="text-sm text-muted-foreground">
          For support, or for demo accounts on addresses that can&apos;t receive mail. The account can book and list an
          agency right away. Recorded in the audit log.
        </p>
        <ConfirmUserEmailForm />
      </section>
    </div>
  );
}
