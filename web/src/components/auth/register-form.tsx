"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { MailCheck } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { useForm } from "react-hook-form";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { register as registerAccount } from "@/lib/auth/actions";
import { registerSchema, type RegisterInput } from "@/lib/auth/schemas";
import { applyActionError } from "@/lib/forms";

const PASSWORD_HINT = "At least 10 characters.";

export function RegisterForm({ next }: { next?: string }) {
  const [sentTo, setSentTo] = useState<string | null>(null);
  const form = useForm<RegisterInput>({
    resolver: zodResolver(registerSchema),
    defaultValues: { displayName: "", email: "", password: "" },
  });
  const { errors, isSubmitting } = form.formState;
  const loginHref = next ? `/login?next=${encodeURIComponent(next)}` : "/login";

  const onSubmit = form.handleSubmit(async (values) => {
    const result = await registerAccount(values);
    if (!result.ok) applyActionError(result.error, form.setError, ["displayName", "email", "password"]);
    else setSentTo(values.email);
  });

  if (sentTo) return <CheckInbox email={sentTo} loginHref={loginHref} />;

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
      <FormAlert message={errors.root?.server?.message} />
      <FormField id="displayName" label="Your name" error={errors.displayName?.message}>
        <Input
          id="displayName"
          autoComplete="name"
          aria-invalid={Boolean(errors.displayName)}
          aria-describedby={describedBy("displayName", errors.displayName?.message)}
          {...form.register("displayName")}
        />
      </FormField>
      <FormField id="email" label="Email" error={errors.email?.message}>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          aria-invalid={Boolean(errors.email)}
          aria-describedby={describedBy("email", errors.email?.message)}
          {...form.register("email")}
        />
      </FormField>
      <FormField id="password" label="Password" error={errors.password?.message} hint={PASSWORD_HINT}>
        <Input
          id="password"
          type="password"
          autoComplete="new-password"
          aria-invalid={Boolean(errors.password)}
          aria-describedby={describedBy("password", errors.password?.message, PASSWORD_HINT)}
          {...form.register("password")}
        />
      </FormField>
      <Button type="submit" pending={isSubmitting} className="mt-2 w-full">
        {isSubmitting ? "Creating your account…" : "Create account"}
      </Button>
      <p className="text-center text-sm text-muted-foreground">
        Already have an account?{" "}
        <Link href={loginHref} className="font-semibold text-primary hover:text-primary-hover">
          Sign in
        </Link>
      </p>
    </form>
  );
}

/** After sign-up: the same message whether or not the address already had an account; the email says which. */
function CheckInbox({ email, loginHref }: { email: string; loginHref: string }) {
  return (
    <div className="flex flex-col gap-5" role="status">
      <h2 className="flex items-center gap-2.5 text-lg font-semibold">
        <MailCheck className="size-5 text-primary" aria-hidden /> Check your inbox
      </h2>
      <p className="text-muted-foreground">
        We&rsquo;ve sent an email to <span className="font-medium text-foreground">{email}</span>. Open the link in it to
        confirm your address, then sign in.
      </p>
      <p className="text-sm text-muted-foreground">
        Nothing there after a few minutes? Check your spam folder. Signing in with this email and your password also
        sends a new link.
      </p>
      <Button asChild variant="outline">
        <Link href={loginHref}>Go to sign in</Link>
      </Button>
    </div>
  );
}
