## 2024-05-22 - Prevent Content Spoofing via Fallback Logic
**Vulnerability:** The application defaulted to displaying the first available resource when a requested resource was not found in the allowed list. This allowed an external configuration file to display a misleading caption (from the file) while showing an unrelated valid image/video (from the fallback), creating a spoofing vulnerability.
**Learning:** "Last resort" or "Fail Open" logic in resource loaders can compromise data integrity and user trust. Always "Fail Closed" (show nothing or an error) when a specific resource is not found.
**Prevention:** Ensure that lookup functions return an error code or null when a match is not found, rather than returning a default value that could be misinterpreted as the requested item.

## 2024-05-23 - Enforce HTTPS for External Resources
**Vulnerability:** External configuration lists could supply `http://` URLs, potentially allowing mixed content or insecure data transmission if the allowlist check was lenient.
**Learning:** Even if the allowed list is secure, runtime inputs should be sanitized to match the expected security standard. Implicitly trusting the protocol provided by external sources can lead to security downgrades.
**Prevention:** Automatically upgrade `http://` to `https://` at the ingestion point (parsing logic) to ensure all runtime URLs comply with security standards before they are used or compared against allowed lists.

## 2024-10-18 - Prevent Caption Spoofing via Loose Filename Matching
**Vulnerability:** `ImageLoader` used a fallback matching strategy that compared filenames if an exact URL match wasn't found. This allowed attackers to display trusted images with arbitrary captions by providing a URL ending in the same filename.
**Learning:** Convenience features like "fuzzy matching" often introduce security gaps ("Confused Deputy"). "Fail Closed" implies "Fail Exact" - do not guess matches based on partial data.
**Prevention:** Stick to exact string matching for allowlists. Remove logic that attempts to match resources based on partial indicators (like filenames) to prevent context spoofing.
