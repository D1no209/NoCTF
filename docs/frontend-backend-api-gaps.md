# Frontend and backend API gaps

This file records UI capabilities that need additional server contract work. They are
not implemented through handwritten routes or production fallbacks.

## Open

### Discover configured image upload limits

- **Frontend need:** show and prevalidate the exact avatar and personal-wallpaper
  upload limits selected by the deployment.
- **Current backend authority:** `Uploads:MaximumAvatarBytes` and
  `Uploads:MaximumWallpaperBytes`; upload endpoints enforce them and return HTTP 413.
- **Current frontend behavior:** validates the supported MIME types, displays a
  configuration-neutral limit message, and lets the server enforce the configured
  byte limit.
- **Future contract:** expose both limits through the public platform-capabilities
  response, then generate the SDK and use those values for copy and client-side
  prevalidation.
