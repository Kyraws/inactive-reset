# Local issue tracker

Agent implementation issues and specifications live under `.scratch/` and are
ignored by Git. They are working records, separate from current public guides
and historical evidence under `docs/`.

## Layout

- One feature or maintenance effort per directory: `.scratch/<feature-slug>/`.
- Specification: `spec.md` within that directory.
- One file per issue: `issues/<NN>-<slug>.md`, numbered from `01`.
- Include a `Status:` line near the top. Append discussion under `## Comments`.

If a workflow requests publication to an issue tracker, write these local files.
If it requests an existing ticket, read the referenced file or number. Do not
create remote issues or send messages unless the user has requested that action.

## Research maps

For work using a research map, keep `map.md` beside the issues. Each child issue
has a `Type:` (`research`, `prototype`, `grilling`, or `task`) and a status.
Use `Blocked by: NN, NN` when necessary. Claim only an unblocked issue, set
`Status: claimed` before working, and set `Status: resolved` after recording its
answer under `## Answer`. Add the result and its evidence pointer to the map.

Use current documentation for implementation facts. Label proposed behaviour
and build-specific experiments explicitly; archived claims are not requirements.
