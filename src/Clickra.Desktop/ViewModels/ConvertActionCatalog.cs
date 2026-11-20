using System;
using System.Collections.Generic;
using Clickra.Shared;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

public static class ConvertActionCatalog
{
    private const string DocGeometry = "M6,2 H14 L18,6 V22 H6 Z M14,2 V6 H18";
    private const string CompressGeometry = "M4,12 H10 M7,9 L10,12 L7,15 M20,12 H14 M17,9 L14,12 L17,15";
    private const string MergeGeometry = "M4,7 H14 M10,3 L14,7 L10,11 M4,17 H20 M16,13 L20,17 L16,21";
    private const string SplitGeometry = "M6,6 L18,18 M6,18 L18,6";
    private const string ImgGeometry = "M3,4 H21 V20 H3 Z M3,16 L8,11 L14,17 L17,14 L21,18";
    private const string TranslateGeometry = "M4,6 H14 M9,4 V6 M6,6 C6,10 9,14 13,16 M9,11 C7,13 5,14 3,15 M15,10 L19,20 M17,16 H21";
    private const string DecryptGeometry = "M7,11 V7 A5,5 0 0 1 17,7 V11 M5,11 H19 V21 H5 Z";
    private const string PrintGeometry = "M6,9 V3 H18 V9 M6,18 H18 V21 H6 Z M4,9 H20 V17 H4 Z";
    private const string RenameGeometry = "M3,17.25 V21 H6.75 L17.81,9.94 L14.06,6.19 Z";

    private static string ToPdfTitle => Strings.T("ui_catalog_convert_to_pdf");

    public static IReadOnlyList<ActionItemViewModel> GetActions()
    {
        return
        [
            CreateAction("compress-pdf", Strings.T("ui_catalog_compress"), Strings.T("ui_catalog_make_pdfs_smaller"), Strings.T("ui_feature_compress_pdf"), CompressGeometry),
            CreateAction("merge-pdf", Strings.T("ui_catalog_merge"), Strings.T("ui_catalog_combine_these_files_into_one"), Strings.T("ui_feature_merge_pdf"), MergeGeometry),
            CreateAction("split-pdf", Strings.T("ui_catalog_split"), Strings.T("ui_catalog_split_each_file_into_several"), Strings.T("ui_feature_split_pdf"), SplitGeometry),
            CreateAction("pdf2img", Strings.T("ui_catalog_convert_to_images"), Strings.T("ui_catalog_turn_each_page_into_an"), Strings.T("ui_feature_pdf_to_images"), ImgGeometry),
            CreateAction("translate-pdf", Strings.T("ui_catalog_translate"), Strings.T("ui_catalog_translate_the_text_into_another"), Strings.T("ui_feature_translate_pdf"), TranslateGeometry),
            CreateAction("decrypt-pdf", Strings.T("ui_catalog_decrypt"), Strings.T("ui_catalog_remove_the_password_or_usage"), Strings.T("ui_feature_decrypt_pdf"), DecryptGeometry),
            CreateAction("print-pdf", Strings.T("ui_catalog_print"), Strings.T("ui_catalog_send_files_to_a_printer"), Strings.T("ui_feature_print_pdf"), PrintGeometry),
            CreateAction("word2pdf", ToPdfTitle, Strings.T("ui_catalog_turn_documents_into_pdfs"), "Word → PDF", DocGeometry),
            CreateAction("excel2pdf", ToPdfTitle, Strings.T("ui_catalog_turn_spreadsheets_into_pdfs"), "Excel → PDF", DocGeometry),
            CreateAction("ppt2pdf", ToPdfTitle, Strings.T("ui_catalog_turn_presentations_into_pdfs"), "PPT → PDF", DocGeometry),
            CreateAction("md2pdf", ToPdfTitle, Strings.T("ui_catalog_turn_markdown_into_pdf"), "MD → PDF", DocGeometry),
            CreateAction("md2word", Strings.T("ui_catalog_convert_to_word"), Strings.T("ui_catalog_turn_markdown_into_word"), "MD → Word", DocGeometry),
            CreateAction("img2pdf", Strings.T("ui_catalog_convert_each_to_pdf"), Strings.T("ui_catalog_turn_each_image_into_its"), Strings.T("ui_feature_images_to_pdf"), DocGeometry),
            CreateAction("img-merge", Strings.T("ui_catalog_combine_into_one_pdf"), Strings.T("ui_catalog_put_these_images_into_one"), Strings.T("ui_feature_merge_images"), MergeGeometry),
            CreateAction("img-stitch", Strings.T("ui_catalog_stitch_into_a_long_image"), Strings.T("ui_catalog_join_images_top_to_bottom"), Strings.T("ui_feature_stitch_images"), ImgGeometry),
            CreateAction("img-compress", Strings.T("ui_feature_compress_images"), Strings.T("ui_catalog_make_images_smaller"), Strings.T("ui_feature_compress_images"), CompressGeometry),
            CreateAction("img-to-png", Strings.T("ui_catalog_change_format"), Strings.T("ui_catalog_convert_images_to_another_format"), Strings.T("ui_feature_convert_image_format"), ImgGeometry),
            CreateAction("batch-rename", Strings.T("ui_catalog_rename"), Strings.T("ui_catalog_give_files_new_names"), Strings.T("ui_feature_batch_rename"), RenameGeometry),
        ];
    }

    private static ActionItemViewModel CreateAction(
        string command,
        string name,
        string toolTip,
        string fullFeatureName,
        string geometry)
    {
        string fileType = ConvertCommandMetadata.GetFileTypeForCommand(command);
        string color = fileType switch
        {
            "word" => "#0078D4",
            "excel" => "#107C41",
            "ppt" => "#D83B01",
            "markdown" => "#6048A4",
            "image" => "#00828C",
            "pdf" => "#475569",
            _ => "#5C5C5C"
        };

        return new ActionItemViewModel(
            command,
            name,
            toolTip,
            fullFeatureName,
            fileType,
            color,
            geometry);
    }
}
