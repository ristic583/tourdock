# ADR 0013: Tour photos and object storage

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
Agencies upload tour photos. Uploads are **untrusted input**: they can be any file, be huge, carry malware or scripts
disguised as images, or leak the photographer's location through EXIF GPS data. Photos are read far more often than written,
so they should be served by storage or a CDN, not streamed through the API.

## Decision

**Storage: S3-compatible object storage, accessed with the AWS SDK.** Moving to AWS S3 (or similar) is configuration only.
- Locally it's **RustFS** (Apache-2.0). **MinIO was the original plan, but its repository is archived and its community
  images are no longer published** on Docker Hub, so building on it would mean depending on an unmaintained server.
- The bucket policy allows **anonymous `GetObject` on single objects only**. Listing the bucket is denied, so photos
  can't be enumerated.
- Keys are unique per photo (`tours/{tourId}/{photoId}/{variant}.webp`) and never overwritten, so objects carry
  `Cache-Control: public, max-age=31536000, immutable`. A CDN can sit in front of the bucket unchanged.
- Photos are served from a **different origin** than the API, so even a malicious file couldn't run script on the API's origin.

**Upload pipeline:** `POST /agencies/{id}/tours/{tourId}/photos`, multipart, managers only.
1. **Size:** at most 10 MB. The request is rejected with `413` before the body is read when `Content-Length` is too big.
2. **Real format:** detected from the bytes by the decoder. The file name and declared content type are ignored.
   Only JPEG, PNG and WebP are accepted. A PDF or SVG named `photo.jpg` is rejected.
3. **Decompression bombs:** dimensions are read from the header **before** decoding, with limits of 10,000 px per side and
   at least 600 px on the short side.
4. **Re-encoding:** every image is decoded and re-encoded as WebP (quality 80) in three variants: thumbnail 480 px,
   medium 1280 px and large 2048 px on the longest side, never upscaled. Re-encoding drops EXIF (including GPS), XMP, ICC
   and anything appended to the file. **Camera orientation is applied first**, so portrait phone photos aren't sideways.
5. Files are stored, **then** the photo is recorded. If recording fails, the uploaded files are deleted again.
6. **Deleting** a photo removes the row and queues file deletion in the **outbox** (ADR 0011), so storage cleanup is retried
   until it succeeds.

**Domain rules** (`Tour` aggregate): at most 20 photos per tour, exactly one cover (the first photo by default; enforced by a
partial unique index too), and an explicit display order. Search results show the cover's medium image.

**Image library: SkiaSharp (MIT).** It handles decoding, resizing and WebP encoding, with native assets that work on Alpine (musl).
ImageSharp was considered. Its Six Labors Split License only grants Apache-2.0 terms in specific cases (OSI open source,
small companies), which doesn't clearly cover this non-OSI, source-available project.

## Consequences
- ✅ Only clean, re-encoded images that are safe to cache ever reach storage, and the API doesn't serve image traffic.
- ⚠️ Processing happens during the request (about 100 ms for a typical photo). At larger scale, uploads would go straight to
  storage through **pre-signed PUT URLs** and be processed asynchronously (queue + worker), with quarantine until validated.
- ⚠️ If the app crashes between "store files" and "record photo", the files can be orphaned. A periodic sweep comparing
  storage keys with the database would clean those up (not implemented).
