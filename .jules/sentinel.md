## 2024-05-22 - Prevent Content Spoofing via Fallback Logic
**Vulnerability:** The application defaulted to displaying the first available resource when a requested resource was not found in the allowed list. This allowed an external configuration file to display a misleading caption (from the file) while showing an unrelated valid image/video (from the fallback), creating a spoofing vulnerability.
**Learning:** "Last resort" or "Fail Open" logic in resource loaders can compromise data integrity and user trust. Always "Fail Closed" (show nothing or an error) when a specific resource is not found.
**Prevention:** Ensure that lookup functions return an error code or null when a match is not found, rather than returning a default value that could be misinterpreted as the requested item.

## 2024-10-25 - Enforce HTTPS for External Content
**Vulnerability:** Loading content via cleartext HTTP allows for potential Man-in-the-Middle (MITM) attacks and exposes user viewing habits (privacy leak).
**Learning:** Even in game engines like Unity/VRChat, ensuring transport security is critical when fetching external resources.
**Prevention:** Enforce HTTPS upgrade logic in URL parsers. Automatically upgrade `http:` schemes to `https:` or reject them.
