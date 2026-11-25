using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Modules.Img.Compression.Application;
using Clickra.Core.Modules.Pdf.Compression.Application;
using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Core.Modules.Pdf.Print.Ports;
using Clickra.Core.Modules.Pdf.ToImage.Application;
using Clickra.Core.Modules.Pdf.Translation.Application;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

/// <summary>
/// The options card of an action page (spec 4.9). Every value starts from the feature default in
/// Settings and can be changed for this run only; 「設為預設值」 appears when the card differs from the
/// default and saves it. Builds the option dictionary Core expects for the selected command.
/// </summary>
public sealed class ActionOptionsViewModel : ViewModelBase
{
    private const string ImageFormatPrefix = "img-to-";

    private string _command = "";

    // PDF compression (3 steps) and image compression (4 steps).
    private int _pdfLevel, _imageLevel;
    // PDF → images.
    private int _pdfImageFormat, _pdfImageDpi = Array.IndexOf(DpiValues, PdfToImageOptions.DefaultDpi);
    private string _pdfImagePages = "";
    private bool _pdfImageStitch;
    // Translation.
    private int _translateLanguage, _translateEngine;
    // Decrypt.
    private string _password = "";
    // Image format (required, no default).
    private int _imageFormat = -1;
    // Markdown.
    private int _markdownTheme, _markdownPaper, _markdownTextSize = 1, _markdownCode;

    public static readonly string[] PdfLevelNames = [Strings.T("ui_pdf_compress_small_file"), Strings.T("ui_pdf_compress_balanced"), Strings.T("ui_pdf_compress_high_quality")];
    public static readonly string[] PdfLevelHints = [Strings.T("ui_pdf_compress_smallest_file_images_lose_quality"), Strings.T("ui_pdf_compress_good_for_sharing"), Strings.T("ui_pdf_compress_images_look_almost_the_same")];
    public static readonly string[] ImageLevelNames = [Strings.T("ui_img_compress_smallest"), Strings.T("ui_pdf_compress_small_file"), Strings.T("ui_img_compress_standard"), Strings.T("ui_pdf_compress_high_quality")];
    public static readonly int[] DpiValues = [72, 96, 150, 200, 300, 600];
    public static readonly string[] DpiLabels = [Strings.T("ui_pdf2img_72_screen"), "96", Strings.T("ui_pdf2img_150_recommended"), "200", Strings.T("ui_pdf2img_300_print"), "600"];
    private static readonly string[] PdfImageFormats = ["png", "jpg", "webp"];
    public static readonly string[] TranslateLanguageCodes = ["zh-TW", "zh-CN", "en", "ja", "ko"];
    public static readonly string[] TranslateLanguageNames = [Strings.T("ui_lang_zh_tw"), Strings.T("ui_lang_zh_cn"), "English", Strings.T("ui_lang_ja"), Strings.T("ui_lang_ko")];
    private static readonly string[] TranslateEngines = ["google", "custom-ai"];
    public static readonly string[] ImageFormats = ["PNG", "JPG", "WebP", "GIF", "HEIC"];
    private static readonly string[] MarkdownThemes = [MarkdownPdfOptions.ThemeDefault, MarkdownPdfOptions.ThemeMinimal, MarkdownPdfOptions.ThemeAcademic,
        Clickra.Core.Storage.ClickraSettings.MarkdownThemeWordTemplate];
    private static readonly string[] MarkdownPapers = [MarkdownPdfOptions.PaperA4, MarkdownPdfOptions.PaperLetter];
    private static readonly string[] MarkdownTextSizes = [MarkdownPdfOptions.TextSmall, MarkdownPdfOptions.TextStandard, MarkdownPdfOptions.TextLarge];
    private static readonly string[] MarkdownCodeThemes = [MarkdownPdfOptions.CodeDark, MarkdownPdfOptions.CodeLight];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Required for XAML compiled binding")]
    public IReadOnlyList<string> PdfLevelSteps => PdfLevelNames;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Required for XAML compiled binding")]
    public IReadOnlyList<string> ImageLevelSteps => ImageLevelNames;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Required for XAML compiled binding")]
    public IReadOnlyList<string> DpiChoices => DpiLabels;
    public static IReadOnlyList<string> DpiLabelsPublic => DpiLabels;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Required for XAML compiled binding")]
    public IReadOnlyList<string> TranslateChoices => TranslateLanguageNames;

    /// <summary>Called when an action is opened: loads the feature defaults for it.</summary>
    public void Reset(string command)
    {
        _command = command;
        _justSavedDefault = false;
        _pdfLevel = Math.Clamp(SettingsController.PdfCompressionImageLevel, 0, 2);
        _imageLevel = Math.Clamp(SettingsController.ImageCompressionLevel, 0, 3);
        _pdfImageFormat = Math.Max(0, Array.IndexOf(PdfImageFormats, SettingsController.PdfToImageFormat));
        _pdfImageDpi = DpiIndexOf(SettingsController.PdfToImageDpi);
        _pdfImagePages = "";
        _pdfImageStitch = SettingsController.PdfToImageStitch;
        _imageMaxDim = MaxDimIndexOf(SettingsController.ImageCompressionMaxDimension);
        _translateLanguage = Math.Max(0, Array.FindIndex(TranslateLanguageCodes,
            c => c.Equals(SettingsController.TranslationTargetLanguage, StringComparison.OrdinalIgnoreCase)));
        _translateEngine = Math.Max(0, Array.IndexOf(TranslateEngines, SettingsController.TranslateEngine));
        _aiEndpoint = SettingsController.TranslateAiEndpoint;
        _aiModel = SettingsController.TranslateAiModel;
        _aiApiKey = SettingsController.TranslateAiApiKey;
        _aiEditing = !AiSaved;
        _rememberAi = true;
        _password = "";
        _imageFormat = -1;
        _markdownTheme = Math.Max(0, Array.IndexOf(MarkdownThemes, SettingsController.MarkdownTheme));
        SetTemplate(_markdownTheme == WordTemplateIndex ? SettingsController.MarkdownTemplatePath : "");
        if (_markdownTheme == WordTemplateIndex && _templatePath.Length == 0) _markdownTheme = 0;
        _markdownPaper = Math.Max(0, Array.IndexOf(MarkdownPapers, SettingsController.MarkdownPaper));
        _markdownTextSize = Math.Max(0, Array.IndexOf(MarkdownTextSizes, SettingsController.MarkdownTextSize));
        _markdownCode = SettingsController.MarkdownCodeTheme == MarkdownPdfOptions.CodeLight ? 1 : 0;
        if (command == "print-pdf") LoadPrinters();
        OnPropertyChanged(string.Empty); // every property may have changed
    }

