using System;
using System.IO;
using Clickra.Core;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        /// <summary>
        /// Shows the unified password prompt dialog for encrypted PDFs, waiting for user input
        /// and signaling the worker thread upon completion.
        /// </summary>
        private void ShowPasswordInputDialog(IntPtr hwnd)
        {
            if (_isPromptingVisualSplitter)
            {
                // The visual splitter renders its own controls; do not prompt for password.
                return;
            }

            string lang = SettingsPresentationController.Shared.Language;
            string subtitle = Loc("pdf_password_title");
            string promptFormat = _passwordPromptIsRetry ? Loc("pdf_password_retry") : Loc("pdf_password_prompt");
            string prompt = string.Format(promptFormat, Path.GetFileName(_passwordPromptFilename));

            string? pwd = NativeDialogs.PromptText(
                parentHwnd: hwnd,
                subtitle: subtitle,
                prompt: prompt,
                initialValue: null,
                isPassword: true,
                dpiScale: _dpiScale,
                language: lang);

            lock (_stateLock)
            {
                _inputPassword = pwd;
                _passwordCancelled = (pwd == null);
            }
            _passwordEvent.Set();
        }
    }
}
