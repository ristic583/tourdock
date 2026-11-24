/**
 * RFC 9457 ProblemDetails handling. The API returns `application/problem+json` with a stable `code`
 * (for example `Auth.InvalidCredentials`) and, for validation failures, per-field `errors`.
 */

export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
};

export type FieldErrors = Record<string, string>;

export function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.status === "number" || typeof v.title === "string" || typeof v.code === "string";
}

/** Friendly messages for codes the UI can explain better than the API's generic detail. */
const MESSAGES: Record<string, string> = {
  "Auth.InvalidCredentials": "Email or password is incorrect.",
  "Auth.SignInNeedsConfirmedEmail": "Confirm your email to sign in. We've sent a new link to your inbox.",
  "Auth.InvalidRefreshToken": "Your session has expired. Please sign in again.",
  "Auth.TooManyAttempts": "Too many failed attempts. Please wait a few minutes and try again.",
  "Concurrency.Busy": "Someone else is booking right now. Please try again.",
  "Slot.NotEnoughSeats": "There aren't enough seats left on this departure.",
  "Slot.PrivateNotAllowed": "This departure can no longer be booked privately.",
  "Slot.SharedNotAllowed": "This departure no longer has shared seats available.",
  "Booking.HoldExpired": "Your reservation hold expired. Please choose the departure again.",
  "Payment.BookingNotPayable": "This booking can no longer be paid for.",
  "Network.Unavailable": "We can't reach TourDock right now. Please try again in a moment.",
};

const STATUS_MESSAGES: Record<number, string> = {
  401: "Please sign in to continue.",
  403: "You don't have access to this.",
  404: "We couldn't find what you were looking for.",
  409: "That conflicts with a recent change. Please refresh and try again.",
  429: "Too many attempts. Please wait a moment and try again.",
  503: "The service is temporarily unavailable. Please try again.",
};

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.code = problem.code;
    this.problem = problem;
  }

  /** Field errors keyed by camelCase field name, first message only (what a form shows). */
  get fieldErrors(): FieldErrors {
    return toFieldErrors(this.problem.errors);
  }

  /** A message suitable for showing to the user. */
  get userMessage(): string {
    return userMessage(this.status, this.problem);
  }
}

export function toApiError(status: number, body: unknown): ApiError {
  return new ApiError(status, isProblemDetails(body) ? body : { status });
}

export function networkError(): ApiError {
  return new ApiError(0, { code: "Network.Unavailable", title: "Network error" });
}

/** `Email` / `items[0].Name` → `email` / `items.0.name` (the paths React Hook Form uses). */
export function normalizeFieldName(name: string): string {
  return name
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .map((part) => (part ? part.charAt(0).toLowerCase() + part.slice(1) : part))
    .join(".");
}

export function toFieldErrors(errors: Record<string, string[]> | undefined): FieldErrors {
  const result: FieldErrors = {};
  if (!errors) return result;
  for (const [key, messages] of Object.entries(errors)) {
    const first = messages.find((m) => m.trim().length > 0);
    if (first) result[normalizeFieldName(key)] = first;
  }
  return result;
}

export function userMessage(status: number, problem: ProblemDetails): string {
  if (problem.code && MESSAGES[problem.code]) return MESSAGES[problem.code];
  if (problem.errors && Object.keys(problem.errors).length > 0) return "Please check the highlighted fields.";
  if (problem.detail) return problem.detail;
  return STATUS_MESSAGES[status] ?? "Something went wrong. Please try again.";
}

/** Serializable error a Server Action returns to its form. */
export type ActionError = {
  message: string;
  code?: string;
  fieldErrors?: FieldErrors;
};

export type ActionResult<T = undefined> = { ok: true; data: T } | { ok: false; error: ActionError };

export function toActionError(error: unknown): ActionError {
  if (error instanceof ApiError) {
    const fieldErrors = error.fieldErrors;
    return {
      message: error.userMessage,
      code: error.code,
      ...(Object.keys(fieldErrors).length > 0 ? { fieldErrors } : {}),
    };
  }
  return { message: "Something went wrong. Please try again." };
}
