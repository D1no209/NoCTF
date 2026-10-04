# Security policy

This policy covers vulnerabilities in NoCTF's platform code and deployment tooling.
Use ordinary issues for non-security bugs and feature requests.

## Supported versions

Security fixes target the latest published NoCTF release. Older deployments should
upgrade to a supported release, following its migration and deployment instructions.
The `main` branch is active development and can contain unreleased changes.

## Report a vulnerability

Use [GitHub's private vulnerability reporting form](https://github.com/D1no209/NoCTF/security/advisories/new)
when the repository's **Security → Report a vulnerability** entry is available.
Reports submitted there are private to the reporter and the maintainers involved.

If that entry is unavailable, open an issue asking for a private security contact.
Do not include the vulnerability, exploit, credentials or affected production data
in that public issue. Maintainers can then arrange a private reporting channel.

A private report should include:

- The affected version or commit, deployment roles and runtime provider.
- A minimal reproduction using an isolated installation and synthetic data.
- Expected and observed behavior, potential impact, and relevant redacted logs.
- Any suggested fix or mitigation and your preferred attribution name.

Coordinate disclosure with maintainers while the report is assessed and a fix is
prepared. Response times depend on maintainer availability; this project does not
promise a fixed response deadline or a paid support service.

## Testing boundaries

Test only installations you own or are authorized to assess. CTF challenge content
may deliberately contain vulnerabilities; distinguish those from an issue in the
platform's authentication, isolation, evaluation or deployment behavior.
