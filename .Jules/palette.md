# Palette's Journal

## 2024-05-23 - Destructive Action Visibility
**Learning:** In Unity Custom Editors, destructive actions (like file deletion or clearing data) often blend in with standard actions, increasing the risk of accidental data loss.
**Action:** Apply `GUI.backgroundColor = Color.red` (or a softened red) to buttons that perform irreversible actions, resetting the color immediately after. This provides a clear visual warning to the user.

## 2024-05-24 - Actionable Verification
**Learning:** When a tool generates or manages files in a specific directory, users often need to verify the output immediately. Providing a path text field is not enough.
**Action:** Add an "Open" or "Reveal" button next to directory selection fields that opens the OS file explorer to that location. This closes the loop between configuration and verification.
