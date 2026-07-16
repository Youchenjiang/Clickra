# Shell Extension Development Best Practices

This is the canonical guide for Clickra's Windows 10/11 shell extension. It
documents the architecture and invariants that must stay aligned between the
NativeAOT DLL, the manifests, and the Explorer COM contract.

## 1. Architecture and COM identity

The shell flow is:

```text
Explorer selection
  -> sparse/MSIX package identity
  -> ClickraShell.dll (NativeAOT)
  -> DllGetClassObject / ClassFactory
  -> IExplorerCommand
  -> IEnumExplorerCommand (Clickra submenu)
  -> Clickra.CLI command arguments
```

Keep the CLSID and interface IDs in [`src/ClickraShell/Guids.cs`](../../src/ClickraShell/Guids.cs)
as the source of truth. The package manifest and the sparse-package manifest
must use the same CLSID as the code.

The current interface IDs are:

| Interface | ID |
|---|---|
| `IUnknown` | `00000000-0000-0000-C000-000000000046` |
| `IClassFactory` | `00000001-0000-0000-C000-000000000046` |
| `IExplorerCommand` | `a08ce4d0-fa25-44ab-b57c-c7b3c3ef1cf0` |
| `IExplorerCommand` compatibility ID | `a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9` |
| `IEnumExplorerCommand` | `c5740441-fa60-492d-944c-354313f8c7b6` |
| `IEnumExplorerCommand` compatibility ID | `a88826f8-186f-4987-aade-ea0cef8fbfe8` |
| `IObjectWithSelection` | `b196b287-bab4-101a-b69c-00aa00341d07` |

Windows builds can request compatibility IDs during submenu discovery. Keep
the compatibility handling and its rationale documented in code comments; do
not replace these values with unverified values copied from another project.

## 2. NativeAOT COM rules

ClickraShell is a NativeAOT shared library. Do not rely on the normal managed
COM callable-wrapper path. Explorer consumes unmanaged vtables, so exported
entry points and vtable methods must use the existing function-pointer pattern
and `[UnmanagedCallersOnly]` declarations.

Rules:

1. Keep `DllGetClassObject` and `DllCanUnloadNow` ABI-compatible and preserve
   their calling convention.
2. Implement `QueryInterface`, `AddRef`, and `Release` for every object type
   (class factory, command, and enumerator).
3. Keep managed logic in ordinary helper methods. An
   `[UnmanagedCallersOnly]` method must not be used as a normal C# call target.
4. Return `E_NOINTERFACE` for unsupported interfaces and `E_NOTIMPL` when a
   method is intentionally unsupported; do not return `S_OK` with an unset
   output pointer.
5. Allocate COM-owned strings with the allocator expected by the interface
   and release them on the corresponding path.

## 3. Memory layout and object lifetime

NativeAOT shell objects are unmanaged memory. Never hand-calculate field
offsets: 64-bit padding can place an `int` after an `IntPtr` at a different
offset than expected. Define the struct once, let the compiler calculate
fields, and use `Marshal.SizeOf<T>()` when allocating storage.

Command and enumerator objects must have independent layouts and lifetimes.
Do not reuse one memory block for different vtable shapes. Every successful
`QueryInterface` increments the reference count, and `Release` must free the
object only when that count reaches zero.

## 4. Packaging invariants

Developers normally exercise the shell through the sparse-package path, while
users receive the full MSIX. Verify both paths before release:

- `packaging/msix/AppxManifest.xml` and `src/resources/AppxManifest.xml` use the
  same CLSID and version. Their package names and publishers may differ by
  deployment context, but each publisher must match its signing certificate.
- `scripts/build_msix.ps1` is the supported packaging entry point; it publishes
  both `Clickra.CLI` and `ClickraShell`, assembles the layout, creates the PRI
  index, and builds `Clickra.msix`.
- A package must be signed with a certificate whose publisher matches the
  manifest. Do not distribute an unsigned MSIX or shell DLL.
- After reinstalling a development package, restart Explorer before judging a
  shell change; Explorer caches COM and menu state.

Do not use a legacy HKCU registry verb script as a substitute for the current
Sparse Package/MSIX flow. Such snippets belong in historical notes only.

## 5. Change checklist

- [ ] CLSID and interface IDs were checked against `src/ClickraShell/Guids.cs`.
- [ ] Both manifests were updated if identity or version changed.
- [ ] NativeAOT publish succeeds for `ClickraShell` and `Clickra.CLI`.
- [ ] The modern Windows 11 submenu appears and enumerates all commands.
- [ ] A command launches with the expected arguments and selection count.
- [ ] Classic Windows 10 behavior was checked when the change affects it.
- [ ] At least one x64 machine was tested; ARM64 coverage is recommended.
- [ ] Diagnostic logging was removed or disabled before shipping.
