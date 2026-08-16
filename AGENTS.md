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

## Function comments

- When adding or modifying a function, add concise Vietnamese comments with full Vietnamese diacritics that explain the function's purpose, its main behavior, and any non-obvious algorithm or condition so the user can understand and debug it easily.

## MRUK room placement and collision

- Before implementing or changing MRUK room placement, spawning, floor projection, collision, or obstacle avoidance, consult the current Meta MRUK documentation or installed package source and verify that the room and required semantic anchors are initialized.
- Never treat a fixed scene `X/Z` projected onto `FLOOR` as a valid final spawn. After MRUK loads, validate that the complete object bounds are inside the room, outside scene volumes with an appropriate clearance buffer, and free of relevant physics overlaps; if invalid, relocate the object to the nearest valid `FLOOR` position before saving `initialPosition`, movement bounds, or reset state.
- Treat occlusion as rendering only, never as physical collision or valid-space detection. Use MRUK scene queries and physics overlap checks for placement and movement safety.
- When MRUK rejects a requested spawn or movement position, expose the rejection or placement state in runtime debug output; do not silently keep restoring the previous position while an input command remains active.
