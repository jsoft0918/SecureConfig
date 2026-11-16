# Security Policy

## Reporting a vulnerability

Please **do not report security vulnerabilities through public issues.** Instead, use GitHub's
[private vulnerability reporting](../../security/advisories/new) (**Security → Report a vulnerability**).
We will review your report and respond as quickly as possible.

## Threat model summary

| Protects against | Does not protect against |
|---|---|
| Secrets leaking through source code, Git repositories, config files, backups, screenshots, or AI coding tools | Compromise of server administrator privileges |
| Leaks of only `applicationHost.config`, the registry, or an environment variable dump (cannot be decrypted without the key) | Arbitrary code execution inside the app process (the app must see the plaintext) |
| Tampering with the ciphertext (detected via the AES-GCM authentication tag → rejected, previous values kept) | Isolation between apps on the same server that share the same master key |

For details, see the "Security notes" section of [docs/DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md).

## Best practices

- **Never commit plaintext JSON or exported files to the repository.** (The default `.gitignore` blocks `*.json` and `*.key`.)
- Do not widen key file permissions (never grant Everyone or Users access).
- Keep only `local` values on developer machines.
