## 2024-05-23 - Custom Editor Tooltips & Feedback
**Learning:** Unity's IMGUI system (`GUILayout.Button`) does not support separate tooltip parameters. Tooltips must be attached via `new GUIContent("Text", "Tooltip")`. This is easy to miss when writing quick editor tools, leading to opaque "magic buttons" that users are afraid to click.
**Action:** When creating or auditing custom Unity Editors, always wrap button strings in `GUIContent` and provide a tooltip that explains the *consequence* of the action (e.g., "Deleting files" vs "Clearing references").

## 2024-05-23 - Data Preview in Inspectors
**Learning:** Users lack confidence in "Fetch/Import" actions if they can't see what was actually fetched before committing it to the project.
**Action:** Always provide a preview of fetched data (like parsing captions alongside URLs) in the Inspector window so users can verify the parsing logic worked correctly before generating assets.
