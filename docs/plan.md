# GSRhino implementation plan

GSRhino is an independent local Gaussian splat viewer for Rhino 8, by CuriousArc and qrost.
Version target: **0.1.0-beta.2**. Local candidates are not public releases.

1. Establish the standalone repository, package identity, Core contracts, and renderer regression fixtures.
2. Adapt the tested native viewport: anisotropic GS, SH0–3, opacity, background sorting, cancellable import, stable bounds and deferred display-mode registration.
3. Import full GS PLY, SPLAT, SPZ, and official OpenUSD GS scenes. Preserve original source bytes, full affine transforms and all supported SH coefficients. Reject unsupported schemas rather than substituting point clouds.
4. Add bilingual documentation, format fixtures, malformed-input tests and the reference rendering oracle. Package separate macOS and Windows candidates.
5. Install the local macOS candidate through the Rhino package tooling. The user performs native viewport acceptance; Windows native acceptance remains separate.

No artificial splat-count ceiling or silent subsampling. Actual memory/runtime/host limits still apply. No generation service, account system or cloud rendering. No publication or remote source push is part of this task.

USD/USDZ decoding uses a pinned, isolated OpenUSD process. PLY/SPLAT/SPZ do not require Python. Unsupported animation, vendor schemas and SH above degree 3 are explicit errors for the first candidate.

## Current milestone

The standalone viewer, file importers, source/cache preservation, regression fixtures and bilingual documentation are implemented. Release compilation, 201 offline .NET tests, 13 real OpenUSD checks and local Mac/Windows archive verification passed. The initial development version is 0.1.0-beta.1. Native GSRhino acceptance and dedicated GPU-renderer/performance work remain separate milestones.

Beta.2 adds read-only compatibility for existing version-1 GS references and truthful empty/loading/error display status. Native verification of this repair remains pending the user's restart and existing-document retest.
