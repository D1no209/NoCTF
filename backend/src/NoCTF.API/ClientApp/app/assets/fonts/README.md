# Sarasa Gothic web fonts

Source: [Sarasa Gothic v1.0.42](https://github.com/be5invis/Sarasa-Gothic/releases/tag/v1.0.42).
License: SIL Open Font License 1.1, preserved in the repository's
`LICENSES/Sarasa-Gothic-OFL.txt` and included in the application image.

| Faces | Purpose | Upstream archive SHA-256 |
| --- | --- | --- |
| SarasaUiSC-Regular / Bold | Interface, headings, notifications and chart labels | `daba2085409fb8a23dfc22f36e02e8b901a08510a3ac95ce3b0fb7161e6925bd` |
| SarasaMonoSC-Regular / Bold | Code, terminal input and monospaced data | `aa2150e99eb38c5f9d3a00fe58e3f90a9d89495c795a8ac80934d1c3e6c377ee` |

Download `SarasaUiSC-TTF-1.0.42.7z` and `SarasaMonoSC-TTF-1.0.42.7z`
from the release above. Extract only the regular and bold faces into a temporary
source directory. From the repository root, build with FontTools 4.60.1 and Brotli:

```sh
python backend/scripts/Build-Sarasa-Webfonts.py \
  --source /path/to/extracted-ttfs \
  --output backend/src/NoCTF.API/ClientApp/app/assets/fonts/split
```

The script separates basic Latin/punctuation, GB2312 level-one Han characters,
and bounded remaining ranges. `split/fonts.css` uses `unicode-range` so browsers
download only the ranges used by displayed text. This is not a subset of current
UI translations: the generated files collectively retain the complete upstream
character map and variation sequences. The script verifies that coverage after
conversion, retains layout features, and records source/output SHA-256 hashes in
`split/manifest.json`.

Font roles live in `app/assets/css/main.css`; generated declarations retain
`font-display: swap`. The regular and bold faces provide normal text; existing
explicit italic styles retain the browser's synthetic oblique rendering. Fonts
are served from the same origin, without a font CDN dependency. Do not restore
the monolithic 8–9 MB WOFF2 faces: they delay first-time loading on slow links.
