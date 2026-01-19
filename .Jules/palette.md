## 2024-05-23 - Persistent Editor Settings and Semantic Feedback
**Learning:** Users find it frustrating when non-serialized editor settings (like directory paths) reset on every script reload or domain reload. Using `EditorPrefs` scoped with `Application.dataPath` provides a robust, project-specific persistence mechanism that survives reloads without modifying asset files. Additionally, replacing generic "Info" status boxes with dynamic `MessageType` (Error/Warning) significantly improves the clarity of operation results.
**Action:** When creating custom Editor tools that require file paths or configuration not suitable for serialization on the target component, always implement `EditorPrefs` persistence in `OnEnable`/`OnDisable`. Ensure all status feedback uses appropriate visual severity levels.

## 2024-05-24 - Actionable Inputs and Silent Correction
**Learning:** Adding actionable buttons (like "Open") immediately adjacent to URL input fields significantly speeds up verification workflows. Furthermore, silently correcting common input errors (like leading/trailing whitespace) prevents unnecessary failures and frustration.
**Action:** Always include a verification action next to external resource inputs. Implement auto-trimming or sanitization on `GUI` change checks to handle copy-paste errors gracefully.

## 2024-05-25 - Frictionless Data Entry in Unity Inspectors
**Learning:** In Unity's IMGUI, modifying a text field's backing variable programmatically (e.g., via a "Paste" button) does not automatically update the UI if the field is currently focused or if the frame hasn't repainted. Explicitly calling `GUI.FocusControl(null)` ensures the field releases focus and refreshes its content, while `Repaint()` forces an immediate visual update.
**Action:** When adding "Paste" or "Reset" buttons to Unity Inspectors, always pair the data modification with `GUI.FocusControl(null)` and `Repaint()` to prevent UI desynchronization.
