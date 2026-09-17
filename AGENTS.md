# SpaceWorld Agent Instructions

## Project

- Unity 2022.3.62f3; 2D game using URP 14.0.12, Input System, and Cinemachine.
- Separate visual art from physical collision when appropriate.
- Keep physical surfaces continuous for slopes and physical stairs. Do not recreate seams or jolts with fragmented colliders.

## Change philosophy

- Inspect before modifying; do not assume the repository or project state.
- Make small, isolated changes and reuse existing systems before creating new ones.
- Do not reformat unrelated files or rewrite large Unity YAML files unless required.
- Preserve GUIDs, `.meta` files, and user-created art and source files.

## Git safety

- Never commit or push without explicit authorization.
- Never use `git add .` or `git add -A`; stage exact paths only.
- Never use `git clean` without explicit authorization or delete untracked files automatically.
- Inspect the diff before a commit and verify staged, tracked-modified, and untracked paths before and after writes.
- `.csproj` and `.sln` are generated Unity/IDE files and must not be tracked.

## Unity safety

- Do not open Unity unless the task requires it.
- Avoid incidental scene, prefab, and ProjectSettings changes; justify any global settings change.
- Before modifying a large scene, identify the exact objects and components affected.
- Do not add gameplay colliders to visual art when invisible continuous surfaces handle collision.

## Player

- `Assets/Scripts/Player/PlayerController.cs` is highly coupled. Change it only when necessary and explain why.
- Prefer external components or systems when reasonable.
- Do not incidentally change validated movement, jump, dash, slopes, wall-slide, ledge, or swing behavior.

## Workflow

AUDIT -> PLAN -> MODIFY -> VERIFY -> DIFF -> TEST -> COMMIT only with explicit approval.

## Validation

- Check relevant compile or static errors, inspect `git diff`, and verify that no unexpected paths appeared.
- Distinguish automated checks from manual or visual validation.
- Do not claim a gameplay or visual change is correct until it has been tested appropriately.
