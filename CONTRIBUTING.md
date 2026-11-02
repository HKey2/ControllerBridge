# Contributing Guidelines

Thank you for your interest in this project!

- **Issues**: Bug reports and feature suggestions via GitHub Issues are welcome.
- **Pull Requests**: We currently do not accept Pull Requests. This project is maintained solely by the primary author. Any submitted PRs will be closed without review.

## Product and local development sources

Keep verification code, synthetic UI data, capture runners, diagnostic firmware variants, measurement scripts and generated test evidence in the Git-ignored `test/` directory. Do not add them to product source files, product command-line options, or the repository. Normal firmware and configurator builds must succeed without `test/` being present. Keep operational input logging and validation required for normal use in product code.

README release notes describe user-visible changes, such as UI bug fixes and setting changes. Put verification procedures, counts and investigation details in local evidence or the dedicated verification record, not in README release notes.
