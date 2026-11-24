"use client";

import { MailCheck } from "lucide-react";
import { useId, useState, useTransition } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { requestEmailChange } from "@/lib/auth/actions";

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Change the account's email. Needs the current password; the new address gets a link and the account keeps the old
 * address until it's opened. The answer is the same whether or not the new address already has an account.
 */
export function ChangeEmailForm({ currentEmail }: { currentEmail: string }) {
  const id = useId();
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<{ email?: string; password?: string; form?: string }>({});
  const [sentTo, setSentTo] = useState<string>();
  const [pending, start] = useTransition();

  if (sentTo) {
    return (
      <div role="status" className="flex items-start gap-3 rounded-control bg-primary-soft px-4 py-3 text-sm">
        <MailCheck className="mt-0.5 size-4 flex-none text-primary" aria-hidden />
        <span>
          We sent a link to <strong className="break-all">{sentTo}</strong>. Your email stays <strong className="break-all">{currentEmail}</strong> until you open it.
        </span>
      </div>
    );
  }

  if (!open) {
    return <Button variant="outline" size="sm" className="self-start" onClick={() => setOpen(true)}>Change email</Button>;
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const next = {
      email: !EMAIL.test(email.trim()) ? "Enter a valid email address." : email.trim().toLowerCase() === currentEmail.toLowerCase() ? "That's already your email." : undefined,
      password: password ? undefined : "Enter your current password.",
    };
    setErrors(next);
    if (next.email || next.password) return;
    start(async () => {
      const result = await requestEmailChange(email, password);
      if (result.ok) {
        setSentTo(email.trim());
        return;
      }
      const fields = result.error.fieldErrors ?? {};
      setErrors({ email: fields.newEmail, password: fields.password ?? (result.error.code === "Auth.WrongPassword" ? result.error.message : undefined), form: fields.newEmail || fields.password || result.error.code === "Auth.WrongPassword" ? undefined : result.error.message });
    });
  }

  return (
    <form noValidate onSubmit={submit} className="flex max-w-[420px] flex-col gap-4">
      <FormAlert message={errors.form} />
      <FormField id={`${id}-email`} label="New email" error={errors.email}>
        <Input id={`${id}-email`} type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} aria-invalid={Boolean(errors.email)} aria-describedby={describedBy(`${id}-email`, errors.email)} />
      </FormField>
      <FormField id={`${id}-password`} label="Current password" error={errors.password} hint="So nobody can change it from a device you left signed in.">
        <Input id={`${id}-password`} type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} aria-invalid={Boolean(errors.password)} aria-describedby={describedBy(`${id}-password`, errors.password, "hint")} />
      </FormField>
      <div className="flex gap-2">
        <Button type="submit" pending={pending}>{pending ? "Sending link…" : "Send confirmation link"}</Button>
        <Button type="button" variant="ghost" disabled={pending} onClick={() => { setOpen(false); setErrors({}); setPassword(""); }}>Cancel</Button>
      </div>
    </form>
  );
}
