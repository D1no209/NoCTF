# NoCTF challenge authoring templates V2

This directory targets NoCTF `0.1.0-alpha.142` and later. It is independent from
`docs/challenge-authoring-templates/`; the original templates remain the compatibility baseline for
older deployments and Checkers that do not receive Fix input.

V2 demonstrates the optional AWDP `checkerFixInput` contract. When explicitly enabled, the Runner
validates an uploaded `.tar.gz` once, creates one canonical tar, injects that tar into the Target,
and later opens the same canonical file again for the Checker. The Checker starts only after the
second injection has completed and sees the contents at `/noctf/fix`.

- Start with [awdp-challenge-template.md](awdp-challenge-template.md).
- Use [starter-kits/awdp](starter-kits/awdp) for a runnable Target, Checker, Fix samples, and smoke test.
- Existing AWDP definitions that omit `checkerFixInput` continue to resolve it as `false`; they do not
  need to be resaved.

No file in this directory contains a real Flag, production callback token, production hostname, or
solution for an actual contest challenge.
