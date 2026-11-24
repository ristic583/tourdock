import { z } from "zod";

// Mirrors the API's validators, so most mistakes are caught before a round trip.
// The API stays the source of truth: its field errors are mapped back onto the same fields.
export const loginSchema = z.object({
  email: z.string().trim().min(1, "Enter your email.").max(254),
  password: z.string().min(1, "Enter your password.").max(128),
});

export const registerSchema = z.object({
  displayName: z.string().trim().min(1, "Enter your name.").max(80, "Use at most 80 characters."),
  email: z.email("Enter a valid email address.").max(254),
  password: z.string().min(10, "Use at least 10 characters.").max(128, "Use at most 128 characters."),
});

export type LoginInput = z.infer<typeof loginSchema>;
export type RegisterInput = z.infer<typeof registerSchema>;

export function zodFieldErrors(error: z.ZodError): Record<string, string> {
  const result: Record<string, string> = {};
  for (const issue of error.issues) {
    const key = issue.path.join(".");
    result[key] ??= issue.message;
  }
  return result;
}
