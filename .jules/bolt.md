## 2024-05-22 - Array Resizing Bottlenecks in UdonSharp

**Learning:** UdonSharp environments often encourage array usage over Lists due to historical or performance reasons (interop overhead). However, standard patterns like "resize array by +1 for each new item" (Shlemiel the Painter's algorithm) are catastrophic for bulk loading operations, turning $O(N)$ operations into $O(N^2)$.

**Action:** When handling bulk data (like loading a list of URLs from a string), always parse to a temporary buffer or count first, then allocate the final array once. Avoid `Array.Resize` (or manual resize) inside a loop.
