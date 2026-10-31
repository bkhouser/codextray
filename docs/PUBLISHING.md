# Public GitHub publication handoff

The prepared public repository is a separate local source copy with one reviewed initial commit and an annotated version tag. It contains no prior private Git history, account data, local state, downloaded tools or installed app. The original private checkout remains preserved. No GitHub repository or remote upload is created by preparation.

Recommended public destination: bkhouser/codextray. The prepared Git author uses the verified GitHub account's noreply address, not a personal email. LICENSE retains the owner-approved Brian Houser attribution.

## Review before the first push

Inspect the prepared repository and its log, confirm the intended owner/name, and review SECURITY.md, PRIVACY.md, LICENSE and the latest review report. The synthetic screenshot is illustrative; it contains no actual account values. The public source does not include the private integration design document.

From the prepared repository, these commands create and push a new public GitHub repository **only when the owner authorizes publication**:

```powershell
gh repo create bkhouser/codextray --public --source . --remote origin
git push --set-upstream origin main
git push origin --tags
```

Use an empty repository: do not initialize a second README/license on GitHub. Never push from the original private checkout, import its old branches/tags, or force-push an existing unrelated repository.

## Repository settings after creation

- Enable private vulnerability reporting, Dependabot alerts/security updates and secret-scanning push protection.
- Protect main using a ruleset: require a PR and successful Verify/CodeQL checks, and block force pushes/deletion.
- Keep Actions workflow permissions read-only by default. CodeQL requests security-events write only for its analysis job.
- Confirm Verify and CodeQL actually pass on GitHub. Local actionlint validates syntax; it does not execute hosted analysis.
- Set a concise description, Windows/.NET/Codex topics, and link the README. No website, support-email or maintainer SLA is promised.

## Binary release decision

CI creates a reviewable Windows x64 artifact containing the executable, docs, app/runtime licenses and SHA-256 checksums. It does not create a public Release. Decide signing policy and perform the clean-machine/high-DPI/multi-monitor checks in RELEASING.md before advertising binaries. Checksums verify integrity against the published manifest; they are not a substitute for Authenticode publisher identity.

Repeat dependency/runtime advisories and secret checks on publication day. Keep grant estimates distinct from authoritative billing balances and do not present date-only grant records as verified timestamps.
