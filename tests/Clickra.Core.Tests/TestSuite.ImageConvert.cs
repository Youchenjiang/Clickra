using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Processors;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string InputPng = "input.png";
    private const string CmdImgToHeic = "img-to-heic";
    private const string CmdImgToWebp = "img-to-webp";
    private const string CmdImgToPng = "img-to-png";
    private const string CmdImgToJpg = "img-to-jpg";
    private const string CmdImgToGif = "img-to-gif";
    private const string CmdImg2Pdf = "img2pdf";
    private const string ExtHeic = ".heic";
    private const string CliProjectDir = "Clickra.CLI";

    public static void RegisterImageConvertTests(TestRunner runner)
    {
        RegisterBasicFormatConversionTests(runner);
        RegisterCodecAndLocalizationTests(runner);
        RegisterCommandRegistryAndRunnerTests(runner);
    }

    private static void RegisterBasicFormatConversionTests(TestRunner runner)
    {
        runner.Run("ImageFormatConvertProcessor converts PNG to JPG with JPEG magic bytes", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string output = Path.Combine(tempDir, "output.jpg");
                FileProcessor.ConvertImageFormat(input, output, "jpg");
                Assert.True(File.Exists(output), "Expected JPG output to exist.");
                Assert.True(HasJpegMagicBytes(output), "Expected JPG magic bytes (FF D8 FF).");
            }));

        runner.Run("ImageFormatConvertProcessor converts JPG to PNG with PNG magic bytes", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.jpg", ImageFormat.Jpeg);
                string output = Path.Combine(tempDir, "output.png");
                FileProcessor.ConvertImageFormat(input, output, "png");
                Assert.True(File.Exists(output), "Expected PNG output to exist.");
                Assert.True(HasPngMagicBytes(output), "Expected PNG magic bytes (89 50 4E 47).");
            }));

        runner.Run("ImageFormatConvertProcessor converts to WEBP and GIF", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string gif = Path.Combine(tempDir, "out.gif");
                FileProcessor.ConvertImageFormat(input, gif, "gif");
                Assert.True(File.Exists(gif), "Expected GIF output to exist.");

                string webp = Path.Combine(tempDir, "out.webp");
                Assert.True(ImageFormatConvertProcessor.IsWebpEncodingSupported(), "WEBP encoding should be bundled with Clickra.");
                FileProcessor.ConvertImageFormat(input, webp, "webp");
                Assert.True(File.Exists(webp), "Expected WEBP output to exist.");
                Assert.True(HasWebpMagicBytes(webp), "Expected RIFF/WEBP magic bytes.");
            }));

        runner.Run("ImageFormatConvertProcessor skips same-format input without overwriting the source", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                long originalSize = new FileInfo(input).Length;
                string same = Path.Combine(tempDir, InputPng);
                FileProcessor.ConvertImageFormat(input, same, "png");
                Assert.True(File.Exists(same), "Expected the source file to still exist.");
                Assert.True(new FileInfo(same).Length == originalSize, "Source must not be overwritten by a same-format conversion.");
            }));

        runner.Run("ImageFormatConvertProcessor rejects unsupported target formats", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string output = Path.Combine(tempDir, "out.xyz");
                Assert.Throws<NotSupportedException>(() => FileProcessor.ConvertImageFormat(input, output, "xyz"));
            }));

        runner.Run("ImageFormatConvertProcessor supports HEIC output when a codec is available", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string output = Path.Combine(tempDir, "out.heic");
                if (ImageFormatConvertProcessor.IsHeicEncodingSupported())
                {
                    FileProcessor.ConvertImageFormat(input, output, "heic");
                    Assert.True(File.Exists(output), "Expected HEIC output to exist when a HEIC encoder is present.");
                    Assert.True(HasHeicMagicBytes(output), "Expected HEIC ftyp magic bytes.");

                    string backPng = Path.Combine(tempDir, "from_heic.png");
                    FileProcessor.ConvertImageFormat(output, backPng, "png");
                    Assert.True(File.Exists(backPng), "Expected PNG output decoded from HEIC to exist.");
                    Assert.True(HasPngMagicBytes(backPng), "Expected PNG magic bytes from HEIC decode.");

                    string backJpg = Path.Combine(tempDir, "from_heic.jpg");
                    FileProcessor.ConvertImageFormat(output, backJpg, "jpg");
                    Assert.True(File.Exists(backJpg), "Expected JPG output decoded from HEIC to exist.");
                    Assert.True(HasJpegMagicBytes(backJpg), "Expected JPG magic bytes from HEIC decode.");

                    string heicPdf = Path.Combine(tempDir, "heic_to_pdf.pdf");
                    FileProcessor.ConvertImagesToPdf(new List<string> { output }, heicPdf);
                    Assert.True(File.Exists(heicPdf), "Expected PDF output from HEIC input to exist.");
                    Assert.True(new FileInfo(heicPdf).Length > 0, "Expected non-empty PDF from HEIC.");

                    string stitchedOut = Path.Combine(tempDir, "stitched.png");
                    FileProcessor.StitchImages(new List<string> { output, output }, stitchedOut);
                    Assert.True(File.Exists(stitchedOut), "Expected stitched output from HEIC inputs to exist.");
                    Assert.True(HasPngMagicBytes(stitchedOut), "Expected valid PNG header from stitched HEIC.");
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() => FileProcessor.ConvertImageFormat(input, output, "heic"));
                }
            }));
    }

    private static void RegisterCodecAndLocalizationTests(TestRunner runner)
    {
        runner.Run("ImageFormatConvertProcessor maps codecs and Store URIs for target formats", () =>
        {
            Assert.Equal("heic", ImageFormatConvertProcessor.GetRequiredCodec("heic") ?? "");
            Assert.Equal("heic", ImageFormatConvertProcessor.GetRequiredCodec(".HEIC") ?? "");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("webp") is null, "webp output uses the bundled encoder.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec(".WEBP") is null, "webp output uses the bundled encoder.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("png") is null, "png reports no extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("jpg") is null, "jpg reports no extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("xyz") is null, "unknown formats report no codec.");

            Assert.Equal("ms-windows-store://pdp/?productid=9PMMSR1CGPWG", ImageFormatConvertProcessor.GetCodecStoreUri("heic")?.AbsoluteUri ?? "");
            Assert.True(ImageFormatConvertProcessor.GetCodecStoreUri("webp") is null, "webp output does not require a Store codec.");
            Assert.True(ImageFormatConvertProcessor.GetCodecStoreUri("png") is null, "png does not require a Store codec.");
        });

        runner.Run("ImageFormatConvertProcessor preflight reports the codec a command is missing", () =>
        {
            bool heicSupported = ImageFormatConvertProcessor.IsHeicEncodingSupported();
            string dummyPng = Path.Combine(Path.GetTempPath(), "clickra_probe_test.png");
            string dummyPdf = Path.Combine(Path.GetTempPath(), "clickra_probe_test.pdf");

            Assert.True(heicSupported == (ImageFormatConvertProcessor.GetMissingCodecForCommand(CmdImgToHeic, new List<string> { dummyPng }) is null), "img-to-heic preflight must agree with IsHeicEncodingSupported.");
            Assert.True(heicSupported == (ImageFormatConvertProcessor.GetMissingCodecForCommand("IMG-TO-HEIC", new List<string> { dummyPng }) is null), "img-to-heic preflight must be case-insensitive.");
            Assert.True(ImageFormatConvertProcessor.GetMissingCodecForCommand(CmdImgToWebp, new List<string> { dummyPng }) is null, "img-to-webp uses the bundled encoder and needs no Store codec.");
            Assert.True(ImageFormatConvertProcessor.GetMissingCodecForCommand(CmdImg2Pdf, new List<string> { dummyPng }) is null, "img2pdf needs no extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetMissingCodecForCommand("merge-pdf", new List<string> { dummyPdf }) is null, "merge-pdf needs no extra codec.");

            Assert.True(WicImageHelper.IsHeifExtension(".heic"), ".heic should require the Windows HEIF decoder.");
            Assert.True(WicImageHelper.IsHeifExtension(".HEIF"), ".heif matching should be case-insensitive.");
            Assert.True(WicImageHelper.IsHeifExtension(".hif"), ".hif should require the Windows HEIF decoder.");
            Assert.False(WicImageHelper.IsHeifExtension(".jpg"), ".jpg must not be treated as a HEIF alias.");

            if (!WicImageHelper.IsHeicDecoderAvailable())
            {
                Assert.Equal("heic", ImageFormatConvertProcessor.GetMissingCodecForCommand(CmdImgToPng, new[] { "probe.heif" }) ?? "");
                Assert.Equal("heic", ImageFormatConvertProcessor.GetMissingCodecForCommand(CmdImgToPng, new[] { "probe.hif" }) ?? "");
            }
        });

        runner.Run("Missing-codec messages are localized in every supported language", () =>
        {
            foreach (var lang in new[] { "zh-TW", "zh-CN", "en-US", "ja-JP", "ko-KR" })
            {
                foreach (string command in new[] { CmdImgToPng, CmdImgToJpg, CmdImgToWebp, CmdImgToGif, CmdImgToHeic })
                {
                    string labelKey = "cmd_" + command.Replace('-', '_');
                    Assert.False(Localization.T(labelKey, lang).Equals(labelKey, StringComparison.Ordinal), $"image command label missing for {command} in {lang}.");
                }

                string heic = Localization.T("error_heic_codec_missing", lang);
                string heicDecoder = Localization.T("error_heic_decoder_missing", lang);
                string collision = Localization.T("error_image_output_collision", lang);
                Assert.False(heic.Equals("error_heic_codec_missing", StringComparison.Ordinal), $"heic error message missing for {lang}.");
                Assert.False(heicDecoder.Equals("error_heic_decoder_missing", StringComparison.Ordinal), $"heic decoder error message missing for {lang}.");
                Assert.True(collision.Contains("{0}"), $"image output collision message for {lang} lost its path placeholder.");
                string prompt = Localization.T("codec_missing_store_prompt", lang);
                Assert.True(prompt.Contains("{0}") && prompt.Contains("{1}"), $"store prompt for {lang} lost its format placeholders.");
            }
            Assert.Equal("HEIF Image Extensions", Localization.T("codec_heif_extension_name", "en-US"));
            Assert.Equal("安裝編碼器", Localization.T("codec_missing_install_action", "zh-TW"));
        });

        runner.Run("ImageFormatConvertProcessor.ToOutputExtension maps aliases to canonical extensions", () =>
        {
            Assert.Equal(".jpg", ImageFormatConvertProcessor.ToOutputExtension("jpg"));
            Assert.Equal(".jpg", ImageFormatConvertProcessor.ToOutputExtension("jpeg"));
            Assert.Equal(".png", ImageFormatConvertProcessor.ToOutputExtension("PNG"));
            Assert.Equal(".webp", ImageFormatConvertProcessor.ToOutputExtension("webp"));
            Assert.Equal(ExtHeic, ImageFormatConvertProcessor.ToOutputExtension("heic"));
        });
    }

    private static void RegisterCommandRegistryAndRunnerTests(TestRunner runner)
    {
        runner.Run("Image format use cases own per-source output planning", () =>
            RunWithTempDirectory(tempDir =>
            {
                string firstDir = Path.Combine(tempDir, "first");
                string secondDir = Path.Combine(tempDir, "second");
                Directory.CreateDirectory(firstDir);
                Directory.CreateDirectory(secondDir);
                string first = CreateTestImage(firstDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(secondDir, "b.gif", ImageFormat.Gif);

                foreach (var (command, extension) in new[]
                {
                    (CmdImgToJpg, ".jpg"),
                    (CmdImgToWebp, ".webp"),
                    (CmdImgToHeic, ExtHeic)
                })
                {
                    ConversionPlan plan = new ImageFormatConvertUseCase(command).Plan(new ConversionRequest(
                        command,
                        new[] { first, second }));

                    Assert.Equal(Path.Combine(firstDir, "a" + extension), plan.Outputs[0]);
                    Assert.Equal(Path.Combine(secondDir, "b" + extension), plan.Outputs[1]);
                    Assert.True(plan.RequiredCapabilities.Count == 0,
                        $"{command} must not require a presentation interaction capability.");
                }
            }));

        runner.Run("Image format use case honors shared output directory and collision safety", () =>
            RunWithTempDirectory(tempDir =>
            {
                string firstDir = Path.Combine(tempDir, "first");
                string secondDir = Path.Combine(tempDir, "second");
                string outputDir = Path.Combine(tempDir, "out");
                Directory.CreateDirectory(firstDir);
                Directory.CreateDirectory(secondDir);
                string first = CreateTestImage(firstDir, "photo.png", ImageFormat.Png);
                string second = CreateTestImage(secondDir, "photo.gif", ImageFormat.Gif);
                var useCase = new ImageFormatConvertUseCase(CmdImgToJpg);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    useCase.Plan(new ConversionRequest(
                        CmdImgToJpg,
                        new[] { first, second },
                        OutputOverride: outputDir)));

                Assert.True(ex.Message.Contains("photo.jpg", StringComparison.OrdinalIgnoreCase),
                    "Collision error should identify the conflicting target path.");
            }));

        runner.Run("Image format use case can run without task tracking", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string outputDir = Path.Combine(tempDir, "out");
                Directory.CreateDirectory(outputDir);
                var useCase = new ImageFormatConvertUseCase(CmdImgToWebp);
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    CmdImgToWebp,
                    new[] { input },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected image format conversion success.");
                Assert.True(string.IsNullOrEmpty(result.TaskId), "Untracked image format conversion must not create a task identity.");
                Assert.True(File.Exists(plan.Outputs[0]), "Expected converted image output.");
                Assert.True(HasWebpMagicBytes(plan.Outputs[0]), "Expected converted output to contain WEBP magic bytes.");
            }));

        runner.Run("ImgStitch use case owns single-output planning", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                ConversionPlan plan = new ImgStitchUseCase().Plan(new ConversionRequest(
                    ImgStitchUseCase.CommandName,
                    new[] { first, second }));

                Assert.True(plan.Outputs.Count == 1, "img-stitch must plan exactly one output.");
                Assert.Equal(Path.Combine(tempDir, ImgStitchUseCase.OutputFileName), plan.Outputs[0]);
                Assert.True(plan.RequiredCapabilities.Count == 0,
                    "img-stitch must not require a presentation interaction capability.");
            }));

        runner.Run("ImgStitch use case honors a shared output directory", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                string outputDir = Path.Combine(tempDir, "out");
                ConversionPlan plan = new ImgStitchUseCase().Plan(new ConversionRequest(
                    ImgStitchUseCase.CommandName,
                    new[] { first, second },
                    OutputOverride: outputDir));

                Assert.Equal(Path.Combine(Path.GetFullPath(outputDir), ImgStitchUseCase.OutputFileName), plan.Outputs[0]);
            }));

        runner.Run("ImgStitch use case can run without task tracking", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                string outputDir = Path.Combine(tempDir, "out");
                Directory.CreateDirectory(outputDir);
                var useCase = new ImgStitchUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    ImgStitchUseCase.CommandName,
                    new[] { first, second },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected img-stitch success.");
                Assert.True(string.IsNullOrEmpty(result.TaskId), "Untracked img-stitch must not create a task identity.");
                Assert.True(File.Exists(plan.Outputs[0]), "Expected stitched PNG output.");
                Assert.True(HasPngMagicBytes(plan.Outputs[0]), "Expected stitched output to contain PNG magic bytes.");
            }));

        runner.Run("ImgMerge use case owns single-output planning", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                ConversionPlan plan = new ImgMergeUseCase().Plan(new ConversionRequest(
                    ImgMergeUseCase.CommandName,
                    new[] { first, second }));

                Assert.True(plan.Outputs.Count == 1, "img-merge must plan exactly one output.");
                Assert.Equal(Path.Combine(tempDir, ImgMergeUseCase.OutputFileName), plan.Outputs[0]);
                Assert.True(plan.RequiredCapabilities.Count == 0,
                    "img-merge must not require a presentation interaction capability.");
            }));

        runner.Run("ImgMerge use case honors a shared output directory", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                string outputDir = Path.Combine(tempDir, "out");
                ConversionPlan plan = new ImgMergeUseCase().Plan(new ConversionRequest(
                    ImgMergeUseCase.CommandName,
                    new[] { first, second },
                    OutputOverride: outputDir));

                Assert.Equal(Path.Combine(Path.GetFullPath(outputDir), ImgMergeUseCase.OutputFileName), plan.Outputs[0]);
            }));

        runner.Run("ImgMerge use case can run without task tracking", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateTestImage(tempDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(tempDir, "b.jpg", ImageFormat.Jpeg);
                string outputDir = Path.Combine(tempDir, "out");
                Directory.CreateDirectory(outputDir);
                var useCase = new ImgMergeUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    ImgMergeUseCase.CommandName,
                    new[] { first, second },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected img-merge success.");
                Assert.True(string.IsNullOrEmpty(result.TaskId), "Untracked img-merge must not create a task identity.");
                Assert.True(File.Exists(plan.Outputs[0]), "Expected merged image PDF output.");
            }));

        runner.Run("Img2Pdf use case owns per-source output planning", () =>
            RunWithTempDirectory(tempDir =>
            {
                string firstDir = Path.Combine(tempDir, "first");
                string secondDir = Path.Combine(tempDir, "second");
                Directory.CreateDirectory(firstDir);
                Directory.CreateDirectory(secondDir);
                string first = CreateTestImage(firstDir, "a.png", ImageFormat.Png);
                string second = CreateTestImage(secondDir, "b.jpg", ImageFormat.Jpeg);

                ConversionPlan plan = new Img2PdfUseCase().Plan(new ConversionRequest(
                    Img2PdfUseCase.CommandName,
                    new[] { first, second }));

                Assert.Equal(Path.Combine(firstDir, "a.pdf"), plan.Outputs[0]);
                Assert.Equal(Path.Combine(secondDir, "b.pdf"), plan.Outputs[1]);
                Assert.True(plan.RequiredCapabilities.Count == 0,
                    "img2pdf must not require a presentation interaction capability.");
            }));

        runner.Run("Img2Pdf use case honors a shared output directory", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string outputDir = Path.Combine(tempDir, "out");
                ConversionPlan plan = new Img2PdfUseCase().Plan(new ConversionRequest(
                    Img2PdfUseCase.CommandName,
                    new[] { input },
                    OutputOverride: outputDir));

                Assert.Equal(Path.Combine(Path.GetFullPath(outputDir), "input.pdf"), plan.Outputs[0]);
            }));

        runner.Run("Img2Pdf use case can run without task tracking", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, InputPng, ImageFormat.Png);
                string outputDir = Path.Combine(tempDir, "out");
                Directory.CreateDirectory(outputDir);
                var useCase = new Img2PdfUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    Img2PdfUseCase.CommandName,
                    new[] { input },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected img2pdf success.");
                Assert.True(string.IsNullOrEmpty(result.TaskId), "Untracked img2pdf must not create a task identity.");
                Assert.True(File.Exists(plan.Outputs[0]), "Expected img2pdf output from application use case.");
            }));

        runner.Run("ConvertCommandRegistry registers img-to-* commands with image inputs", () =>
        {
            foreach (var command in new[] { CmdImgToPng, CmdImgToJpg, CmdImgToWebp, CmdImgToGif, CmdImgToHeic })
            {
                Assert.True(ConvertCommandRegistry.IsKnownCommand(command), $"{command} should be a known command.");
                Assert.True(ConvertCommandRegistry.GetMinFiles(command) == 1, $"{command} should accept a single file.");
                Assert.True(ConvertCommandRegistry.GetAllowedExtensions(command).Contains(".png", StringComparer.OrdinalIgnoreCase) || command == CmdImgToPng, $"{command} should accept PNG inputs unless PNG is the target.");
                Assert.True(ConvertCommandRegistry.GetAllowedExtensions(command).Contains(ExtHeic, StringComparer.OrdinalIgnoreCase) || command == CmdImgToHeic, $"{command} should accept HEIC inputs unless HEIC is the target.");
                Assert.Equal("image", ConvertCommandRegistry.GetFileTypeForCommand(command));
            }
        });

        runner.Run("ConvertCommandRegistry excludes same-format sources from img-to-* commands", () =>
        {
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToPng).Contains(".png", StringComparer.OrdinalIgnoreCase), "img-to-png must not accept .png inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToJpg).Contains(".jpg", StringComparer.OrdinalIgnoreCase), "img-to-jpg must not accept .jpg inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToJpg).Contains(".jpeg", StringComparer.OrdinalIgnoreCase), "img-to-jpg must not accept .jpeg inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToWebp).Contains(".webp", StringComparer.OrdinalIgnoreCase), "img-to-webp must not accept .webp inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToGif).Contains(".gif", StringComparer.OrdinalIgnoreCase), "img-to-gif must not accept .gif inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToHeic).Contains(ExtHeic, StringComparer.OrdinalIgnoreCase), "img-to-heic must not accept .heic inputs.");

            Assert.True(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToHeic).Contains(".png", StringComparer.OrdinalIgnoreCase), "img-to-heic should still accept .png inputs.");
            Assert.True(ConvertCommandRegistry.GetExcludedExtensions(CmdImgToHeic).Contains(ExtHeic, StringComparer.OrdinalIgnoreCase), "img-to-heic should declare .heic as excluded.");
            Assert.True(ConvertCommandRegistry.GetExcludedExtensions(CmdImg2Pdf).Length == 0, "img2pdf accepts every image input.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions(CmdImg2Pdf).Contains(".bmp", StringComparer.OrdinalIgnoreCase), "img2pdf must accept .bmp inputs.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions(CmdImg2Pdf).Contains(".tiff", StringComparer.OrdinalIgnoreCase), "img2pdf must accept .tiff inputs.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions(CmdImgToPng).Contains(".bmp", StringComparer.OrdinalIgnoreCase), "img-to-png must accept .bmp inputs.");
        });

        runner.Run("Image format use case predicts target extension per input", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "photo.png", ImageFormat.Png);
                ConversionPlan jpgPlan = new ImageFormatConvertUseCase(CmdImgToJpg).Plan(
                    new ConversionRequest(CmdImgToJpg, new[] { input }));
                Assert.True(jpgPlan.Outputs.Count == 1, "Expected exactly one predicted output.");
                Assert.True(jpgPlan.Outputs[0].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase), $"Expected .jpg output, got {jpgPlan.Outputs[0]}.");

                ConversionPlan heicPlan = new ImageFormatConvertUseCase(CmdImgToHeic).Plan(
                    new ConversionRequest(CmdImgToHeic, new[] { input }));
                Assert.True(heicPlan.Outputs.Count == 1, "Expected exactly one predicted output.");
                Assert.True(heicPlan.Outputs[0].EndsWith(ExtHeic, StringComparison.OrdinalIgnoreCase), $"Expected .heic output, got {heicPlan.Outputs[0]}.");
            }));

        runner.Run("Image format use case rejects colliding image outputs", () =>
            RunWithTempDirectory(tempDir =>
            {
                string png = CreateTestImage(tempDir, "photo.png", ImageFormat.Png);
                string gif = CreateTestImage(tempDir, "photo.gif", ImageFormat.Gif);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    new ImageFormatConvertUseCase(CmdImgToJpg).Plan(
                        new ConversionRequest(CmdImgToJpg, new[] { png, gif })));
                Assert.True(ex.Message.Contains("photo.jpg", StringComparison.OrdinalIgnoreCase), "Collision error should identify the conflicting output path.");
                Assert.False(ex.Message.Contains("{0}", StringComparison.Ordinal), "Collision error should substitute the output path placeholder.");
            }));

        runner.Run("Image format use case runs img-to-heic end to end when a HEIC encoder is available", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "source.png", ImageFormat.Png);
                var messages = new List<string>();
                var useCase = new ImageFormatConvertUseCase(CmdImgToHeic);
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    CmdImgToHeic,
                    new[] { input },
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
                var progress = new ImmediateProgress<ConversionProgress>(state => messages.Add(state.Message));
                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress).GetAwaiter().GetResult();

                if (ImageFormatConvertProcessor.IsHeicEncodingSupported())
                {
                    Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected HEIC conversion success.");
                    Assert.True(File.Exists(plan.Outputs[0]), "Expected HEIC output to exist when a HEIC encoder is present.");
                    Assert.True(HasHeicMagicBytes(plan.Outputs[0]), "Expected HEIC magic bytes on use-case output.");
                }
                else
                {
                    Assert.True(result.Status == ConversionResultStatus.Failed,
                        "Missing HEIC encoder should fail through the application result instead of succeeding.");
                }
                Assert.True(messages.Count > 0, "Expected the use case to attempt the conversion.");
            }));

        runner.Run("Image format use case runs img-to-webp end to end", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "source.png", ImageFormat.Png);
                var messages = new List<string>();
                var useCase = new ImageFormatConvertUseCase(CmdImgToWebp);
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    CmdImgToWebp,
                    new[] { input },
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));
                var progress = new ImmediateProgress<ConversionProgress>(state => messages.Add(state.Message));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected WEBP conversion success.");
                Assert.True(File.Exists(plan.Outputs[0]), "Expected WEBP output to exist after use-case execution.");
                Assert.True(HasWebpMagicBytes(plan.Outputs[0]), "Expected use-case WEBP output to have RIFF/WEBP magic bytes.");
                Assert.True(messages.Count > 0, "Expected the use case to attempt the conversion.");
            }));

        runner.Run("Product surfaces expose every img-to-* command", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string cli = File.ReadAllText(Path.Combine(root, "src", CliProjectDir, "Cli", "ClickraCli.cs"));
            string startup = File.ReadAllText(Path.Combine(root, "src", CliProjectDir, "Cli", "ClickraStartup.cs"));
            string dashboard = File.ReadAllText(Path.Combine(root, "src", CliProjectDir, "Dashboard", "DashboardWindow.ConvertRegistry.cs"));
            string progress = File.ReadAllText(Path.Combine(root, "src", CliProjectDir, "Progress", "ProgressWindow.Process.cs"));
            string fluentXaml = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml"));
            string fluentCode = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));

            foreach (var (command, buttonName, labelKey) in new[]
            {
                (CmdImgToPng, "BtnImgToPng", "cmd_img_to_png"),
                (CmdImgToJpg, "BtnImgToJpg", "cmd_img_to_jpg"),
                (CmdImgToWebp, "BtnImgToWebp", "cmd_img_to_webp"),
                (CmdImgToGif, "BtnImgToGif", "cmd_img_to_gif"),
                (CmdImgToHeic, "BtnImgToHeic", "cmd_img_to_heic")
            })
            {
                Assert.True(cli.Contains($"case \"{command}\"", StringComparison.Ordinal), $"Legacy CLI must dispatch {command}.");
                Assert.True(startup.Contains(command, StringComparison.Ordinal), $"CLI help/version must list {command}.");
                Assert.True(dashboard.Contains($"Command = \"{command}\"", StringComparison.Ordinal), $"Native dashboard must expose {command}.");
                Assert.True(progress.Contains("imageFormatUseCase is ImageFormatConvertUseCase", StringComparison.Ordinal)
                            && progress.Contains("RunApplicationImageFormat(hwnd, cmd, currentFiles, progressCallback)", StringComparison.Ordinal),
                    $"Native progress routing must execute {command} through the application use-case catalog.");
                Assert.False(progress.Contains($"case \"{command}\"", StringComparison.Ordinal),
                    $"Native progress routing must not keep a second dispatch branch for {command}.");
                Assert.True(cli.Contains("HandleImageFormatQuiet(command, files, outputDir)", StringComparison.Ordinal),
                    $"Quiet CLI must execute {command} through the application use-case catalog.");
                Assert.True(fluentXaml.Contains($"x:Name=\"{buttonName}\"", StringComparison.Ordinal) && fluentXaml.Contains($"Tag=\"{command}\"", StringComparison.Ordinal), $"Fluent XAML must expose {command}.");
                Assert.True(fluentCode.Contains(buttonName, StringComparison.Ordinal) && fluentCode.Contains(labelKey, StringComparison.Ordinal), $"Fluent code-behind must hook and localize {command}.");
            }
        });
    }

    private static string CreateTestImage(string directory, string fileName, ImageFormat format)
    {
        string path = Path.Combine(directory, fileName);
        using (var bitmap = new Bitmap(32, 24))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                using var brush = new SolidBrush(Color.FromArgb(40, 90, 180));
                g.FillRectangle(brush, 4, 4, 24, 16);
            }
            bitmap.Save(path, format);
        }
        return path;
    }

    private static bool HasJpegMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(3).ToArray();
        return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }

    private static bool HasPngMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(8).ToArray();
        return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
    }

    private static bool HasHeicMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(12).ToArray();
        if (header.Length < 12) return false;
        return header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70;
    }

    private static bool HasWebpMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(12).ToArray();
        if (header.Length < 12) return false;
        return header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
               header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P';
    }
}
