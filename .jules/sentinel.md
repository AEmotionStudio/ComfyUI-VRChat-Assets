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

## 2024-10-24 - Enforce HTTPS on Configuration Sources & Rate Limiting
**Vulnerability:** Configuration sources (text files with URLs) could be loaded over insecure HTTP, and update checks could be configured to run too frequently (DoS risk).
**Learning:** Security controls applied to *content* (images/videos) must also apply to the *configuration sources* that dictate that content. Rate limiting is essential for automated polling mechanisms.
**Prevention:** Enforce HTTPS at the config loading stage. Clamp polling intervals to a safe minimum (e.g., 5 seconds).

## 2025-05-21 - Protocol Mismatch in Object Wrappers vs. Internal Logic
**Vulnerability:** The `ImageLoader` and `VideoURLProvider` scripts validated and upgraded URLs to HTTPS in their string cache for comparisons, but failed to update the actual `VRCUrl` objects used for the download/playback requests. This meant the code *looked* secure (validating strings) but *acted* insecurely (requesting HTTP), creating a false sense of security and leaving users vulnerable to MITM attacks.
**Learning:** Validating a shadow copy (cache) of data does not secure the source data. When using object wrappers (like `VRCUrl`), modifying the extracted value does not modify the object itself. Security upgrades must be applied to the actual objects used for operations.
**Prevention:** Explicitly replace or upgrade the source objects (e.g., `predefinedUrls[i] = new VRCUrl(secureUrl)`) when sanitizing inputs, rather than just sanitizing the local variable used for logic checks.

## 2025-05-27 - Removal of Legacy "Cyclical" Fallback
**Vulnerability:** A "cyclical assignment" fallback logic was retained behind a flag (`useGeneratedUrlData`), effectively re-enabling the previously identified Content Spoofing vulnerability. This allowed unrelated images to be displayed with arbitrary captions if the exact URL match failed.
**Learning:** When removing a vulnerability (like a fallback), ensure all variants of that logic are removed, including those hidden behind flags or legacy modes. Security fixes should be comprehensive and not leave "backdoors" for specific configurations.
**Prevention:** Remove the `useGeneratedUrlData` fallback block entirely to enforce strict Allowlist matching in all cases.

## 2025-05-30 - Prevent SSRF via Editor Scripts
**Vulnerability:** `ImageLoaderEditor` and `VideoURLProviderEditor` accepted arbitrary URL schemes (like `file://`) in the "GitHub Raw URL" field, allowing attackers (or malicious configs) to read local files via `WebClient`.
**Learning:** Developer tools running in trusted environments (like Unity Editor) are often overlooked vectors for SSRF/LFI if they process external input without validation. `WebClient` supports `file:` URI scheme by default.
**Prevention:** Explicitly validate the URL scheme (allow only `http` and `https`) before passing user input to networking APIs, even in Editor scripts.
