# Contributing Guidelines

Thank you for your interest in this project!

- **Issues**: Bug reports and feature suggestions via GitHub Issues are welcome.
- **Pull Requests**: We currently do not accept Pull Requests. This project is maintained solely by the primary author. Any submitted PRs will be closed without review.

## Product and local development sources

Keep verification code, synthetic UI data, capture runners, diagnostic firmware variants, measurement scripts and generated test evidence in the Git-ignored `test/` directory. Do not add them to product source files, product command-line options, or the repository. Normal firmware and configurator builds must succeed without `test/` being present. Keep operational input logging and validation required for normal use in product code.

Repository documentation is for the tool's end users: describe features, operation, settings, supported scope, relevant limitations and user-visible changes. Keep all verification procedures, counts, results, reports and evidence under the Git-ignored `test/` directory. Do not include them in README files, public documentation or commit messages/comments. A dedicated verification document outside `test/` is not an exception.

## Routine updates and release packages

Update `dist/ControllerBridge.exe` during normal project work, and update `dist/ControllerBridge.uf2` only when a firmware update is needed. Create or refresh a distribution ZIP only when explicitly requested by the user, in conjunction with a Release. An ordinary edit, build or EXE update does not authorize packaging, committing or publishing a Release.

Except for special changes, advance versions per commit, not per request or build. Keep one version throughout the same uncommitted change set. GUI-only versions that do not affect firmware retain the numeric main version and use the GUI revision policy below.

## Version labels

The main version comes from `firmware-version.txt`. A new main version is displayed without a GUI revision suffix. Use `GUI rN` only to distinguish EXE-only updates while that main version stays unchanged. Treat the unsuffixed base release as the first edition: the first suffixed GUI-only revision is `GUI r2`, followed by `GUI r3`, `GUI r4`, and so on. This follows the committed v0.19.0 and v0.20.16 releases, both of which moved directly from an unsuffixed title to `GUI r2`. Keep all edits and rebuilds in the same uncommitted change set at the same GUI revision. Do not increment the revision for each request or local build; advance it only for a later GUI revision after the preceding change set has been committed. Reset the label when the main version changes instead of carrying the number forward. A correction that removes a stale suffix restores the base label and does not create another GUI revision.

Keep the application title, current README/documentation labels and release metadata consistent. Use `guiRevision: 0` as the metadata marker for an unsuffixed base release; it does not imply a displayed r0. Set `guiRevision: 2` for the first suffixed GUI revision, matching `GUI r2`, then increment for later GUI releases. Dated verification records and archived artifacts may retain the labels actually used at the time.
