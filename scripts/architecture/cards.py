"""Which source files each diagram box stands for (paths under src/, fnmatch patterns; '**' = any depth).
The generator fills the boxes' file lists from here and checks every drawn line against the Roslyn graph:
a line is drawn only when the code really has that reference, and a reference with no line is reported."""
import fnmatch

CARDS = {
    # ---- L1 screens (renderers)
    "ndash": ["Clickra.CLI/Dashboard/*"],
    "nprog": ["Clickra.CLI/Progress/*"],
    "nnone": ["Clickra.CLI/Cli/*"],
    "m_lay": ["Clickra.CLI/UI/**", "Clickra.CLI/Native/*"],
    "fmain": ["Clickra.Fluent/MainPage*", "Clickra.Fluent/DynamicSettingsController.cs", "Clickra.Fluent/OfficeEnginePreflight.cs",
              "Clickra.Fluent/Controls/FluentDialogs.cs"],
    "fprog": ["Clickra.Fluent/TaskProgressPage.xaml.cs", "Clickra.Fluent/Controls/VisualSplitterControl.xaml.cs",
              "Clickra.Fluent/Controls/SplitPagesOverlay.xaml.cs"],
    # ---- L1 shared presentation state and data catalogs
    "m_nav": ["Clickra.Core/Presentation/Dashboard/*"],
    "m_ws": ["Clickra.Core/Presentation/Convert/ConvertWorkspaceController.cs"],
    "m_exec": ["Clickra.Core/Presentation/Convert/ConversionExecutionController.cs"],
    "m_prog": ["Clickra.Core/Presentation/Progress/*"],
    "m_split": ["Clickra.Core/Modules/PdfSplit/Presentation/*"],
    "m_hist": ["Clickra.Core/Modules/HistoryTasks/Presentation/*HistoryPresentationController.cs"],
    "d_feed": ["Clickra.Core/Modules/HistoryTasks/Presentation/HistoryFeed.cs"],
    "m_set": ["Clickra.Core/Modules/Settings/Presentation/*SettingsPresentationController.cs"],
    "d_set": ["Clickra.Core/Modules/Settings/Presentation/SettingDescriptor.cs"],
    # ---- L2 application services (row 1)
    "a_cmd": ["Clickra.Core/Application/ConvertCommandRegistry.cs"],
    "a_uc": ["Clickra.Core/Application/ConversionUseCases.cs", "Clickra.Core/Application/ConversionUseCaseRegistry.cs",
             "Clickra.Core/Application/IConversionUseCase.cs", "Clickra.Core/Application/ConversionRequest.cs",
             "Clickra.Core/Application/ConversionPlan.cs", "Clickra.Core/Application/ConversionResult.cs",
             "Clickra.Core/Application/ConversionInteraction.cs"],
    "a_pages": ["Clickra.Core/Modules/PdfSplit/Application/PdfPageCountQuery.cs"],
    "a_hist": ["Clickra.Core/Modules/HistoryTasks/Application/*"],
    "a_off": ["Clickra.Core/Modules/Settings/Application/LibreOfficeSettingsController.cs"],   # before a_set: first match wins
    "a_set": ["Clickra.Core/Modules/Settings/Application/*"],
    # ---- L2 use cases
    "a_base": ["Clickra.Core/Application/*UseCaseBase.cs"],
    "a_life": ["Clickra.Core/Application/ConversionTask*.cs"],
    "u_Image": ["Clickra.Core/Modules/Image/Application/*"],
    "u_Markdown": ["Clickra.Core/Modules/Markdown/Application/*"],
    "u_Office": ["Clickra.Core/Modules/Office/Application/*"],
    "u_PdfCompression": ["Clickra.Core/Modules/PdfCompression/Application/*"],
    "u_PdfDecrypt": ["Clickra.Core/Modules/PdfDecrypt/Application/*"],
    "u_PdfMerge": ["Clickra.Core/Modules/PdfMerge/Application/*"],
    "u_PdfTranslation": ["Clickra.Core/Modules/PdfTranslation/Application/*"],
    "u_PdfSplit": ["Clickra.Core/Modules/PdfSplit/Application/*"],
    # ---- below L2 (exits)
    "s_shared": ["Shared/*"],
    "s_office": ["Clickra.Core/Modules/Office/Infrastructure/*"],
    "s_store": ["Clickra.Core/Storage/*"],
    "s_proc": ["Clickra.Core/Modules/*/Processing/**", "Clickra.Core/Processors/**", "Clickra.Core/Modules/PdfTranslation/**"],
}
# boxes later in this list do not take files an earlier box already took
ORDER = list(CARDS)
L2_CARDS = [k for k in CARDS if k.startswith(("a_", "u_"))]
L2_SCOPE = ["Clickra.Core/Application/*", "Clickra.Core/Modules/*/Application/*"]   # every file here must sit in an L2 card


def _match(path, pat):
    if "**" in pat:
        return fnmatch.fnmatch(path, pat.replace("/**", "/*")) or fnmatch.fnmatch(path, pat.replace("**", "*"))
    return fnmatch.fnmatch(path, pat) and path.count("/") == pat.count("/")


def assign(paths):
    """file -> box id (first box in ORDER whose pattern matches)."""
    out = {}
    for p in sorted(paths):
        for k in ORDER:
            if any(_match(p, pat) for pat in CARDS[k]):
                out[p] = k
                break
    return out
