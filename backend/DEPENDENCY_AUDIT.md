# ImageSharp audit exceptions

Reviewed on 2026-10-11 for `SixLabors.ImageSharp` 3.1.12. These are limited
reachability exceptions, not upstream vulnerability fixes. NuGet audit and
warnings-as-errors remain enabled. The exception items apply only to this pinned
version and cover the following five advisory URLs.

| Advisory | Affected operation | NoCTF boundary |
|---|---|---|
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44) | Lazy ICC CLUT parser in v3 | Avatar identify/decode explicitly skips metadata. Wallpaper processing never accesses ICC entries and removes the profile before encoding. |
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4) | Floating-point TIFF with `HistogramEqualization` | TIFF is rejected before identify/decode; neither processor uses histogram equalization or `HalfVector4`. |
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q) | TIFF CCITT T4 encoding | Outputs use explicit WebP encoders; there is no production TIFF encoder. |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w) | TIFF CCITT T6 encoding | TIFF input is rejected and output is WebP. |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g) | BigTIFF IFD decoding loop | Both processors reject TIFF before identify/decode. |

The only production ImageSharp users are the avatar and wallpaper processors;
profile covers use the avatar processor. They detect the format first and accept
only JPEG, PNG and WebP. Dimension, pixel-count and single-frame checks remain in
place. Wallpaper retains EXIF until `AutoOrient`, then removes EXIF, XMP and ICC
metadata; it does not parse ICC entries or convert color profiles.

`ImageProcessingSecurityTests` exercises classic TIFF and BigTIFF rejection at
both ingress boundaries and verifies that an embedded PNG ICC profile is omitted
from avatar output. Existing processor tests cover supported formats and output
normalization.

Reassess these exceptions when upgrading ImageSharp, adding a production caller,
changing accepted formats, enabling color-profile processing, adding histogram
operations or changing output encoders. A new advisory is not automatically
excluded. Any future package upgrade must review the current security fixes and
build prerequisites rather than retaining this version's exceptions.
