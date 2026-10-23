# Contributing

## License

FreeTheLights is licensed under the GNU General Public License, version 3
or later, with an additional permission to combine it with SimHub (see
`LICENSE.md` and `LICENSE-EXCEPTION.md`). By contributing, you agree that
your contribution is licensed under the same terms, including that
permission.

## Branches

- `main` always builds without warnings and passes all tests.
- Every change is developed on its own branch off `main`, never on `main`
  itself. Name it after its purpose: `feature/<topic>`, `fix/<topic>`,
  `refactor/<topic>`, `docs/<topic>` or `tools/<topic>`.
- A branch is a series of isolated, self-contained commits. It is rebased
  onto `main` before it is merged, so history stays linear.
- Clean up a branch before merging: squash "wip" and "fix typo" commits into
  the commit they belong to, and split commits that do more than one thing.

## Commits

The rules follow the Linux kernel's guidelines
("Submitting patches", "Describe your changes", "Separate your changes").

### One logical change per commit

- Each commit does one thing: a bug fix, a feature, a refactoring, a
  formatting change. Don't mix them; a refactoring that a feature needs is a
  commit of its own, placed before the feature.
- Each commit builds without warnings and passes the tests on its own, so
  that `git bisect` works at every step.
- Tests belong in the same commit as the code they test. Documentation
  (`README.md`, `CLAUDE.md`) changes go with the change that makes them
  necessary.
- Don't commit unrelated whitespace or line ending changes.

### Subject line

```
<area>: <summary in imperative mood>
```

- **Area:** the part of the code the commit changes, in lower case:
  `engine`, `settings`, `cardata`, `simhub` (`SimHubData/`, preset building,
  Connect), `effect`, `ui`, `plugin` (`FreeTheLightsPlugin.cs`), `tests`,
  `tools`, `build`, `docs`. Use a narrower area when it is clearer
  (`ui/inspector`, `engine/flags`). For a change that spans several areas,
  name the one it is about.
- **Summary:** imperative mood, as if giving an order to the code base:
  "Add", "Fix", "Remove", not "Added" or "Adds". Start with a capital
  letter, no period at the end.
- At most 72 characters in total, ideally around 50.

Examples:

```
engine: Fix proximity LEDs filling from the wrong side on columns
ui/inspector: Show mixed colors when several elements are selected
cardata: Look up car files through the manifest
```

### Body

- Separate it from the subject with a blank line. Wrap lines at 72
  characters (URLs, log output and code may be longer).
- Describe **what** problem the commit solves and **why** it is solved this
  way. The diff already shows how. Say what the user sees: wrong behavior
  before, behavior after.
- Write in the imperative mood ("Make the renderer cache parsed colors"),
  not about yourself ("I changed", "This patch").
- The commit must stand on its own: don't rely on the branch name, a chat
  or an issue for context. Quote the relevant part when referencing one.
- Mention side effects and anything that was deliberately left out, for
  example when the settings format version is bumped (which resets the
  users' settings).
- A trivial commit (typo, obvious one-liner) may have no body.

### Trailers

At the end, after a blank line, in this order:

- `Fixes: <12-char sha> ("<subject>")` when fixing a bug introduced by an
  earlier commit.
- `Link: <url>` for an issue, discussion or external reference.
- `Co-Authored-By: Name <email>` for every co-author, including AI
  assistants.

### Example

```
engine: Release LEDs when no frame was rendered for a second

When DataUpdate stopped (game closed, plugin error), the effects kept
showing the last frame, so the wheel stayed frozen on the last RPM state
until SimHub was restarted.

Store the render time with each frame and let FrameStore return no frame
once it is older than one second. The effects then show transparent
LEDs and the effects below FreeTheLights in the profile take over again.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Pull requests

The title of a pull request becomes its line in the release notes, so
write it for drivers ("Show the debris flag in iRacing"). Label it
`enhancement` or `bug` to list it under "New features" or "Fixes";
`skip-release-notes` leaves it out (`.github/release.yml`).

## Releases

Only admins of the repository publish releases.

1. Bump the version in `src/FreeTheLights/Properties/AssemblyInfo.cs`
   (`AssemblyVersion` and `AssemblyFileVersion`, `X.Y.Z.0`) in a commit
   `build: Bump the version to X.Y.Z`, merged like any other change.
   Versions follow semantic versioning; the release is tagged `vX.Y.Z`.
2. On an up-to-date, clean `main`, run `sh tools/release.sh -d`. It
   builds and tests the release, tags it, and creates a draft release
   with `FreeTheLights.dll`, its SHA-256 and the notes generated from the
   pull requests merged since the previous release.
3. Review the notes on GitHub and publish the draft. Running the plugin
   then offers the update to its users (it only looks at published,
   non-pre-release releases of this repository).

The repository's settings guard this (set once by an admin):

- a tag ruleset for `v*` that blocks creating, updating and deleting
  these tags, with the admin role on the bypass list;
- a branch ruleset for `main` that requires pull requests and blocks
  force pushes;
- two-factor authentication on the admin accounts.
