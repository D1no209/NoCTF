# Frontend and backend API gaps

This file records UI capabilities that need additional server contract work. They are
not implemented through handwritten routes or production fallbacks.

## Open

None.

## Resolved

### Discover configured image upload limits

- **Frontend need:** show and prevalidate the exact avatar and personal-wallpaper
  upload limits selected by the deployment.
- **Current backend authority:** `Uploads:MaximumAvatarBytes` and
  `Uploads:MaximumWallpaperBytes`; upload endpoints enforce them and return HTTP 413.
- **Contract:** `GET /api/v1/platform/configuration` exposes only the avatar and
  wallpaper byte limits required by public clients. It does not expose object keys,
  stored file metadata, or unrelated administrative upload limits.
- **Frontend behavior:** shows the exact configured limits and prevalidates the
  cropped avatar upload and source wallpaper file. Missing capabilities from an
  older server fall back to configuration-neutral copy and server enforcement.
- **Authority:** upload endpoints still enforce the same singleton
  `FileUploadLimits` values and return HTTP 413. The public contract is advisory,
  not an authorization or storage decision.