    /// <summary>Selects the image target format from a command such as img-to-jpg (再轉一次, 繼續).</summary>
    public void SetImageFormatFromCommand(string command)
    {
        if (!command.StartsWith(ImageFormatPrefix, StringComparison.OrdinalIgnoreCase)) return;
        string format = command[ImageFormatPrefix.Length..];
        ImageFormatIndex = Array.FindIndex(ImageFormats, f => f.Equals(format, StringComparison.OrdinalIgnoreCase));
    }

    // ----- which card -----
    public bool IsCompressPdf => _command == "compress-pdf";
    public bool IsPdfToImage => _command == "pdf2img";
    public bool IsTranslate => _command == "translate-pdf";
    public bool IsDecrypt => _command == "decrypt-pdf";
    public bool IsImageCompress => _command == "img-compress";
    public bool IsImageFormat => _command.StartsWith(ImageFormatPrefix, StringComparison.OrdinalIgnoreCase);
    public bool IsMarkdown => _command is "md2pdf" or "md2word";
    public bool IsMarkdownPdf => _command == "md2pdf";
    public bool IsPrint => _command == "print-pdf";
    public bool IsOffice => _command is "word2pdf" or "excel2pdf" or "ppt2pdf" or "img2pdf";


    // ----- 設為預設值 (spec 4.9): one button at the right of the action's title row, for the whole card -----
    public ActionOptionsViewModel()
    {
        // Every *IsDefault change also changes what the title-row button shows.
        PropertyChanged += (_, e) =>
        {
            string name = e.PropertyName ?? "";
            if (name.Length == 0 || (name.EndsWith("IsDefault", StringComparison.Ordinal) && name != nameof(AllAtDefault))
                || name is nameof(CanSaveMarkdownDefault) or nameof(JustSavedDefault))
            {
                OnPropertyChanged(nameof(AllAtDefault));
                OnPropertyChanged(nameof(ShowSaveDefault));
                OnPropertyChanged(nameof(ShowSavedNote));
            }
        };
    }

    /// <summary>Actions whose card has values with a feature default (decrypt and image format have none).</summary>
    public bool HasDefaults => IsCompressPdf || IsImageCompress || IsPdfToImage || IsTranslate || IsMarkdown || IsPrint;
    public bool AllAtDefault => GetAllAtDefault();

    private bool GetAllAtDefault()
    {
        if (IsCompressPdf) return PdfLevelIsDefault;
        if (IsImageCompress) return ImageLevelIsDefault;
        if (IsPdfToImage) return PdfImageIsDefault;
        if (IsTranslate) return TranslateIsDefault;
        if (IsMarkdown) return MarkdownThemeIsDefault;
        if (IsPrint) return PrintIsDefault;
        return true;
    }

    public bool ShowSaveDefault => HasDefaults && (IsMarkdown ? CanSaveMarkdownDefault : !AllAtDefault) && !(IsPrint && !HasPrinters);
    public bool ShowSavedNote => HasDefaults && _justSavedDefault && AllAtDefault;

    public void SaveDefault()
    {
        if (IsCompressPdf) SavePdfDefault();
        else if (IsImageCompress) SaveImageDefault();
        else if (IsPdfToImage) SavePdfImageDefault();
        else if (IsTranslate) SaveTranslateDefault();
        else if (IsMarkdown) SaveMarkdownDefault();
        else if (IsPrint) SavePrintDefault();
    }

    /// <summary>設為預設值 was just pressed: the button gives way to 「已設為預設值」 until another action is opened.</summary>
    private bool _justSavedDefault;
    public bool JustSavedDefault => _justSavedDefault;
    private void MarkSaved()
    {
        _justSavedDefault = true;
        OnPropertyChanged(nameof(JustSavedDefault));
    }

    // ----- PDF compression -----
    public int PdfLevel
    {
        get => _pdfLevel;
        set
        {
            if (value is < 0 or > 2 || value == _pdfLevel) return;
            _pdfLevel = value;
            Changed(nameof(PdfLevel), nameof(PdfLevelHint), nameof(PdfLevelIsDefault));
        }
    }
    public string PdfLevelHint => $"{PdfLevelNames[_pdfLevel]}\u3000{PdfLevelHints[_pdfLevel]}";
    public bool PdfLevelIsDefault =>
        _pdfLevel == Math.Clamp(SettingsController.PdfCompressionImageLevel, 0, 2);
    public void SavePdfDefault()
    {
        MarkSaved();
        SettingsController.PdfCompressionImageLevel = _pdfLevel;
        OnPropertyChanged(nameof(PdfLevelIsDefault));
    }

    // ----- image compression -----
    public int ImageLevel
    {
        get => _imageLevel;
        set
        {
            if (value is < 0 or > 3 || value == _imageLevel) return;
            _imageLevel = value;
            Changed(nameof(ImageLevel), nameof(ImageLevelIsDefault));
        }
    }
    public bool ImageLevelIsDefault =>
        _imageLevel == Math.Clamp(SettingsController.ImageCompressionLevel, 0, 3) &&
        MaxDimValues[_imageMaxDim] == SettingsController.ImageCompressionMaxDimension;
    public void SaveImageDefault()
    {
        MarkSaved();
        SettingsController.ImageCompressionLevel = _imageLevel;
        SettingsController.ImageCompressionMaxDimension = MaxDimValues[_imageMaxDim];
        OnPropertyChanged(nameof(ImageLevelIsDefault));
    }

