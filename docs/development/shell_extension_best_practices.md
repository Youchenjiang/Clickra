# Shell Extension Development Best Practices

To prevent future certification failures and ensure high compatibility across Windows 11 builds, follow these guidelines.

## 1. COM Identity Management
*   **Centralize CLSID**: Define the CLSID in a single `Guids` class and use it for all comparisons in `DllGetClassObject`.
*   **Manifest Synchronization**: Always verify that `AppxManifest.xml` (MSIX) and `src/resources/AppxManifest.xml` (Sparse Package) use the exact same CLSID as the code.
*   **Fresh IDs for Hotfixes**: If a previous version had registration issues, consider migrating to a completely new CLSID to bypass potential registry cache conflicts.

## 2. Robust Interface Support
*   **Permissive QI**: When a shell requests an interface that behaves exactly like `IExplorerCommand`, support it even if the IID is slightly different.
*   **Logging fallback**: Always have a `Logger` implementation ready in the code (commented out or behind a compiler flag) to capture failing IIDs during testing.
*   **IEnumExplorerCommand**: Ensure sub-command enumerators also support alternative IIDs, otherwise sub-menus will disappear.

## 3. NativeAOT Compatibility
*   **UnmanagedCallersOnly**: Ensure all entry points (`DllGetClassObject`, `DllCanUnloadNow`) and VTable methods are correctly attributed.
*   **Unsafe Operations**: NativeAOT requires explicit `unsafe` blocks when handling function pointers (`delegate*`).
*   **Memory Management**: Since we are in an unmanaged context, ensure `Marshal.AllocCoTaskMem` and `FreeCoTaskMem` are used correctly for strings and objects.

## 4. Packaging & Deployment
*   **Digital Signature**: Never distribute an MSIX or Shell DLL without a valid digital signature. Windows 11 may block unsigned COM servers.
*   **Sparse Package vs MSIX**: Remember that developers usually test with Sparse Packages (via `Register-SparsePackage`), but users get the full MSIX. Always verify both paths.

## 5. Testing Checklist
- [ ] Context menu appears on both Windows 10 (classic) and Windows 11 (modern).
- [ ] Sub-menus load and list all items.
- [ ] Clicking a menu item correctly launches the application with the right arguments.
- [ ] Verify functionality on at least one ARM64 and one x64 device if possible.
