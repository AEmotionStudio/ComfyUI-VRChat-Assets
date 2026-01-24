# Palette's Journal

## 2024-05-23 - Destructive Action Visibility
**Learning:** In Unity Custom Editors, destructive actions (like file deletion or clearing data) often blend in with standard actions, increasing the risk of accidental data loss.
**Action:** Apply `GUI.backgroundColor = Color.red` (or a softened red) to buttons that perform irreversible actions, resetting the color immediately after. This provides a clear visual warning to the user.

## 2024-05-24 - Actionable Verification
**Learning:** When a tool generates or manages files in a specific directory, users often need to verify the output immediately. Providing a path text field is not enough.
**Action:** Add an "Open" or "Reveal" button next to directory selection fields that opens the OS file explorer to that location. This closes the loop between configuration and verification.

## 2024-05-25 - Copy to Verify
**Learning:** For tools that fetch and parse lists of data, limited UI previews (e.g., first 5 items) are insufficient for full verification.
**Action:** Provide a "Copy to Clipboard" button to allow users to verify the entire dataset in their preferred external text editor.

## 2024-05-27 - Data Density vs. Action Priority
**Learning:** When a component generates a large amount of data (like a URL list), displaying that data via the default inspector can push critical configuration and action buttons off-screen, forcing users to scroll excessively.
**Action:** Use `DrawPropertiesExcluding` to hide the large data fields from the default inspector flow, and manually draw them at the bottom of the inspector (or in a collapsed foldout). This prioritizes the controls that users need to interact with most frequently.