    /// <summary>最大邊長: 0 keeps the size; smaller images are never enlarged.</summary>
    public static readonly int[] MaxDimValues = [0, 3840, 2560, 1920, 1280];
    public static readonly string[] MaxDimLabels = [Strings.T("ui_img_compress_keep_size"), Strings.T("ui_img_compress_3840_px_4k"), "2560 px", Strings.T("ui_img_compress_1920_px_full_hd"), "1280 px"];
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Required for XAML compiled binding")]
    public IReadOnlyList<string> MaxDimChoices => MaxDimLabels;
    private int _imageMaxDim;
    public int ImageMaxDimIndex
    {
        get => _imageMaxDim;
        set { _imageMaxDim = Math.Clamp(value, 0, MaxDimValues.Length - 1); Changed(nameof(ImageMaxDimIndex), nameof(ImageLevelIsDefault)); }
    }
    private static int MaxDimIndexOf(int value)
    {
        int i = Array.IndexOf(MaxDimValues, value);
        return i >= 0 ? i : 0;
    }

    // ----- PDF → images -----
    public int PdfImageFormatIndex { get => _pdfImageFormat; set { _pdfImageFormat = Math.Clamp(value, 0, 2); Changed(nameof(PdfImageFormatIndex), nameof(PdfImageIsDefault)); } }
    /// <summary>Everything but the page range (that belongs to this file) has a feature default.</summary>
    public bool PdfImageIsDefault =>
        PdfImageFormats[_pdfImageFormat] == SettingsController.PdfToImageFormat &&
        DpiValues[_pdfImageDpi] == SettingsController.PdfToImageDpi &&
        _pdfImageStitch == SettingsController.PdfToImageStitch;
    public void SavePdfImageDefault()
    {
        MarkSaved();
        SettingsController.PdfToImageFormat = PdfImageFormats[_pdfImageFormat];
        SettingsController.PdfToImageDpi = DpiValues[_pdfImageDpi];
        SettingsController.PdfToImageStitch = _pdfImageStitch;
        OnPropertyChanged(nameof(PdfImageIsDefault));
    }
    public int PdfImageDpiIndex { get => _pdfImageDpi; set { _pdfImageDpi = Math.Clamp(value, 0, DpiValues.Length - 1); Changed(nameof(PdfImageDpiIndex), nameof(PdfImageIsDefault)); } }
    public string PdfImagePages { get => _pdfImagePages; set { _pdfImagePages = value ?? ""; OnPropertyChanged(); } }
    public bool PdfImageStitch { get => _pdfImageStitch; set { _pdfImageStitch = value; Changed(nameof(PdfImageStitch), nameof(PdfImageIsDefault)); } }
    private static int DpiIndexOf(int dpi)
    {
        int i = Array.IndexOf(DpiValues, dpi);
        return i >= 0 ? i : Array.IndexOf(DpiValues, PdfToImageOptions.DefaultDpi);
    }

