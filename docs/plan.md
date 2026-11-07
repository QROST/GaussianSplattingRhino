# GSRhino implementation plan

GSRhino is an independent local Gaussian splat viewer for Rhino 8, by CuriousArc and qrost.
Version target: **0.1.0-beta.5**. The distribution target is a macOS preview; native acceptance remains separate.

1. Establish the standalone repository, package identity, Core contracts, and renderer regression fixtures.
2. Implement the native viewport: anisotropic GS, SH0–3, opacity, background sorting, cancellable import, stable bounds and deferred display-mode registration.
3. Import full GS PLY, SPLAT, SPZ, and official OpenUSD GS scenes. Preserve original source bytes, full affine transforms and all supported SH coefficients. Reject unsupported schemas rather than substituting point clouds.
4. Add bilingual documentation, format fixtures, malformed-input tests and the reference rendering oracle. Package separate macOS and Windows candidates.
5. Install the local macOS candidate through the Rhino package tooling. The user performs native viewport acceptance; Windows native acceptance remains separate.

No artificial splat-count ceiling or silent subsampling. Actual memory/runtime/host limits still apply. No generation service, account system or cloud rendering. Source push and macOS Yak publication follow the verified release package; Windows distribution remains pending native validation.

USD/USDZ decoding uses a pinned, isolated OpenUSD process. PLY/SPLAT/SPZ do not require Python. Unsupported animation, vendor schemas and SH above degree 3 are explicit errors for the first candidate.

## Current milestone

The standalone viewer, file importers, USD export, source/cache preservation, regression fixtures and bilingual documentation are implemented. The current preview candidate is 0.1.0-beta.5; exact automated evidence is recorded in [validation](validation.md). Native GSRhino acceptance and dedicated GPU-renderer/performance work remain separate milestones.

Beta.2 adds read-only compatibility for existing version-1 GS references and truthful empty/loading/error display status. Native verification of this repair remains pending the user's restart and existing-document retest.

Beta.3 adds explicit selected/all top-level GS export to the USD family with cancellation and atomic destination replacement. USDZ is a self-contained GS exchange package; arbitrary Rhino geometry and third-party viewer support are outside this milestone.

Beta.4 repairs fractional-DPI sampling, partial-frame status, purpose-aware USD visibility/material validation and corrupt-cache recovery. The panel provides a local USD runtime check and installed-package setup instructions. Native DPI/navigation, restart, Windows and Intel Mac acceptance remain separate from automated tests.

Beta.5 adds English fallback text with automatic Chinese selection from Rhino’s UI language, or the system/process UI language when Rhino does not provide a valid language. Embedded translations cover the panel, commands and local diagnostics without changing numeric culture or source content. Native bilingual layout checks remain pending.
