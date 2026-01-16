## 2024-05-22 - VRCUrl.Get() Interop Overhead
**Learning:** Calling `VRCUrl.Get()` inside loops in UdonSharp creates significant performance overhead due to interop calls between the Udon VM and the host engine.
**Action:** Cache `VRCUrl` string values into a native `string[]` array at startup (e.g., in `Start()`) and use the cached array for lookups and comparisons in loops.