    // ----- translation -----
    public int TranslateLanguageIndex
    {
        get => _translateLanguage;
        set
        {
            if (value < 0 || value >= TranslateLanguageCodes.Length) return;
            _translateLanguage = value;
            Changed(nameof(TranslateLanguageIndex), nameof(TranslateIsDefault));
        }
    }
    public int TranslateEngineIndex
    {
        get => _translateEngine;
        set
        {
            _translateEngine = Math.Clamp(value, 0, 1);
            Changed(nameof(TranslateEngineIndex), nameof(TranslateIsDefault), nameof(TranslateServiceName), nameof(TranslateOnlineNotice), nameof(IsCustomAi),
                nameof(ShowAiSummary), nameof(ShowAiFields), nameof(AiSummaryHost), nameof(IsReady), nameof(BlockingText));
        }
    }
    /// <summary>Who receives the content, by name: Google 翻譯, or the custom service's host (api.openai.com).</summary>
    public string TranslateServiceName
    {
        get
        {
            if (_translateEngine != 1) return Strings.T("ui_translate_google_translate");
            return Uri.TryCreate(_aiEndpoint.Trim(), UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0 ? uri.Host : Strings.T("ui_translate_custom_ai_service_2");
        }
    }

    // 自訂 AI 服務 (spec 4.9, canvas 「翻譯的自訂 AI 服務要在哪裡填」 B): once a service is remembered the card shows
    // one line with 變更; the first time, or after 變更, the address, model and key are filled in right here with
    // 「記住這個服務」. Remembering a service is separate from 設為預設值, which only picks the default translator.
    private string _aiEndpoint = "", _aiModel = "", _aiApiKey = "";
    private bool _aiEditing, _rememberAi = true;
    public bool IsCustomAi => _translateEngine == 1;
    private static bool AiSaved =>
        SettingsController.TranslateAiEndpoint.Trim().Length > 0 && SettingsController.TranslateAiModel.Trim().Length > 0
        && SettingsController.TranslateAiApiKey.Trim().Length > 0;
    public bool ShowAiSummary => IsCustomAi && !_aiEditing;
    public bool ShowAiFields => IsCustomAi && _aiEditing;
    public string AiSummaryModel => _aiModel.Trim();
    /// <summary>「翻譯需要連網，檔案內容會送到 Google 翻譯」 under the service choice.</summary>
    public string TranslateOnlineNotice => Strings.F("ui_translate_online_notice", ("service", TranslateServiceName));
    public string AiSummaryHost => Strings.F("ui_translate_summary_host", ("name", TranslateServiceName));
    public bool RememberAi { get => _rememberAi; set { _rememberAi = value; OnPropertyChanged(); } }
    public void EditAi()
    {
        _aiEditing = true;
        Changed(nameof(ShowAiSummary), nameof(ShowAiFields));
    }
    public string AiEndpoint { get => _aiEndpoint; set { _aiEndpoint = value ?? ""; Changed(nameof(AiEndpoint), nameof(TranslateServiceName), nameof(TranslateOnlineNotice), nameof(AiSummaryHost), nameof(IsReady), nameof(BlockingText)); } }
    public string AiModel { get => _aiModel; set { _aiModel = value ?? ""; Changed(nameof(AiModel), nameof(AiSummaryModel), nameof(IsReady), nameof(BlockingText)); } }
    public string AiApiKey { get => _aiApiKey; set { _aiApiKey = value ?? ""; Changed(nameof(AiApiKey), nameof(IsReady), nameof(BlockingText)); } }
    private bool AiIncomplete => IsCustomAi && (_aiEndpoint.Trim().Length == 0 || _aiModel.Trim().Length == 0 || _aiApiKey.Trim().Length == 0);
    public bool TranslateIsDefault =>
        TranslateLanguageCodes[_translateLanguage].Equals(SettingsController.TranslationTargetLanguage, StringComparison.OrdinalIgnoreCase) &&
        TranslateEngines[_translateEngine] == SettingsController.TranslateEngine;
    public void SaveTranslateDefault()
    {
        MarkSaved();
        SettingsController.TranslationTargetLanguage = TranslateLanguageCodes[_translateLanguage];
        SettingsController.TranslateEngine = TranslateEngines[_translateEngine];
        OnPropertyChanged(nameof(TranslateIsDefault));
    }

    // ----- decrypt -----
    public string Password { get => _password; set { _password = value ?? ""; Changed(nameof(Password), nameof(IsReady), nameof(BlockingText)); } }

    private bool _decryptNeedsPassword = true;
    /// <summary>False when every protected file only restricts printing or copying: they open without a password,
    /// so the card asks for none (spec 4.9).</summary>
    public bool DecryptNeedsPassword
    {
        get => _decryptNeedsPassword;
        set { _decryptNeedsPassword = value; Changed(nameof(DecryptNeedsPassword), nameof(DecryptNoPassword), nameof(IsReady), nameof(BlockingText)); }
    }
    public bool DecryptNoPassword => !_decryptNeedsPassword;

    private bool _decryptSingleFile = true;
    public bool DecryptSingleFile
    {
        get => _decryptSingleFile;
        set { _decryptSingleFile = value; Changed(nameof(DecryptSingleFile), nameof(DecryptNoPasswordText)); }
    }
    /// <summary>Restriction-only PDFs: what they are and what decrypting gives, in plain words (spec 4.9).</summary>
    public string DecryptNoPasswordText => _decryptSingleFile
        ? Strings.T("ui_decrypt_this_pdf_only_has_usage")
        : Strings.T("ui_decrypt_these_pdfs_only_have_usage");

    // ----- image format -----
    private bool[] _imageFormatAllowed = [true, true, true, true, true];
    /// <summary>Formats the selected files can be converted to (a PNG cannot become PNG); others are disabled.</summary>
    public IReadOnlyList<bool> ImageFormatAllowed => _imageFormatAllowed;
    public void SetAllowedImageFormats(Func<string, bool> isCompatible)
    {
        _imageFormatAllowed = ImageFormats.Select(f => isCompatible(ImageFormatPrefix + f.ToLowerInvariant())).ToArray();
        OnPropertyChanged(nameof(ImageFormatAllowed));
    }

    public int ImageFormatIndex
    {
        get => _imageFormat;
        set
        {
            _imageFormat = value is >= 0 and < 5 ? value : -1;
            Changed(nameof(ImageFormatIndex), nameof(IsReady), nameof(BlockingText));
        }
    }

    // ----- Markdown -----
    public const int WordTemplateIndex = 3;

    /// <summary>The four template cards (spec 4.9 預覽卡片): a page preview each, then 「Word 範本」.</summary>
    public IReadOnlyList<TemplateCardViewModel> TemplateCards { get; } =
    [
        new(Strings.T("ui_markdown_default"), MarkdownPdfOptions.ThemeDefault),
        new(Strings.T("ui_markdown_minimal"), MarkdownPdfOptions.ThemeMinimal),
        new(Strings.T("ui_markdown_academic"), MarkdownPdfOptions.ThemeAcademic),
        new(Strings.T("ui_markdown_word_template"), null),
    ];
    private string _templatePath = "";
    private string _templateSummary = "";
    private string _templateError = "";

    /// <summary>Asks the page to show a .docx picker (選擇 Word 範本); the page calls SetTemplate or CancelTemplatePick.</summary>
    public event EventHandler? TemplatePickRequested;

    public int MarkdownThemeIndex
    {
        get => _markdownTheme;
        set
        {
            int index = Math.Clamp(value, 0, WordTemplateIndex);
            if (index == WordTemplateIndex && _templatePath.Length == 0)
            {
                // Spec 4.9: choosing 「Word 範本」 opens the picker first; cancelling keeps the previous template.
                TemplatePickRequested?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged();
                return;
            }
            _markdownTheme = index;
            Changed(nameof(MarkdownThemeIndex), nameof(UsesWordTemplate), nameof(IsReady), nameof(BlockingText),
                nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons), nameof(ShowTemplateSummary));
        }
    }
    public bool UsesWordTemplate => _markdownTheme == WordTemplateIndex;
    public string TemplateFileName => System.IO.Path.GetFileName(_templatePath);
    /// <summary>What was read from the template: fonts, size, line height, margins, centred title.</summary>
    public string TemplateSummary => _templateSummary;
    public string TemplateError => _templateError;
    public bool HasTemplateError => _templateError.Length > 0;

    public void RequestTemplatePick() => TemplatePickRequested?.Invoke(this, EventArgs.Empty);

