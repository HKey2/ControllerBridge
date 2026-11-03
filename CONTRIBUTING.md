# Contributing Guidelines

Thank you for your interest in this project!

- **Issues**: Bug reports and feature suggestions via GitHub Issues are welcome.
- **Pull Requests**: We currently do not accept Pull Requests. This project is maintained solely by the primary author. Any submitted PRs will be closed without review.

## Product and local development sources

Keep verification code, synthetic UI data, capture runners, diagnostic firmware variants, measurement scripts and generated test evidence in the Git-ignored `test/` directory. Do not add them to product source files, product command-line options, or the repository. Normal firmware and configurator builds must succeed without `test/` being present. Keep operational input logging and validation required for normal use in product code.

README release notes describe user-visible changes, such as UI bug fixes and setting changes. Put verification procedures, counts and investigation details in local evidence or the dedicated verification record, not in README release notes.

## Version labels

The main version comes from `firmware-version.txt`. A new main version is displayed without a GUI revision suffix. Use `GUI rN` only to distinguish EXE-only updates while that main version stays unchanged; reset the GUI revision when the main version changes instead of carrying the number forward. A correction that removes a stale suffix restores the base label and does not create another GUI revision.

Keep the application title, current README/documentation labels and release metadata consistent. Use `guiRevision: 0` for the unsuffixed base release in package metadata. Dated verification records and archived artifacts may retain the labels actually used at the time.
