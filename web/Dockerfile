# TourDock web (Next.js). Build context: ./web
# Uses Docker's built-in BuildKit frontend. Keeping this local avoids an unnecessary Docker Hub
# request before a build, which previously left local development relying on manually copied assets.
ARG NODE_IMAGE=node:26.13.0-alpine3.24

# ---- Dependencies (cached until package-lock.json changes) ----
FROM ${NODE_IMAGE} AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund

# ---- Build: `output: "standalone"` traces only the files the server needs ----
FROM ${NODE_IMAGE} AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

# ---- Runtime: plain Node, no npm or source code, non-root ----
FROM ${NODE_IMAGE} AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
# Files stay owned by root (read-only for the app); only Next's cache directory is writable.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
RUN mkdir -p .next/cache && chown node:node .next/cache
USER node
EXPOSE 3000
# Every 2s while starting, then once a minute: each check execs a process
# through dockerd/containerd, which keeps an idle host busy.
HEALTHCHECK --interval=60s --timeout=3s --start-period=60s --start-interval=2s --retries=3 \
  CMD wget -qO- http://127.0.0.1:3000/api/health > /dev/null || exit 1
CMD ["node", "server.js"]
