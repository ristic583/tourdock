import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { GoogleSignIn } from "./google-sign-in";

describe("GoogleSignIn", () => {
  it("links to the Google start route, keeping where the visitor was going", () => {
    render(<GoogleSignIn enabled next="/bookings" />);
    expect(screen.getByRole("link", { name: "Continue with Google" }).getAttribute("href")).toBe("/api/auth/google/start?next=%2Fbookings");
  });

  it("explains why the last Google sign-in failed", () => {
    render(<GoogleSignIn enabled failure="google_unverified" />);
    expect(screen.getByRole("alert").textContent).toContain("isn't verified");
  });

  it("shows nothing when Google isn't offered and nothing went wrong", () => {
    const { container } = render(<GoogleSignIn enabled={false} failure="something-else" />);
    expect(container.innerHTML).toBe("");
  });
});
