import type { FieldValues, Path, UseFormSetError } from "react-hook-form";

import type { ActionError } from "@/lib/api/problem";

/**
 * Puts a Server Action / API error onto a React Hook Form: per-field messages go to the matching
 * fields, and the general message goes to `root.server`. Errors for fields the form doesn't have
 * are folded into the general message so they're never silently dropped.
 */
export function applyActionError<T extends FieldValues>(
  error: ActionError,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): void {
  const unknown: string[] = [];
  let focused = false;
  for (const [name, message] of Object.entries(error.fieldErrors ?? {})) {
    if ((fields as readonly string[]).includes(name)) {
      setError(name as Path<T>, { type: "server", message }, { shouldFocus: !focused });
      focused = true;
    } else {
      unknown.push(message);
    }
  }
  const message = unknown.length > 0 ? [error.message, ...unknown].join(" ") : error.message;
  setError("root.server" as Path<T>, { type: "server", message });
}
