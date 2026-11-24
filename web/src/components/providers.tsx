"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import { ToastProvider } from "@/components/feedback/toaster";
import { ApiError } from "@/lib/api/problem";

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Retry only what can succeed on a retry: network errors and 5xx. A 4xx won't change.
        retry: (failureCount, error) =>
          failureCount < 2 && (!(error instanceof ApiError) || error.status === 0 || error.status >= 500),
      },
    },
  });
}

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(makeQueryClient);
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>{children}</ToastProvider>
    </QueryClientProvider>
  );
}
