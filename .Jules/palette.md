# Palette's Journal

## 2024-05-23 - Destructive Action Visibility
**Learning:** In Unity Custom Editors, destructive actions (like file deletion or clearing data) often blend in with standard actions, increasing the risk of accidental data loss.
**Action:** Apply `GUI.backgroundColor = Color.red` (or a softened red) to buttons that perform irreversible actions, resetting the color immediately after. This provides a clear visual warning to the user.
