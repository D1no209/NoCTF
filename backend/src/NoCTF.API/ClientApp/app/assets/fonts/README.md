# Sarasa Gothic web fonts

Source: [Sarasa Gothic v1.0.42](https://github.com/be5invis/Sarasa-Gothic/releases/tag/v1.0.42).
License: SIL Open Font License 1.1, preserved in the repository's
`LICENSES/Sarasa-Gothic-OFL.txt` and included in the application image.

| Faces | Purpose | Upstream archive SHA-256 |
| --- | --- | --- |
| SarasaUiSC-Regular / Bold | Interface, headings, notifications and chart labels | `daba2085409fb8a23dfc22f36e02e8b901a08510a3ac95ce3b0fb7161e6925bd` |
| SarasaMonoSC-Regular / Bold | Code, terminal input and monospaced data | `aa2150e99eb38c5f9d3a00fe58e3f90a9d89495c795a8ac80934d1c3e6c377ee` |

Download `SarasaUiSC-TTF-1.0.42.7z` and `SarasaMonoSC-TTF-1.0.42.7z`
from the release above. Extract only the regular and bold faces. Convert each
TTF with FontTools 4.60.1 and Brotli:

```sh
fonttools ttLib.woff2 compress SarasaUiSC-Regular.ttf
fonttools ttLib.woff2 compress SarasaUiSC-Bold.ttf
fonttools ttLib.woff2 compress SarasaMonoSC-Regular.ttf
fonttools ttLib.woff2 compress SarasaMonoSC-Bold.ttf
```

The conversion preserves the complete upstream character map and glyphs; it does
not subset characters to the current UI translations. The CSS font roles and
`font-display: swap` declarations live in `app/assets/css/main.css`. The regular
and bold faces provide normal text; existing explicit italic styles retain the
browser's synthetic oblique rendering. Fonts are served from the same origin,
without a font CDN dependency.