    public void SetTemplate(string path)
    {
        _templatePath = path ?? "";
        bool centerTitle = false;
        double lineMultiple = 1.15, marginCm = 2.54;
        if (_templatePath.Length > 0)
        {
            LoadTemplateSummary(_templatePath, TemplateFileName, out _templateSummary, out _templateError,
                out centerTitle, out lineMultiple, out marginCm);
            _markdownTheme = WordTemplateIndex;
        }
        else
        {
            _templateSummary = "";
            _templateError = "";
        }
        Changed(nameof(MarkdownThemeIndex), nameof(UsesWordTemplate), nameof(TemplateFileName), nameof(TemplateSummary),
            nameof(TemplateError), nameof(HasTemplateError), nameof(IsReady), nameof(BlockingText),
            nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons), nameof(ShowTemplateSummary));
        TemplateCards[WordTemplateIndex].SetWordTemplate(_templatePath.Length == 0 ? null : TemplateFileName, !HasTemplateError,
            centerTitle, lineMultiple, marginCm);
    }

    private static void LoadTemplateSummary(
        string path,
        string fileName,
        out string summary,
        out string error,
        out bool centerTitle,
        out double lineMultiple,
        out double marginCm)
    {
        summary = "";
        error = "";
        centerTitle = false;
        lineMultiple = 1.15;
        marginCm = 2.54;

        try
        {
            if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException();
            var template = MarkdownTemplateSource.Load(path);
            var t = template.Typography;
            var l = template.Layout;
            string fonts = t.CjkFont.Equals(t.LatinFont, StringComparison.OrdinalIgnoreCase) ? t.CjkFont : Strings.F("ui_markdown_font_pair", ("cjk", t.CjkFont), ("latin", t.LatinFont));
            lineMultiple = t.BodySizePoints > 0 ? t.LineHeightPoints / t.BodySizePoints : 1.15;
            marginCm = l.MarginPoints / 28.3465;
            centerTitle = l.CenterH1;
            // Canvas MarkdownWordB: one line of what was read.
            summary = Strings.F(centerTitle ? "ui_markdown_template_summary_centered" : "ui_markdown_template_summary",
                ("fonts", fonts),
                ("size", t.BodySizePoints.ToString("0.#", Strings.Culture)),
                ("spacing", lineMultiple.ToString("0.##", Strings.Culture)),
                ("margin", marginCm.ToString("0.##", Strings.Culture)));
        }
        catch (System.IO.FileNotFoundException)
        {
            error = Strings.F("ui_markdown_template_missing", ("name", fileName));
        }
        catch (System.IO.InvalidDataException ex) when (ex.Message.Contains("East Asian", StringComparison.OrdinalIgnoreCase))
        {
            // Core needs the template to name a Chinese/Japanese/Korean font (English Word files often don't).
            error = Strings.F("ui_markdown_template_no_cjk_font", ("name", fileName));
        }
        catch (Exception) // skipcq: CS-R1008
        {
            error = Strings.F("ui_markdown_template_unusable", ("name", fileName));
        }
    }

    public void CancelTemplatePick() => OnPropertyChanged(nameof(MarkdownThemeIndex));

    /// <summary>Everything on the card that has a feature default: template, paper (PDF only) and text size.</summary>
    public bool MarkdownThemeIsDefault =>
        MarkdownThemes[_markdownTheme] == SettingsController.MarkdownTheme &&
        (!UsesWordTemplate || string.Equals(_templatePath, SettingsController.MarkdownTemplatePath, StringComparison.OrdinalIgnoreCase)) &&
        (!IsMarkdownPdf || MarkdownPapers[_markdownPaper] == SettingsController.MarkdownPaper) &&
        MarkdownTextSizes[_markdownTextSize] == SettingsController.MarkdownTextSize &&
        MarkdownCodeThemes[_markdownCode] == SettingsController.MarkdownCodeTheme;

    public void SaveMarkdownDefault()
    {
        MarkSaved();
        SettingsController.MarkdownTheme = MarkdownThemes[_markdownTheme];
        SettingsController.MarkdownTemplatePath = UsesWordTemplate ? _templatePath : "";
        if (IsMarkdownPdf) SettingsController.MarkdownPaper = MarkdownPapers[_markdownPaper];
        SettingsController.MarkdownTextSize = MarkdownTextSizes[_markdownTextSize];
        SettingsController.MarkdownCodeTheme = MarkdownCodeThemes[_markdownCode];
        Changed(nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons));
    }

    /// <summary>設為預設值 under the cards: only when the choice differs from the default and is usable.</summary>
    public bool CanSaveMarkdownDefault => !MarkdownThemeIsDefault && !(UsesWordTemplate && HasTemplateError);
    public bool ShowTemplateSummary => UsesWordTemplate && !HasTemplateError;
    public bool ShowTemplateButtons => UsesWordTemplate || CanSaveMarkdownDefault;
    public int MarkdownPaperIndex { get => _markdownPaper; set { _markdownPaper = Math.Clamp(value, 0, 1); Changed(nameof(MarkdownPaperIndex), nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons)); } }
    public int MarkdownTextSizeIndex { get => _markdownTextSize; set { _markdownTextSize = Math.Clamp(value, 0, 2); Changed(nameof(MarkdownTextSizeIndex), nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons)); } }
    public int MarkdownCodeIndex { get => _markdownCode; set { _markdownCode = Math.Clamp(value, 0, 1); Changed(nameof(MarkdownCodeIndex), nameof(MarkdownThemeIsDefault), nameof(CanSaveMarkdownDefault), nameof(ShowTemplateButtons)); } }
    // ----- print (spec 4.9: printer and copies on one row, the rest side by side below) -----
    private IReadOnlyList<string> _printers = Array.Empty<string>();
    private int _printerIndex = -1;
    private int _copies = 1;
    private string _printPages = "";
    private int _duplex, _orientation;
    private bool _darkenText;
    private IReadOnlyList<string> _paperSizes = [Strings.T("ui_print_printer_setting")];
    private int _paperIndex;

    private bool _isLoadingPrinters;
    public bool IsLoadingPrinters
    {
        get => _isLoadingPrinters;
        private set
        {
            if (_isLoadingPrinters != value)
            {
                _isLoadingPrinters = value;
                Changed(nameof(IsLoadingPrinters), nameof(HasPrinters), nameof(IsReady), nameof(BlockingText), nameof(PrintIsDefault));
            }
        }
    }

    public IReadOnlyList<string> Printers => _printers;
    public bool HasPrinters => _isLoadingPrinters || _printers.Count > 0;
    public int PrinterIndex
    {
        get => _printerIndex;
        set
        {
            _printerIndex = value >= 0 && value < _printers.Count ? value : -1;
            CanDuplex = _printerIndex >= 0 && PrinterDiscovery.CanDuplex(_printers[_printerIndex]);
            if (!CanDuplex)
            {
                _duplex = 0;
            }
            Changed(nameof(PrinterIndex), nameof(CanDuplex), nameof(DuplexIndex), nameof(IsReady), nameof(BlockingText), nameof(PrintIsDefault));
            LoadPaperSizes();
        }
    }
    public decimal Copies
    {
        get => _copies;
        set
        {
            _copies = (int)Math.Clamp(value, 1, 99);
            Changed(nameof(Copies), nameof(CanCollate), nameof(PrintIsDefault));
        }
    }
    public string PrintPages { get => _printPages; set { _printPages = value ?? ""; OnPropertyChanged(); } }
    /// <summary>0 單面, 1 雙面（長邊翻頁）, 2 雙面（短邊翻頁）.</summary>
    public int DuplexIndex { get => _duplex; set { _duplex = Math.Clamp(value, 0, 2); Changed(nameof(DuplexIndex), nameof(PrintIsDefault)); } }
    public bool CanDuplex { get; private set; } = true;
    /// <summary>0 自動, 1 直向, 2 橫向.</summary>
    public int OrientationIndex { get => _orientation; set { _orientation = Math.Clamp(value, 0, 2); Changed(nameof(OrientationIndex), nameof(PrintIsDefault)); } }
    public bool FitToPage { get => ScaleMode == "fit"; set { ScaleModeIndex = value ? 0 : 1; } }
    public bool DarkenText { get => _darkenText; set { _darkenText = value; Changed(nameof(DarkenText), nameof(PrintIsDefault)); } }
    public IReadOnlyList<string> PaperSizes => _paperSizes;
    public int PaperIndex { get => _paperIndex; set { _paperIndex = Math.Clamp(value, 0, Math.Max(0, _paperSizes.Count - 1)); Changed(nameof(PaperIndex), nameof(PrintIsDefault)); } }

    private static readonly int[] PagesPerSheetValues = [1, 2, 4, 6, 9, 16];
    private int _pagesPerSheetIndex;
    public int PagesPerSheetIndex
    {
        get => _pagesPerSheetIndex;
        set
        {
            _pagesPerSheetIndex = Math.Clamp(value, 0, PagesPerSheetValues.Length - 1);
            Changed(nameof(PagesPerSheetIndex), nameof(PagesPerSheet), nameof(IsMultiPagePerSheet), nameof(PrintIsDefault));
        }
    }
    public int PagesPerSheet => PagesPerSheetValues[_pagesPerSheetIndex];
    public bool IsMultiPagePerSheet => PagesPerSheet >= 2;

    private int _pageOrderIndex;
    public int PageOrderIndex
    {
        get => _pageOrderIndex;
        set
        {
            _pageOrderIndex = Math.Clamp(value, 0, 1);
            Changed(nameof(PageOrderIndex), nameof(PageOrder), nameof(PrintIsDefault));
        }
    }
    public string PageOrder => _pageOrderIndex == 1 ? "N" : "Z";

    private bool _pageBorder;
    public bool PageBorder
    {
        get => _pageBorder;
        set
        {
            _pageBorder = value;
            Changed(nameof(PageBorder), nameof(PrintIsDefault));
        }
    }

    private int _scaleModeIndex;
    public int ScaleModeIndex
    {
        get => _scaleModeIndex;
        set
        {
            _scaleModeIndex = Math.Clamp(value, 0, 2);
            Changed(nameof(ScaleModeIndex), nameof(ScaleMode), nameof(IsCustomScale), nameof(FitToPage), nameof(PrintIsDefault));
        }
    }
    public string ScaleMode => _scaleModeIndex switch
    {
        1 => "actual",
        2 => "custom",
        _ => "fit"
    };
    public bool IsCustomScale => _scaleModeIndex == 2;

    private decimal _scalePercent = 100;
    public decimal ScalePercent
    {
        get => _scalePercent;
        set
        {
            _scalePercent = Math.Clamp(value, 1, 500);
            Changed(nameof(ScalePercent), nameof(PrintIsDefault));
        }
    }

    private int _colorModeIndex;
    public int ColorModeIndex
    {
        get => _colorModeIndex;
        set
        {
            _colorModeIndex = Math.Clamp(value, 0, 1);
            Changed(nameof(ColorModeIndex), nameof(Grayscale), nameof(PrintIsDefault));
        }
    }
    public bool Grayscale => _colorModeIndex == 1;

    private bool _collate = true;
    public bool Collate
    {
        get => _collate;
        set
        {
            _collate = value;
            Changed(nameof(Collate), nameof(PrintIsDefault));
        }
    }
    public bool CanCollate => _copies >= 2;

    /// <summary>Everything but the page range has a feature default; an empty printer or paper means 「跟隨系統／印表機」.</summary>
    public bool PrintIsDefault =>
        PrinterNameForSave() == SettingsController.PrintPrinter &&
        _copies == SettingsController.PrintCopies &&
        ((PrintDuplexMode)_duplex).ToString() == SettingsController.PrintDuplex &&
        PaperForSave() == SettingsController.PrintPaper &&
        ((PrintOrientation)_orientation).ToString() == SettingsController.PrintOrientation &&
        PagesPerSheet == SettingsController.PrintPagesPerSheet &&
        PageOrder == SettingsController.PrintPageOrder &&
        _pageBorder == SettingsController.PrintPageBorder &&
        ScaleMode == SettingsController.PrintScale &&
        (int)_scalePercent == SettingsController.PrintScalePercent &&
        Grayscale == SettingsController.PrintGrayscale &&
        _collate == SettingsController.PrintCollate &&
        _darkenText == SettingsController.PrintDarkenText;
    public void SavePrintDefault()
    {
        MarkSaved();
        SettingsController.PrintPrinter = PrinterNameForSave();
        SettingsController.PrintCopies = _copies;
        SettingsController.PrintDuplex = ((PrintDuplexMode)_duplex).ToString();
        SettingsController.PrintPaper = PaperForSave();
        SettingsController.PrintOrientation = ((PrintOrientation)_orientation).ToString();
        SettingsController.PrintPagesPerSheet = PagesPerSheet;
        SettingsController.PrintPageOrder = PageOrder;
        SettingsController.PrintPageBorder = _pageBorder;
        SettingsController.PrintScale = ScaleMode;
        SettingsController.PrintScalePercent = (int)_scalePercent;
        SettingsController.PrintGrayscale = Grayscale;
        SettingsController.PrintCollate = _collate;
        SettingsController.PrintDarkenText = _darkenText;
        OnPropertyChanged(nameof(PrintIsDefault));
    }
    /// <summary>The system default printer is saved as empty, so the default follows Windows when it changes.</summary>
    private string PrinterNameForSave()
    {
        if (_printerIndex < 0) return SettingsController.PrintPrinter;
        string name = _printers[_printerIndex];
        string? system = null;
        try
        {
            system = PrinterDiscovery.GetDefaultPrinter();
        }
        catch (Exception) // skipcq: CS-R1008
        {
            // Spooler or default printer query unavailable
        }
        return string.Equals(name, system, StringComparison.OrdinalIgnoreCase) && SettingsController.PrintPrinter.Length == 0 ? "" : name;
    }
    private string PaperForSave() => _paperIndex <= 0 ? "" : _paperSizes[_paperIndex];

    private static readonly IPrinterDiscoveryService PrinterDiscovery = new SystemPrinterDiscoveryService();

    private void LoadPrinters()
    {
        _copies = SettingsController.PrintCopies;
        _printPages = "";
        _duplex = Enum.TryParse(SettingsController.PrintDuplex, out PrintDuplexMode duplex) ? (int)duplex : 0;
        _orientation = Enum.TryParse(SettingsController.PrintOrientation, out PrintOrientation orientation) ? (int)orientation : 0;
        _pagesPerSheetIndex = Array.IndexOf(PagesPerSheetValues, SettingsController.PrintPagesPerSheet);
        if (_pagesPerSheetIndex < 0) _pagesPerSheetIndex = 0;
        _pageOrderIndex = string.Equals(SettingsController.PrintPageOrder, "N", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _pageBorder = SettingsController.PrintPageBorder;
        _scaleModeIndex = SettingsController.PrintScale switch
        {
            "actual" => 1,
            "custom" => 2,
            _ => 0
        };
        _scalePercent = Math.Clamp(SettingsController.PrintScalePercent, 1, 500);
        _colorModeIndex = SettingsController.PrintGrayscale ? 1 : 0;
        _collate = SettingsController.PrintCollate;
        _darkenText = SettingsController.PrintDarkenText;

        if (SystemPrinterDiscoveryService.HasCache)
        {
            ApplyDiscoveredPrinters(
                PrinterDiscovery.GetInstalledPrinters(),
                PrinterDiscovery.GetDefaultPrinter());
            return;
        }

        IsLoadingPrinters = true;
        _printers = new[] { Strings.T("ui_print_searching_printers") };
        _printerIndex = 0;
        _paperSizes = new[] { Strings.T("ui_print_printer_setting") };
        _paperIndex = 0;
        CanDuplex = false;

        System.Threading.Tasks.Task.Run(() =>
        {
            IReadOnlyList<string> installed;
            string? def;
            try
            {
                installed = PrinterDiscovery.GetInstalledPrinters();
                def = PrinterDiscovery.GetDefaultPrinter();
            }
            catch (Exception) // skipcq: CS-R1008
            {
                installed = Array.Empty<string>();
                def = null;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyDiscoveredPrinters(installed, def);
            });
        });
    }

    private void ApplyDiscoveredPrinters(IReadOnlyList<string> installed, string? def)
    {
        try
        {
            _printers = installed;
            // The saved printer when it is still installed, else the system default.
            string saved = SettingsController.PrintPrinter;
            string? fallback = saved.Length > 0 && _printers.Any(p => p.Equals(saved, StringComparison.OrdinalIgnoreCase))
                ? saved : def;
            if (fallback is null)
            {
                _printerIndex = _printers.Count > 0 ? 0 : -1;
            }
            else
            {
                int matchedIndex = -1;
                for (int i = 0; i < _printers.Count; i++)
                {
                    if (_printers[i].Equals(fallback, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedIndex = i;
                        break;
                    }
                }
                _printerIndex = Math.Max(0, matchedIndex);
            }
        }
        catch (Exception) // skipcq: CS-R1008
        {
            _printers = Array.Empty<string>(); // no printing support on this system
            _printerIndex = -1;
        }

        IsLoadingPrinters = false;
        CanDuplex = _printerIndex >= 0 && PrinterDiscovery.CanDuplex(_printers[_printerIndex]);
        if (!CanDuplex) _duplex = 0;
        LoadPaperSizes();
        Changed(nameof(Printers), nameof(PrinterIndex), nameof(HasPrinters), nameof(IsLoadingPrinters), nameof(CanDuplex), nameof(DuplexIndex), nameof(PaperSizes), nameof(PaperIndex), nameof(IsReady), nameof(BlockingText), nameof(PrintIsDefault));
    }

    private void LoadPaperSizes()
    {
        var sizes = new List<string> { Strings.T("ui_print_printer_setting") };
        try
        {
            if (_printerIndex >= 0) sizes.AddRange(PrinterDiscovery.GetSupportedPaperSizes(_printers[_printerIndex]));
        }
        catch (Exception) // skipcq: CS-R1008
        {
            // Spooler paper size query unavailable, keep 印表機預設 only
        }
        _paperSizes = sizes;
        string savedPaper = SettingsController.PrintPaper;
        _paperIndex = savedPaper.Length == 0 ? 0 : Math.Max(0, sizes.FindIndex(p => p.Equals(savedPaper, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(PaperSizes));
        OnPropertyChanged(nameof(PaperIndex));
    }

    // ----- start button -----
    /// <summary>False while a required choice is missing (spec 4.1: 必選選項).</summary>
    public bool IsReady => !(IsMarkdown && UsesWordTemplate && HasTemplateError) && !(IsDecrypt && _decryptNeedsPassword && _password.Length == 0) && !(IsImageFormat && _imageFormat < 0) && !(IsPrint && (_isLoadingPrinters || _printerIndex < 0)) && !(IsTranslate && AiIncomplete);

    /// <summary>What the start button says while it is disabled.</summary>
    public string BlockingText => GetBlockingText();

    private string GetBlockingText()
    {
        if (IsMarkdown && UsesWordTemplate && HasTemplateError) return Strings.T("ui_markdown_choose_a_template_that_works");
        if (IsDecrypt && _decryptNeedsPassword && _password.Length == 0) return Strings.T("ui_decrypt_enter_the_password_first");
        if (IsImageFormat && _imageFormat < 0) return Strings.T("ui_img_format_choose_a_format_first");
        if (IsPrint)
        {
            if (_isLoadingPrinters) return Strings.T("ui_print_searching_printers");
            if (_printerIndex < 0) return HasPrinters ? Strings.T("ui_print_choose_a_printer_first") : Strings.T("ui_print_cant_find_the_printer");
        }
        if (IsTranslate && AiIncomplete) return Strings.T("ui_translate_enter_the_ai_service_url");
        return "";
    }

    /// <summary>The command to run: the image format card picks one of the img-to-* commands.</summary>
    public string ResolveCommand(string command) =>
        IsImageFormat && _imageFormat >= 0 ? ImageFormatPrefix + ImageFormats[_imageFormat].ToLowerInvariant() : command;

    /// <summary>Options for Core, matching each use case's option keys.</summary>
    public Dictionary<string, object> Build()
    {
        var options = new Dictionary<string, object>(StringComparer.Ordinal);
        if (IsCompressPdf)
        {
            BuildCompressPdfOptions(options);
        }
        else if (IsImageCompress)
        {
            BuildImageCompressOptions(options);
        }
        else if (IsPdfToImage)
        {
            BuildPdfToImageOptions(options);
        }
        else if (IsTranslate)
        {
            BuildTranslateOptions(options);
        }
        else if (IsPrint)
        {
            BuildPrintOptions(options);
        }
        else if (IsDecrypt)
        {
            options["password"] = _password;
        }
        else if (IsMarkdown)
        {
            BuildMarkdownOptions(options);
        }
        return options;
    }

    private void BuildCompressPdfOptions(Dictionary<string, object> options)
    {
        options[CompressPdfUseCase.LevelOptionKey] = PdfCompressionOptions.ToOptionName(PdfCompressionOptions.FromSliderLevel(_pdfLevel));
    }

    private void BuildImageCompressOptions(Dictionary<string, object> options)
    {
        ImageCompressionLevel level = ImageCompressionOptions.FromSliderLevel(_imageLevel);
        options["level"] = ImageCompressionOptions.ToOptionName(level);
        options["quality"] = ImageCompressionOptions.GetQuality(level);
        options["max_dimension"] = MaxDimValues[_imageMaxDim];
    }

    private void BuildPdfToImageOptions(Dictionary<string, object> options)
    {
        options[PdfToImageOptions.FormatOptionKey] = PdfImageFormats[_pdfImageFormat];
        options[PdfToImageOptions.DpiOptionKey] = DpiValues[_pdfImageDpi];
        options[PdfToImageOptions.PagesOptionKey] = string.IsNullOrWhiteSpace(_pdfImagePages) ? PdfToImageOptions.DefaultPages : _pdfImagePages.Trim();
        options[PdfToImageOptions.StitchOptionKey] = _pdfImageStitch;
    }

    private void BuildTranslateOptions(Dictionary<string, object> options)
    {
        options[TranslatePdfUseCase.TargetLanguageOptionKey] = TranslateLanguageCodes[_translateLanguage];
        options[TranslatePdfUseCase.EngineOptionKey] = TranslateEngines[_translateEngine];
        if (IsCustomAi)
        {
            options[TranslatePdfUseCase.AiEndpointOptionKey] = _aiEndpoint.Trim();
            options[TranslatePdfUseCase.AiModelOptionKey] = _aiModel.Trim();
            options[TranslatePdfUseCase.AiApiKeyOptionKey] = _aiApiKey.Trim();
            // 「記住這個服務」: kept for next time when the run starts; unticked, nothing is stored.
            if (_aiEditing && _rememberAi)
            {
                SettingsController.TranslateAiEndpoint = _aiEndpoint.Trim();
                SettingsController.TranslateAiModel = _aiModel.Trim();
                SettingsController.TranslateAiApiKey = _aiApiKey.Trim();
            }
        }
    }

    private void BuildPrintOptions(Dictionary<string, object> options)
    {
        options[PrintPdfOptions.PrinterNameKey] = _printers[_printerIndex];
        options[PrintPdfOptions.CopiesKey] = _copies;
        if (!string.IsNullOrWhiteSpace(_printPages)) options[PrintPdfOptions.PageRangeKey] = _printPages.Trim();
        options[PrintPdfOptions.DuplexKey] = ((PrintDuplexMode)_duplex).ToString();
        options[PrintPdfOptions.OrientationKey] = ((PrintOrientation)_orientation).ToString();
        options[PrintPdfOptions.PagesPerSheetKey] = PagesPerSheet;
        options[PrintPdfOptions.PageOrderKey] = PageOrder;
        options[PrintPdfOptions.PageBorderKey] = _pageBorder;
        options[PrintPdfOptions.ScaleKey] = ScaleMode;
        options[PrintPdfOptions.ScalePercentKey] = (int)_scalePercent;
        options[PrintPdfOptions.GrayscaleKey] = Grayscale;
        options[PrintPdfOptions.CollateKey] = _collate;
        options[PrintPdfOptions.DarkenTextKey] = _darkenText;
        if (_paperIndex > 0) options[PrintPdfOptions.PaperSizeKey] = _paperSizes[_paperIndex];
    }

    private void BuildMarkdownOptions(Dictionary<string, object> options)
    {
        if (UsesWordTemplate)
        {
            options[MarkdownPdfOptions.ThemeKey] = MarkdownPdfOptions.ThemeDefault;
            options[MarkdownPdfOptions.TemplatePathKey] = _templatePath;
        }
        else
        {
            options[MarkdownPdfOptions.ThemeKey] = MarkdownThemes[_markdownTheme];
        }
        if (IsMarkdownPdf) options[MarkdownPdfOptions.PaperKey] = MarkdownPapers[_markdownPaper];
        options[MarkdownPdfOptions.TextSizeKey] = MarkdownTextSizes[_markdownTextSize];
        options[MarkdownPdfOptions.CodeThemeKey] = MarkdownCodeThemes[_markdownCode];
    }

    private void Changed(params string[] names)
    {
        foreach (string name in names) OnPropertyChanged(name);
    }
}
