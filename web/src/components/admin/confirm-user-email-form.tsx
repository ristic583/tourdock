"use client";

import { useId, useState } from "react";

import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog, ConfirmError } from "@/components/ui/confirm-dialog";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { adminConfirmEmail } from "@/lib/auth/actions";

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Marks one account's email as confirmed, after asking. */
export function ConfirmUserEmailForm() {
  const id = useId();
  const toast = useToast();
  const [email, setEmail] = useState("");
  const valid = EMAIL.test(email.trim());

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="min-w-[260px] flex-1">
        <FormField id={id} label="Account email" hint="The exact address the account signed up with.">
          <Input id={id} type="email" value={email} onChange={(event) => setEmail(event.target.value)} aria-describedby={describedBy(id, undefined, "hint")} />
        </FormField>
      </div>
      <ConfirmDialog
        trigger={<Button disabled={!valid}>Confirm email</Button>}
        title="Confirm this email address?"
        description={`${email.trim()} will count as confirmed without the owner opening our link. Only do this when you know the address belongs to them.`}
        confirmLabel="Confirm email"
        pendingLabel="Confirming…"
        onConfirm={async () => {
          const result = await adminConfirmEmail(email);
          if (!result.ok) throw new ConfirmError(result.error.code === "Auth.UserNotFound" ? "There's no account with this email." : result.error.message);
        }}
        onDone={() => {
          toast(`${email.trim()} is confirmed.`);
          setEmail("");
        }}
      />
    </div>
  );
}
