using System;
using System.Collections.Generic;
using Clickra.Shared;

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

    private const string ToPdfTitle = "轉成 PDF";

    public static IReadOnlyList<ActionItemViewModel> GetActions()
    {
        return
        [
            CreateAction("compress-pdf", "壓縮", "把檔案壓縮成較小的 PDF", "壓縮 PDF", CompressGeometry),
            CreateAction("merge-pdf", "合併", "把這些檔案合併成一份 PDF", "合併 PDF", MergeGeometry),
            CreateAction("split-pdf", "分割", "把每個檔案拆成多份 PDF", "分割 PDF", SplitGeometry),
            CreateAction("pdf2img", "轉成圖片", "把每一頁轉成一張圖片", "PDF → 圖片", ImgGeometry),
            CreateAction("translate-pdf", "翻譯", "把內容翻譯成其他語言", "翻譯 PDF", TranslateGeometry),
            CreateAction("decrypt-pdf", "解密", "把檔案的密碼移除", "解密 PDF", DecryptGeometry),
            CreateAction("print-pdf", "列印", "把檔案送到印表機列印", "列印 PDF", PrintGeometry),
            CreateAction("word2pdf", ToPdfTitle, "把文件轉成 PDF", "Word → PDF", DocGeometry),
            CreateAction("excel2pdf", ToPdfTitle, "把試算表轉成 PDF", "Excel → PDF", DocGeometry),
            CreateAction("ppt2pdf", ToPdfTitle, "把簡報轉成 PDF", "PPT → PDF", DocGeometry),
            CreateAction("md2pdf", ToPdfTitle, "把 Markdown 轉成 PDF", "MD → PDF", DocGeometry),
            CreateAction("md2word", "轉成 Word", "把 Markdown 轉成 Word", "MD → Word", DocGeometry),
            CreateAction("img2pdf", "分別轉成 PDF", "把每張圖片各轉成一個 PDF", "圖片 → PDF", DocGeometry),
            CreateAction("img-merge", "合成一份 PDF", "把這些圖片合成一份 PDF", "合併圖片", MergeGeometry),
            CreateAction("img-stitch", "拼成長圖", "把圖片上下接成一張長圖", "拼接圖片", ImgGeometry),
            CreateAction("img-compress", "壓縮圖片", "把圖片壓縮成較小的檔案", "壓縮圖片", CompressGeometry),
            CreateAction("img-to-png", "轉換格式", "把圖片轉成其他格式", "轉換圖片格式", ImgGeometry),
            CreateAction("batch-rename", "重新命名", "把檔案改成新的名稱", "批次重新命名", RenameGeometry),
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
