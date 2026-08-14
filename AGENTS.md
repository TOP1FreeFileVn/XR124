# XR124 Project Rules

## Unity system issues

- Before changing code, package versions, project settings, or generated caches for any Unity, package, Editor, build, or platform-system error, research the issue online first using official documentation, issue trackers, or relevant community reports.
- State the verified likely cause and proposed fix before applying the change; do not make speculative Unity-system fixes that require the user to ask for research again.

## Change reporting

- After every code edit, report each changed file and the exact current line range for every added or modified code block as a clickable file link labelled `FileName.cs (line N)`.
- For each reported range, state its purpose in Vietnamese in one short sentence.
- Verify the reported line ranges after the final edit, because line numbers can shift during implementation.

## Meta XR hand skeletons

- Before using or changing any Meta XR hand bone ID, verify the runtime `OVRSkeleton.SkeletonType` and consult the current Meta XR SDK documentation or source. Use `XRHand_*` IDs for `XRHandLeft` and `XRHandRight`, and use legacy `Hand_*` IDs only for `HandLeft` and `HandRight`; never assume that IDs from the two skeleton layouts are interchangeable because their numeric values can resolve to different joints.

## AGENTS.md preservation

- Never delete, replace, rewrite, or otherwise remove any existing text from `AGENTS.md` without the user's explicit permission. When asked to add a rule, preserve every existing character and append only the requested content unless the user explicitly authorizes another kind of edit.
