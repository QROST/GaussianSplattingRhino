# Native host acceptance

Record Rhino version, OS/architecture, graphics backend, asset hash/count and results. A build or archive check is not native acceptance. Do not test through automated desktop control.

1. Restart Rhino with the independent package installed. Verify the publisher is CuriousArc and `GSRhinoPanel` opens without a startup error.
2. Run `GSRhinoProbe`. Check red/teal color, smooth Gaussian edges and the anisotropic ellipse.
3. Import `fixtures/gaussian-sh3.ply`. Orbit through several views, pan, zoom, switch wireframe and GSRhino modes, resize the viewport and change display DPI, including 125%/150% scaling and moving between monitors. Logical/device size reporting alone must not repeatedly rebuild a stationary frame; a real resize or camera move must rebuild. Check the expected proxy in other modes and stable full-color GS display in GS mode.
4. Import a real scene without reducing its count. Compare the decoded/imported count, record peak memory and time to the first full frame, then navigate repeatedly. A temporary rebuilding proxy is expected; permanent disappearance is a failure.
5. Test an opaque mesh, Brep/NURBS surface and point cloud in front of, behind and through the splats; add a clipping plane. Record each combination separately. Repeat with the opaque objects selected/highlighted as well as unselected. Test transparent Rhino geometry as an explicitly unsupported/experimental combination.
6. Import multiple USD GS prims with nested transforms, non-uniform scale, instances, stage units and Y/Z up. Compare their placement and view-dependent color with the reference scene. Ordinary mesh USDZ must return a clear non-GS message.
7. Cancel during decode/preparation; switch or close documents during import; check no partial insertion, stalled UI or unhandled exception.
8. Undo/redo, copy/move/scale/rotate an imported instance, import the same source twice, delete it, save/reopen and restart. Temporarily remove a cache and verify a visible diagnostic instead of silent loss.
9. Open an existing 3DM containing compatible v1 GS references. Switch to GSRhino mode without re-importing. Verify a nonzero instance/compatible-reference count, correct placement/color and no document modification. Check missing/corrupt source references report resource errors rather than success. An empty document must say no GS was recognized and create no empty projection jobs.
10. Perform the [USD import/export checks](usd-exchange.md#manual-acceptance), including transformed copies, units, all four extensions, missing assets and cancellation.
11. Repeat on Windows Rhino and Intel Mac. Passing Apple Silicon Mac does not close these rows.

Use `GSRhinoDiagnostics` for the local log/cache locations. Logs and asset paths may be private; inspect before sharing. No network service is required for these checks after the optional USD decoder is installed.

## Beta.4 recovery and setup

- Install only the Yak package on a clean user profile. Open USD setup instructions, copy the command, and verify it resolves to the installed script on macOS and Windows (including spaces in the path). Check missing, incompatible and working runtimes. Close the panel during a check.
- In a disposable test document, back up a GS cache, corrupt that test cache, then reload using a valid reference or re-import the original source. Verify the complete GS returns, the damaged bytes remain in a `.corrupt-*` backup and existing placements remain intact. Do not use a production asset for this destructive fixture.
- Show two resources together with one missing/loading. The viewport must report partial readiness even while the healthy GS is visible; diagnostics should agree.
