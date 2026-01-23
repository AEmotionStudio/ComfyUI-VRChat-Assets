# Bolt's Performance Journal

## Critical Learnings

### UdonSharp Performance Patterns
- **Fail Closed Resource Loading**: Lookup functions must return error codes (e.g., -1) when matches are not found, rather than defaulting to arbitrary values.
- **Avoid Array Resizing in Loops**: UdonSharp has significant overhead for array resizing. Favor batch processing (collecting data in temporary arrays and resizing main storage once) for dynamic lists.
- **Inspector Refresh**: Custom Editor scripts require `EditorUtility.SetDirty(target)` to persist changes and `GUI.FocusControl(null)` + `Repaint()` to refresh UI.
- **Avoid "Shlemiel the Painter"**: Minimizing repeated string concatenation and unnecessary loop iterations is critical in Udon.

## 2024-10-25 - [VRCUrl Interop Caching]
**Learning:** `VRCUrl.Get()` is an expensive interop call. Calling it inside loops (e.g., for string matching) causes frame drops in VRChat.
**Action:** Cache `VRCUrl` string values into a native `string[]` array at startup (`Start`) and use the cached array for all runtime comparisons.

## 2024-05-23 - [Batch vs Iterative Initialization]
**Learning:** `VideoURLProvider` was using iterative array resizing (`AddUrlIndex`) during initialization, causing O(N²) memory pressure at startup. `ImageLoader` already used batching (`AddImageIndicesBatch`).
**Action:** Refactored `VideoURLProvider` to use batch processing for initialization, aligning it with `ImageLoader`'s efficient pattern and removing the inefficient iterative method.

## 2024-10-26 - [O(1) Lookup Mask]
**Learning:** Nested loops for duplicate detection (scanning `_activeUrlIndices` which involves O(N) comparisons inside an O(M) loop) scale poorly (O(N*M)). UdonSharp handles integer array lookups much faster than object/string scans.
**Action:** Implemented a persistent `bool[] _activeUrlMask` to track active indices in O(1) time. This replaces the inner loop in `FindMatchingUrlIndex`, optimizing the entire list processing to O(N).

## 2024-10-27 - [Sequential Search Hint]
**Learning:** Linear searches (O(N)) for matching URLs in `ImageLoader` become O(N*M) when processing lists. Since downloaded lists often match the order of predefined assets, we can optimize for the common case.
**Action:** Implemented a "Search Hint" strategy: cache the last found index and optimistically check `index + 1` before falling back to linear search. This reduces comparisons by ~99% for sorted lists with negligible overhead for random ones.

## 2024-11-20 - [Allocation-Free String Scanning]
**Learning:** `string.Substring()` allocates new memory on every call. Using it inside loops just to check for prefixes (e.g., `line.Substring(pos).StartsWith("http")`) generates significant garbage.
**Action:** Refactored URL extraction to use `line.IndexOf("http", startPos) == startPos`. This performs an in-place check without allocating intermediate strings, reducing GC pressure during playlist parsing.
