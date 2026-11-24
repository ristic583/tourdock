"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useForm } from "react-hook-form";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { login } from "@/lib/auth/actions";
import { loginSchema, type LoginInput } from "@/lib/auth/schemas";
import { applyActionError } from "@/lib/forms";

export function LoginForm({ next }: { next?: string }) {
  const form = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });
  const { errors, isSubmitting } = form.formState;

  const onSubmit = form.handleSubmit(async (values) => {
    // On success the action redirects, so a returned value always means an error.
    const result = await login(values, next);
    if (result && !result.ok) applyActionError(result.error, form.setError, ["email", "password"]);
    else if (result?.data.fullPageLoad) window.location.assign(result.data.fullPageLoad);
  });

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
      <FormAlert message={errors.root?.server?.message} />
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
      <FormField id="password" label="Password" error={errors.password?.message}>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          aria-invalid={Boolean(errors.password)}
          aria-describedby={describedBy("password", errors.password?.message)}
          {...form.register("password")}
        />
      </FormField>
      <Button type="submit" pending={isSubmitting} className="mt-2 w-full">
        {isSubmitting ? "Signing in…" : "Sign in"}
      </Button>
      <p className="text-center text-sm text-muted-foreground">
        New to TourDock?{" "}
        <Link
          href={next ? `/register?next=${encodeURIComponent(next)}` : "/register"}
          className="font-semibold text-primary hover:text-primary-hover"
        >
          Create an account
        </Link>
      </p>
    </form>
  );
}
