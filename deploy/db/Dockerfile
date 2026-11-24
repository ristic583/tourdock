# PostgreSQL with the pgvector extension, on the same Alpine image the database has always used.
# Staying on Alpine (musl) keeps existing data volumes valid: a glibc-based image (such as pgvector/pgvector) would sort
# text differently under the indexes that are already on disk.
ARG POSTGRES_IMAGE=postgres:18.6-alpine3.24

FROM ${POSTGRES_IMAGE} AS build
ARG PGVECTOR_VERSION=0.8.7
ARG PGVECTOR_COMMIT=f37c13f68b57d2c3472b2214fbcff699d6d34876
RUN apk add --no-cache build-base git
RUN git clone --branch "v${PGVECTOR_VERSION}" --depth 1 https://github.com/pgvector/pgvector.git /src/pgvector \
    && test "$(git -C /src/pgvector rev-parse HEAD)" = "${PGVECTOR_COMMIT}"
WORKDIR /src/pgvector
# OPTFLAGS="" builds portable code (no -march=native, the image runs on other CPUs); with_llvm=no skips JIT bitcode,
# which would need clang and only speeds up JIT-compiled queries.
RUN make OPTFLAGS="" with_llvm=no \
    && make install OPTFLAGS="" with_llvm=no DESTDIR=/out

FROM ${POSTGRES_IMAGE}
COPY --from=build /out/usr/local/lib/postgresql/ /usr/local/lib/postgresql/
COPY --from=build /out/usr/local/share/postgresql/extension/ /usr/local/share/postgresql/extension/
