"use client";

import { createContext, useCallback, useContext, useState, type ReactNode } from "react";

import type { Departure } from "@/lib/api/contracts";

/** `pickedAt` is when the departure was picked, so deadlines can be compared without reading the clock in render. */
type Selection = { departure?: Departure; pickedAt?: number; select: (departure?: Departure) => void };

const SelectionContext = createContext<Selection>({ select: () => {} });

/**
 * The departure picked in the booking panel, shared with the rest of the tour page
 * (the cancellation policy shows its refund deadlines for that departure).
 */
export function DepartureSelectionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ departure?: Departure; pickedAt?: number }>({});
  const select = useCallback((departure?: Departure) => setState({ departure, pickedAt: departure ? Date.now() : undefined }), []);
  return <SelectionContext value={{ ...state, select }}>{children}</SelectionContext>;
}

export function useDepartureSelection(): Selection {
  return useContext(SelectionContext);
}
