# Security Notes

This document tracks known security issues that have not yet been fixed.
Critical issues have already been resolved (see git log for XXE and shell injection fixes).

---

## Fixed (resolved on branch `install-mono-for-mac-linux`)

| Issue | Location | Fix commit |
|-------|----------|------------|
| XXE via XmlDocument.LoadXml | Game.cs, XMLConfig.cs, AutoMapper.cs, MapForm.cs | `22ef75e`, `496d504` |
| Shell injection via Interaction.Shell | FormMain.cs, Command.cs, ScriptExplorer.cs | `16407e1`, `7bc57c8` |

---

## Known Issues (not yet fixed)

### High

**H1 — Unquoted shell paths in Process.Start (Command.cs)**
- **File:** `Core/Command.cs` lines 2116, 2144
- **Issue:** `Arguments` values are built from `ParseGlobalVars(...)` output (game scripting input). A path containing an embedded double-quote character could break argument quoting and cause unintended behavior in the launched editor.
- **Impact:** Argument quoting robustness — not a direct injection vector since `FileName`/`Arguments` are already separated, but hardening is warranted.
- **Fix:** Sanitize or properly escape double-quotes in the path values before passing to `Arguments`.

**H2 — SSL certificate validation (Utility.cs)**
- **File:** `Utility/Utility.cs` lines 148–164
- **Issue:** SSL certificate validation uses a hardcoded certificate for Simutronics. Commented-out fallback code (lines 157–158) suggests incomplete validation logic. The hardcoded certificate must be updated via a recompile if Simutronics rotates their cert.
- **Impact:** Certificate pinning brittleness; MITM risk if the cert is rotated and the client isn't rebuilt.
- **Fix:** Load the pinned certificate from a user-updatable file, or implement a more robust certificate validation strategy.

**H3 — Plugin DLL loading without signature enforcement (PluginServices.cs)**
- **File:** `Utility/PluginServices.cs` lines 44, 79, 150
- **Issue:** DLL files from the user-configured plugin directory are loaded via `Assembly.Load(File.ReadAllBytes(...))`. An MD5 hash is computed but not enforced as a blocklist/allowlist check. If the plugin directory is writable by another process, DLLs could be swapped for malicious ones.
- **Impact:** Local privilege escalation on multi-user systems or if the plugin directory is in a world-writable location.
- **Fix:** Enforce an allowlist of known-good hashes, or require DLLs to be Authenticode-signed.

**H4 — Temp directory DLL planting (EmbeddedAssembly.cs)**
- **File:** `Utility/EmbeddedAssembly.cs` lines 57, 81, 84
- **Issue:** Embedded assemblies are extracted to a predictable temp path (`Path.GetTempPath() + fileName`) and loaded with `Assembly.LoadFile()`. SHA1 validation is performed, but the check occurs after the file is written — a TOCTOU (time-of-check/time-of-use) race window exists on multi-user systems.
- **Impact:** DLL planting attack on shared systems.
- **Fix:** Write to a unique temp path (e.g., using `Path.GetTempFileName()`), validate hash before loading, and delete immediately after loading.

### Medium

**M1 — XPath injection in XMLConfig (XMLConfig.cs)**
- **File:** `Utility/XMLConfig.cs` lines 204, 207, 237, 243, 272
- **Issue:** User-controlled path strings are passed directly into `SelectSingleNode(node)` XPath queries. If attacker-controlled input reaches these paths, XPath injection is possible.
- **Impact:** Could allow reading unintended config nodes.
- **Fix:** Validate/sanitize node and key path arguments; consider using compiled XPath expressions.

**M2 — Regex ReDoS in script engine (Script.cs)**
- **File:** `Script/Script.cs` lines 358, 362, 506, 510, 3367
- **Issue:** User-defined trigger patterns and `waitfor` strings are compiled with `new Regex(...)` without a timeout. A malicious or accidentally catastrophic regex (e.g., `(a+)+$`) will freeze the UI thread.
- **Impact:** Denial of service — application hangs until the process is killed.
- **Fix:** Compile regexes with `new Regex(pattern, options, TimeSpan.FromSeconds(5))` to enforce a match timeout.

**M3 — Path traversal in image file cache (FileHandler.cs)**
- **File:** `Utility/FileHandler.cs` lines 52, 54
- **Issue:** A `filename` parameter derived from game server data is combined with `Path.Combine()` to form a cache path. While `gamecode` is validated for a "GS" prefix, `filename` is not checked for path traversal sequences (`../`, `..\`).
- **Impact:** A malicious server could write files outside the intended cache directory.
- **Fix:** Call `Path.GetFileName(filename)` to strip any directory components before passing to `Path.Combine()`.

### Low / Informational

**L1 — Hardcoded SSL certificate in source (Utility.cs)**
- **File:** `Utility/Utility.cs` line 150
- **Issue:** The Simutronics server certificate is embedded as a literal string in source code. Requires recompile to update.
- **Fix:** Load from an external, user-updatable certificate store or bundle file.

**L2 — UseShellExecute and cross-platform (FormMain.cs, Command.cs, ScriptExplorer.cs)**
- **File:** Multiple — all `Process.Start(ProcessStartInfo)` sites added in the shell injection fix
- **Issue:** All `ProcessStartInfo` instances use `UseShellExecute = true`, which works correctly on Windows. On Mono/Linux/Mac (targeted by the Mono porting work on this branch), behavior differs — `UseShellExecute` may need to be `false` with explicit shell/open command invocations.
- **Fix:** Revisit all 24 `Process.Start` sites when implementing Mono platform support. Wrap with `#if WINDOWS` guards or add a platform-aware helper method.
