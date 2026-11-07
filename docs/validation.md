# Validation

Candidate: **0.1.0-beta.5** (macOS preview). Maintainer: **CuriousArc · qrost**. Date: 2026-11-06.

## Beta.5 language and release preparation

- Fresh complete Release solution build on Apple Silicon macOS: **zero warnings/errors; 296 .NET tests passed**, zero failures/skips. The compiled informational version is `0.1.0-beta.5`.
- The 13 language checks are included in the full suite. They verify Rhino language precedence, English fallback, Simplified Chinese selection for Chinese locales, background messages, unchanged numeric/UI culture and user content, and all embedded translation format arguments. The Chinese JSON is embedded directly in the Core assembly, so no culture-specific satellite directory is needed in the Yak archive.
- Current tracked source/documentation identity checks pass. Project attribution is CuriousArc and qrost; no developer home paths or excluded branding are included. Required third-party notices remain intact.
- Prior actual OpenUSD integration and native-data exchange evidence below is retained: the bridge and rendering algorithms have not changed in this release. The full current .NET suite rechecks import/export protocol and cancellation behavior.
- Only a macOS Yak preview is prepared for distribution. Exact archive bytes, SHA-256, source commit and package checks are recorded in `artifacts/latest-package-mac.json`; source push, installation and publication are separate operations. Windows and Intel Mac execution and native bilingual layout/rendering acceptance remain pending.

## Beta.4 prerelease repairs

- Complete Release solution build on Apple Silicon macOS: **zero warnings/errors; 283 .NET tests passed**, zero failures/skips. The four inherited-pipe process regressions run on Unix and are explicitly skipped on Windows; they are not Windows execution evidence.
- **25 actual OpenUSD 26.11 tests passed**, including inherited and instanced purpose visibility, preview/full/custom-purpose material bindings, collection binding, unregistered Gaussian APIs and valid composed overrides/removals. Earlier full-data interchange coverage remains included.
- Fractional-DPI tests exercise 125%/150% integer rounding, alternating logical/device size reports, both projection types, real resize and real camera movement. Normalization follows native-size authentication and preserves strict pose/frustum comparisons. The first accepted sampling grid remains stable until independently observed window bounds change.
- Partial loading/error status remains visible while a healthy resource renders; only a fully ready accepted frame hides the status overlay. Writing objects into the document is reported separately from viewport readiness.
- Cache tests cover verified fallback, corrupt-target replacement, valid-cache reuse, cancellation and concurrent recovery. Eight additional independent processes on macOS waited on the same held lock, then produced one repair and seven reuses; the original corrupt bytes were retained exactly once. Windows execution remains unverified.
- The panel now locates the setup script inside the installed package, copies a shell-quoted command, and probes the local OpenUSD version/schema without installing anything. Cancellation also bounds output collection when a launcher exits before its pipes close. The real 15-second readiness timeout and public USD import cancellation/workspace cleanup passed. Closing a check restores an actionable panel status.
- The actual C# → isolated OpenUSD process → GS decoding path passed with `usd-two-parts.usdz`: two parts, four splats, complete SH3, Z-up, 0.001 meters/unit, and no decoder warnings. The default installed runtime reported ready.
- Independent native verification remains pending: actual window-bound behavior across monitors, navigation, depth/clipping, restart and Windows/Intel Mac. A one-pixel resize observed before the next native inventory refresh may settle on the next normal draw; polling does not force continuous redraws.


## Beta.3 USD import/export

- Complete Release build: zero warnings/errors; **246 .NET tests passed**, zero failures/skips.
- **18 actual OpenUSD 26.11 tests passed**, covering all four output extensions, binary USDC identity, USDZ packaging, complete SH0–3, opacity endpoints, empty/duplicate names, antialias metadata, units/up axis, reflection/shear and large-coordinate rebasing. Unsupported or malformed data fails explicitly.
- Production USD interchange check: eight conversions across USD, USDC, USDA and USDZ, with two instances of the same SH3 asset. Full counts, names, antialias flag, units, world positions, covariance and directional SH colors passed.
- A local **212,864-splat** scene passed a full-data USDZ exchange check: full count, every SH coefficient and every activated opacity checked; maximum local-position component difference was **2.384185791015625e-7 source units**. The read/export/read cycle took about **23.1 seconds** on this machine. This is an offline exchange measurement, not a Rhino frame-rate guarantee. The private input/output is not included in source or packages.
- Real child-process cancellation/failure retained an existing target, killed the conversion process tree and removed private workspace data. Successful output replaced the target and decoded again.
- Native import/export UI, cross-unit Rhino document placement and third-party viewer acceptance remain pending. Export follows float32 GS attribute precision; no arbitrary-double lossless claim is made. Extent is not authored for external viewer framing/culling.

