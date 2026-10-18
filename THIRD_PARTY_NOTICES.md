# Third-party notices

NoCTF's AGPL-3.0-only license applies to the project's original work. Third-party
components retain their own licenses and attribution requirements.

## Shared UI source

The shared Vue UI components are based on [shadcn-vue](https://github.com/unovue/shadcn-vue)
and the [shadcn/ui](https://github.com/shadcn-ui/ui) component system. Their MIT
license texts and copyright notices are preserved in
[LICENSES/shadcn-vue-MIT.txt](LICENSES/shadcn-vue-MIT.txt) and
[LICENSES/shadcn-ui-MIT.txt](LICENSES/shadcn-ui-MIT.txt).

## Fonts

The self-hosted Sarasa UI SC and Sarasa Mono SC faces are from
[Sarasa Gothic v1.0.42](https://github.com/be5invis/Sarasa-Gothic/releases/tag/v1.0.42).
Their SIL Open Font License 1.1 and copyright notices are preserved in
[LICENSES/Sarasa-Gothic-OFL.txt](LICENSES/Sarasa-Gothic-OFL.txt).
The regular and bold TTF faces are split into local WOFF2 unicode-range assets.
Together the subsets preserve every upstream encoded character and variation
sequence. Source URLs, checksums and conversion instructions are recorded beside
the font assets.

## Package dependencies

Frontend, documentation and backend dependencies are identified by their package
manifests, Bun lockfiles and `backend/Directory.Packages.props`. Preserve the licenses
and notices that those packages supply when distributing them. This file documents
copied UI sources and does not replace a dependency's own license.

Project-created illustrations and backgrounds are documented in [ASSET_CREDITS.md](ASSET_CREDITS.md).
