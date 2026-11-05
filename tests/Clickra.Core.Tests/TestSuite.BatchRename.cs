using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Modules.BatchRename.Application;
using Clickra.Core.Modules.BatchRename.Domain;
using Clickra.Core.Modules.BatchRename.Processing;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterBatchRenameTests(TestRunner runner)
    {
        runner.Run("BatchRename: SafetyGuard validates filenames and flags reserved/invalid names", () =>
        {
            Assert.True(RenameSafetyGuard.ValidateFilename("valid_file-01.txt") == null, "Valid filename should return null.");
            Assert.True(RenameSafetyGuard.ValidateFilename("報告_2026.pdf") == null, "Valid CJK filename should return null.");

            Assert.True(RenameSafetyGuard.ValidateFilename("") != null, "Empty string should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("   ") != null, "Whitespace should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("invalid:name.txt") != null, "Colon should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("invalid*name.txt") != null, "Asterisk should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("invalid?name.txt") != null, "Question mark should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("trailing_space ") != null, "Trailing space should be invalid.");
            Assert.True(RenameSafetyGuard.ValidateFilename("trailing_dot.") != null, "Trailing dot should be invalid.");

            // Windows reserved device names
            Assert.True(RenameSafetyGuard.ValidateFilename("CON") != null, "CON should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("con.txt") != null, "CON.txt should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("PRN.pdf") != null, "PRN.pdf should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("AUX") != null, "AUX should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("NUL.doc") != null, "NUL.doc should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("com1.log") != null, "COM1.log should be reserved.");
            Assert.True(RenameSafetyGuard.ValidateFilename("Lpt2.dat") != null, "LPT2.dat should be reserved.");
        });

        runner.Run("BatchRename: Engine transforms prefix, suffix, numbering and case", () =>
        {
            string fakePath = @"C:\Fake\sample_document.TXT";

            // 1. Prefix and Suffix
            var opts1 = new BatchRenameOptions(Prefix: "CLICKRA_", Suffix: "_FINAL");
            Assert.Equal("CLICKRA_sample_document_FINAL.TXT", BatchRenameEngine.TransformName(fakePath, opts1, 1));

            // 2. Sequential Numbering (Suffix with padding 3)
            var opts2 = new BatchRenameOptions(
                Numbering: new NumberingOptions(StartIndex: 5, Step: 2, PaddingDigits: 3, Position: InsertPosition.Suffix, Separator: "-"));
            Assert.Equal("sample_document-005.TXT", BatchRenameEngine.TransformName(fakePath, opts2, 5));

            // 3. Sequential Numbering (Replace name)
            var opts3 = new BatchRenameOptions(
                Numbering: new NumberingOptions(StartIndex: 1, Step: 1, PaddingDigits: 2, Position: InsertPosition.ReplaceName));
            Assert.Equal("01.TXT", BatchRenameEngine.TransformName(fakePath, opts3, 1));

            // 4. Find & Replace
            var opts4 = new BatchRenameOptions(
                Replace: new ReplaceOptions(FindText: "document", ReplaceWith: "report"));
            Assert.Equal("sample_report.TXT", BatchRenameEngine.TransformName(fakePath, opts4, 1));

            // 5. Case & Extension transforms
            var opts5 = new BatchRenameOptions(
                CaseTransform: CaseTransformMode.UpperCase,
                ExtensionMode: ExtensionTransformMode.LowerCase);
            Assert.Equal("SAMPLE_DOCUMENT.txt", BatchRenameEngine.TransformName(fakePath, opts5, 1));

            var opts6 = new BatchRenameOptions(
                CaseTransform: CaseTransformMode.TitleCase,
                ExtensionMode: ExtensionTransformMode.Custom,
                CustomExtension: ".pdf");
            Assert.Equal("Sample_Document.pdf", BatchRenameEngine.TransformName(fakePath, opts6, 1));
        });

        runner.Run("BatchRename: Engine detects conflicts and duplicate targets", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "clickra_test_rename_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string file1 = Path.Combine(tempDir, "file1.txt");
                string file2 = Path.Combine(tempDir, "file2.txt");
                string existingFile = Path.Combine(tempDir, "target.txt");

                File.WriteAllText(file1, "content 1");
                File.WriteAllText(file2, "content 2");
                File.WriteAllText(existingFile, "already here");

                // Both files try to rename to "01.txt" -> Duplicate target conflict
                var optsConflict = new BatchRenameOptions(
                    Numbering: new NumberingOptions(Position: InsertPosition.ReplaceName, StartIndex: 1, Step: 0));

                var planDuplicate = BatchRenameEngine.GeneratePlan(new[] { file1, file2 }, optsConflict);
                Assert.True(planDuplicate.Items.All(i => i.Status == RenameItemStatus.ConflictDuplicateTarget),
                    "Expected all items to have ConflictDuplicateTarget status.");
                Assert.False(planDuplicate.CanExecute, "Duplicate plan must not be executable.");

                // file1 tries to rename to "target.txt" which already exists on disk
                var optsExisting = new BatchRenameOptions(
                    Replace: new ReplaceOptions("file1", "target"));
                var planExisting = BatchRenameEngine.GeneratePlan(new[] { file1 }, optsExisting);
                Assert.True(planExisting.Items[0].Status == RenameItemStatus.ConflictExistingFile,
                    "Expected ConflictExistingFile status when target exists on disk.");
                Assert.False(planExisting.CanExecute, "Plan with disk conflict must not be executable.");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });

        runner.Run("BatchRename: Executor executes direct rename and reports progress", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "clickra_test_rename_exec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string f1 = Path.Combine(tempDir, "doc_a.txt");
                string f2 = Path.Combine(tempDir, "doc_b.txt");
                File.WriteAllText(f1, "A");
                File.WriteAllText(f2, "B");

                var opts = new BatchRenameOptions(
                    Prefix: "NEW_",
                    Numbering: new NumberingOptions(StartIndex: 1, Step: 1, PaddingDigits: 2, Position: InsertPosition.Suffix));

                var plan = BatchRenameEngine.GeneratePlan(new[] { f1, f2 }, opts);
                Assert.True(plan.CanExecute, "Plan should be executable.");
                Assert.Equal(2, plan.ValidCount);

                int progressCalls = 0;
                var progress = new Progress<BatchRenameProgress>(_ => progressCalls++);

                var result = BatchRenameExecutor.ExecuteAsync(plan, progress, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(result.Succeeded, "Execution should succeed.");
                Assert.Equal(2, result.TotalProcessed);

                Assert.False(File.Exists(f1), "Old file 1 should not exist.");
                Assert.False(File.Exists(f2), "Old file 2 should not exist.");
                Assert.True(File.Exists(Path.Combine(tempDir, "NEW_doc_a_01.txt")), "New file 1 must exist.");
                Assert.True(File.Exists(Path.Combine(tempDir, "NEW_doc_b_02.txt")), "New file 2 must exist.");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });

        runner.Run("BatchRename: Executor safely resolves two-phase cyclic swaps without data loss", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "clickra_test_rename_swap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // File A -> File B, File B -> File A (Swap!)
                string fA = Path.Combine(tempDir, "alpha.txt");
                string fB = Path.Combine(tempDir, "beta.txt");
                File.WriteAllText(fA, "Alpha Content");
                File.WriteAllText(fB, "Beta Content");

                // Manual items to test cyclic swap detection and execution
                var items = new List<RenameItem>
                {
                    new(fA, "alpha.txt", "beta.txt", fB, RenameItemStatus.Valid),
                    new(fB, "beta.txt", "alpha.txt", fA, RenameItemStatus.Valid)
                };

                var plan = new BatchRenamePlan(items);
                Assert.True(plan.RequiresTwoPhaseSwap, "Cyclic swap must require two-phase renaming.");
                Assert.True(plan.CanExecute, "Cyclic swap plan should be valid for execution.");

                var result = BatchRenameExecutor.ExecuteAsync(plan, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(result.Succeeded, "Swap execution should succeed.");
                Assert.Equal(2, result.TotalProcessed);

                // Contents should now be swapped safely
                Assert.Equal("Alpha Content", File.ReadAllText(fB));
                Assert.Equal("Beta Content", File.ReadAllText(fA));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });

        runner.Run("BatchRename: BatchRenameUseCase resolves directory inputs", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "clickra_test_rename_dir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string f1 = Path.Combine(tempDir, "1.txt");
                string f2 = Path.Combine(tempDir, "2.txt");
                File.WriteAllText(f1, "1");
                File.WriteAllText(f2, "2");

                var files = BatchRenameUseCase.ResolveTargetFiles(new[] { tempDir });
                Assert.Equal(2, files.Count);
                Assert.Equal(Path.GetFullPath(f1), files[0]);
                Assert.Equal(Path.GetFullPath(f2), files[1]);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });
    }
}
