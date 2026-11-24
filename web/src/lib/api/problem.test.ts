import { describe, expect, it } from "vitest";

import { ApiError, networkError, normalizeFieldName, toActionError, toApiError, toFieldErrors } from "./problem";

describe("ProblemDetails handling", () => {
  it("maps a validation problem to camelCase field errors", () => {
    const error = toApiError(400, {
      status: 400,
      title: "Validation failed",
      code: "Validation.Failed",
      errors: {
        email: ["'Email' is not a valid email address."],
        Password: ["Too short.", "Second message is ignored."],
        "Items[0].Name": ["Required."],
      },
    });

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe("Validation.Failed");
    expect(error.fieldErrors).toEqual({
      email: "'Email' is not a valid email address.",
      password: "Too short.",
      "items.0.name": "Required.",
    });
    expect(error.userMessage).toBe("Please check the highlighted fields.");
  });

  it("prefers a friendly message for known codes", () => {
    const error = toApiError(400, { status: 400, code: "Auth.InvalidCredentials", detail: "raw detail" });
    expect(error.userMessage).toBe("Email or password is incorrect.");
  });

  it("falls back to the API detail, then to a message per status", () => {
    expect(toApiError(409, { status: 409, code: "Some.NewCode", detail: "Slot changed." }).userMessage).toBe(
      "Slot changed.",
    );
    expect(toApiError(503, "<html>bad gateway</html>").userMessage).toBe(
      "The service is temporarily unavailable. Please try again.",
    );
  });

  it("represents network failures as status 0", () => {
    const error = networkError();
    expect(error.status).toBe(0);
    expect(error.userMessage).toMatch(/can't reach TourDock/);
  });

  it("builds a serializable action error", () => {
    const error = toActionError(toApiError(400, { status: 400, errors: { displayName: ["Required."] } }));
    expect(error).toEqual({
      message: "Please check the highlighted fields.",
      fieldErrors: { displayName: "Required." },
    });
    expect(toActionError(new Error("boom"))).toEqual({ message: "Something went wrong. Please try again." });
  });

  it("ignores empty messages", () => {
    expect(toFieldErrors({ email: ["", "  "] })).toEqual({});
    expect(normalizeFieldName("Tours[2].Seasons[0].StartDate")).toBe("tours.2.seasons.0.startDate");
  });
});