## Beta.2 existing-document repair

The user confirmed beta.1 installed, then opened an existing 3DM and changed display modes. Diagnostics reported zero GS instances and zero input while claiming a complete frame. The reference recognizer accepted only the native namespace, so compatible existing GS blocks were omitted before any projection or drawing. The empty-frame message concealed this cause.

Beta.2 reads unambiguous version-1 GS metadata across namespaces, verifies the referenced PLY against its SHA-256, and caches the complete data without rewriting the original document, asset or instance transform. Sixteen new reference tests cover namespace isolation, ambiguity, preserved data, missing/corrupt caches and cancellation. Fifteen display-policy tests cover empty, loading, failed, hidden, partially ready and projected-zero scenes. Empty placement sets no longer enqueue projection work. Native visual confirmation of the repair remains pending the user's restart and retest.

## Earlier beta.2 verification

- Complete Release solution build on Apple Silicon macOS using .NET SDK 8.0.129: **zero warnings and errors**. Rhino SDK target: 8.32.26160.13001.
- **232 .NET tests passed**, zero failures/skips. Coverage includes full PLY encodings and SH, covariance, projection, sorting, opacity, depth reference, camera/DPI stability, mode-registration policy, canonical PLY roundtrip, SPLAT, SPZ, USD bridge protocol validation, compatible GS reference/cache recovery and truthful display status.
- Retained beta.1 decoder evidence (decoder unchanged in beta.2): **13 real OpenUSD integration checks passed** with the official pinned `usd-core==26.11` runtime. USDA, USDC and USDC-root USDZ produce equivalent full GS parts; hierarchical/non-uniform transforms, instances, half/float fields, complete SH and explicit rejection of unsupported/missing dependencies are covered. Expected warnings in the missing-reference negative fixture are intentional.
- Actual C# `GaussianSceneReader` → isolated Python → GS cache decoding succeeded for `usd-two-parts.usdz`: two parts, four splats, SH3, Z-up, 0.001 meters/unit. The default private runtime location was verified against the plugin's .NET application-data location.
- Six synthetic SPZ fixtures were generated and decoded by the pinned official C++ implementation. Tests compare parameters, transformed positions, covariance and view-dependent SH color. This caught and corrected the rotated coordinate-family mapping. All 13 fixture/generator SHA-256 values match the provenance record. No user model is included.
- Four CPU reference PNGs have closed-form alpha/depth center values. Both procedural Gaussian textures reproduce byte-for-byte. These are mathematical checks, not native screenshots.
- macOS and Windows Yak candidates pass strict entry allowlisting, identity/platform/target checks and byte-for-byte payload comparisons. Each archive contains 40 files. Hashes and source revisions are recorded in `artifacts/latest-package-mac.json` and `artifacts/latest-package-win.json`.
- Release Python scripts compile and the macOS shell wrapper passes syntax checking. The Windows PowerShell wrapper has not been executed on Windows.
- Source/documentation identity checks passed; no developer home paths are included. Public project attribution uses CuriousArc and qrost. Required third-party copyright notices are preserved.

Native GSRhino acceptance remains pending for colored anisotropic splats, navigation, clipping and display-mode registration. Automated checks do not establish these host results.

## Reproduce

```sh
dotnet build GSRhino.sln -c Release
dotnet test tests/GSRhino.Tests/GSRhino.Tests.csproj -c Release --no-build
python3 scripts/create-gs-assets.py --check
dotnet run --project tools/GSRhino.Reference -c Release -- --output artifacts/reference
dotnet run --project tools/GSRhino.Reference -c Release -- --inspect fixtures/usd-two-parts.usdz
```

Run `fixtures/usd-verify.py` with the private USD runtime's Python executable to repeat the 25 current integration checks. Ordinary .NET tests do not require that optional runtime. The SPZ fixture generator, upstream revision and digests are in `fixtures/spz-official-provenance.json`.

## Remaining native acceptance

Manual acceptance of this independent package remains pending. Windows and Intel Mac execution, the complete depth/alpha matrix, save/reopen/Undo lifecycle and measured sustained frame rate require the checks in [host acceptance](host-acceptance.md). A Windows-tagged managed package is not proof of Windows Rhino execution.

Local CLI installation, when performed, is recorded separately in `artifacts/latest-install.json` with installed-byte verification. It does not establish that Rhino loaded the new plugin or rendered it successfully. Automated validation does not use desktop UI automation or establish remote publication; installation and publication results are recorded separately.

Yak emits a content-name warning because assembly `GSRhino.Rhino` and package slug `gsrhino` differ. Both identities are intentional; platform, Rhino target, plugin GUID and payload verification still pass.
