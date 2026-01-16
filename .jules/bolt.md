# Bolt's Performance Journal

## Critical Learnings

### UdonSharp Performance Patterns
- **Fail Closed Resource Loading**: Lookup functions must return error codes (e.g., -1) when matches are not found, rather than defaulting to arbitrary values.
- **Avoid Array Resizing in Loops**: UdonSharp has significant overhead for array resizing. Favor batch processing (collecting data in temporary arrays and resizing main storage once) for dynamic lists.
- **Inspector Refresh**: Custom Editor scripts require `EditorUtility.SetDirty(target)` to persist changes and `GUI.FocusControl(null)` + `Repaint()` to refresh UI.
- **Avoid "Shlemiel the Painter"**: Minimizing repeated string concatenation and unnecessary loop iterations is critical in Udon.
