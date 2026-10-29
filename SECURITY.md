# Security Policy

## Supported versions

Halo has no released versions yet. Security fixes are made on the `main` branch.

| Version | Supported |
|---|---|
| `main` (source) | Yes |
| Development MSIX builds you made yourself | Rebuild from the latest `main` |

## Reporting a vulnerability

**Please do not report security vulnerabilities in public issues, discussions or pull requests.**

Report them privately through GitHub's private vulnerability reporting:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Fill in the advisory form.

Direct link: <https://github.com/Ajaykumar-Mallameeda/HaloIsland/security/advisories/new>

Please include:

- what the issue is and its likely impact;
- the steps to reproduce it, and the Windows version and Halo commit you used;
- any proof-of-concept, kept to the minimum needed to show the problem.

Do not include real personal data, credentials or other people's information in your report.

## What to expect

Halo is maintained by one person in their spare time, so these are goals, not guarantees:

- an acknowledgement within 7 days;
- an assessment, and a plan or a request for more information, within 30 days;
- credit in the advisory and the changelog when the fix is published, unless you prefer to stay anonymous.

Please give us reasonable time to fix the issue before you disclose it publicly.

## Scope

In scope:

- the Halo source in this repository (`src/`), and the packaging scripts in `packaging/`;
- local data handling under `%LOCALAPPDATA%\ProjectHalo`, such as logs leaking user content or unsafe file handling;
- the shelf, clipboard and drag-out features, such as opening or running files unexpectedly.

Out of scope:

- vulnerabilities in Windows, .NET or third-party packages themselves (report them upstream);
- development MSIX packages that you built and signed yourself;
- issues that need an attacker who already has full control of the user's account or machine.

## Secrets and signing material

This repository never contains signing keys, certificates or credentials. If you find something that looks like a
secret in the repository or its history, please report it privately using the steps above rather than opening an
issue.
