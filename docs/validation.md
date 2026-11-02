# Validation

Candidate: **0.1.0-beta.2**. Maintainer: **CuriousArc · qrost**. Date: 2026-11-02.

## Beta.2 existing-document repair

The user confirmed beta.1 installed, then opened an existing 3DM and changed display modes. Diagnostics reported zero GS instances and zero input while claiming a complete frame. The reference recognizer accepted only the native namespace, so compatible existing GS blocks were omitted before any projection or drawing. The empty-frame message concealed this cause.

Beta.2 reads unambiguous version-1 GS metadata across namespaces, verifies the referenced PLY against its SHA-256, and caches the complete data without rewriting the original document, asset or instance transform. Sixteen new reference tests cover namespace isolation, ambiguity, preserved data, missing/corrupt caches and cancellation. Fifteen display-policy tests cover empty, loading, failed, hidden, partially ready and projected-zero scenes. Empty placement sets no longer enqueue projection work. Native visual confirmation of the repair remains pending the user's restart and retest.

## Verified locally

- Complete Release solution build on Apple Silicon macOS using .NET SDK 8.0.129: **zero warnings and errors**. Rhino SDK target: 8.32.26160.13001.
- **232 .NET tests passed**, zero failures/skips. Coverage includes full PLY encodings and SH, covariance, projection, sorting, opacity, depth reference, camera/DPI stability, mode-registration policy, canonical PLY roundtrip, SPLAT, SPZ, USD bridge protocol validation, compatible GS reference/cache recovery and truthful display status.
- Retained beta.1 decoder evidence (decoder unchanged in beta.2): **13 real OpenUSD integration checks passed** with the official pinned `usd-core==26.11` runtime. USDA, USDC and USDC-root USDZ produce equivalent full GS parts; hierarchical/non-uniform transforms, instances, half/float fields, complete SH and explicit rejection of unsupported/missing dependencies are covered. Expected warnings in the missing-reference negative fixture are intentional.
- Actual C# `GaussianSceneReader` → isolated Python → GS cache decoding succeeded for `usd-two-parts.usdz`: two parts, four splats, SH3, Z-up, 0.001 meters/unit. The default private runtime location was verified against the plugin's .NET application-data location.
- Six synthetic SPZ fixtures were generated and decoded by the pinned official C++ implementation. Tests compare parameters, transformed positions, covariance and view-dependent SH color. This caught and corrected the rotated coordinate-family mapping. All 13 fixture/generator SHA-256 values match the provenance record. No user model is included.
- Four CPU reference PNGs have closed-form alpha/depth center values. Both procedural Gaussian textures reproduce byte-for-byte. These are mathematical checks, not native screenshots.
- macOS and Windows Yak candidates pass strict entry allowlisting, identity/platform/target checks and byte-for-byte payload comparisons. Each archive contains 40 files. Hashes and source revisions are recorded in `artifacts/latest-package-mac.json` and `artifacts/latest-package-win.json`.
- Release Python scripts compile and the macOS shell wrapper passes syntax checking. The Windows PowerShell wrapper has not been executed on Windows.
- Source/documentation naming checks found no prior product branding or developer home paths. Public project attribution uses CuriousArc and qrost. Required third-party copyright notices are preserved.

The native display implementation incorporates previously tested Apple Silicon Mac behavior, including colored anisotropic splats, improved navigation, basic clipping and deferred display-mode registration. This is a reused implementation baseline, not native acceptance of the newly assembled GSRhino package.

## Reproduce

```sh
dotnet build GSRhino.sln -c Release
dotnet test tests/GSRhino.Tests/GSRhino.Tests.csproj -c Release --no-build
python3 scripts/create-gs-assets.py --check
dotnet run --project tools/GSRhino.Reference -c Release -- --output artifacts/reference
dotnet run --project tools/GSRhino.Reference -c Release -- --inspect fixtures/usd-two-parts.usdz
```

Run `fixtures/usd-verify.py` with the private USD runtime's Python executable to repeat the 13 integration checks. Ordinary .NET tests do not require that optional runtime. The SPZ fixture generator, upstream revision and digests are in `fixtures/spz-official-provenance.json`.

## Remaining native acceptance

Manual acceptance of this independent package remains pending. Windows and Intel Mac execution, the complete depth/alpha matrix, save/reopen/Undo lifecycle and measured sustained frame rate require the checks in [host acceptance](host-acceptance.md). A Windows-tagged managed package is not proof of Windows Rhino execution.

Local CLI installation, when performed, is recorded separately in `artifacts/latest-install.json` with installed-byte verification. It does not establish that Rhino loaded the new plugin or rendered it successfully. No desktop UI automation or remote publication was performed.

Yak emits a content-name warning because assembly `GSRhino.Rhino` and package slug `gsrhino` differ. Both identities are intentional; platform, Rhino target, plugin GUID and payload verification still pass.
