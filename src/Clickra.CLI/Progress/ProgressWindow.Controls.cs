using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using Clickra.Core;
using Clickra.Core.Modules.PdfSplit.Presentation;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public partial class ProgressWindow
    {
        /// <summary>Static window procedure: resolves the ProgressWindow instance from the
        /// window's user data, routes messages to it and frees the GCHandle on destroy.</summary>
        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            ProgressWindow? window = null;
            if (msg == 0x0081) // WM_NCCREATE
            {
                IntPtr lpCreateParams = Marshal.ReadIntPtr(l);
                SetWindowLongPtr(hwnd, -21, lpCreateParams); // GWLP_USERDATA = -21
                if (lpCreateParams != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(lpCreateParams);
                    window = gcHandle.Target as ProgressWindow;
                    if (window != null) window._hwnd = hwnd;
                }
            }
            else
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        window = gcHandle.Target as ProgressWindow;
                    }
                }
            }

            IntPtr result = IntPtr.Zero;
            if (window != null)
            {
                result = window.InstanceWndProc(hwnd, msg, w, l);
            }
            else
            {
                result = DefWindowProcW(hwnd, msg, w, l);
            }

            if (msg == 0x0082) // WM_NCDESTROY
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        gcHandle.Free();
                    }
                    SetWindowLongPtr(hwnd, -21, IntPtr.Zero);
                }
            }

            return result;
        }

        /// <summary>Subclassed EDIT window procedure: maps Enter to OK and Escape to Cancel
        /// for the password prompt.</summary>
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static unsafe IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
            if (msg == 0x0100) // WM_KEYDOWN
            {
                int key = w.ToInt32();
                if (key == 0x0D) // VK_RETURN
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1001, IntPtr.Zero); // WM_COMMAND, ID = 1001 (OK)
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1002, IntPtr.Zero); // WM_COMMAND, ID = 1002 (Cancel)
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0002) // WM_DESTROY
            {
                RemoveProp(hwnd, "ClickraOldWndProc");
            }
            return oldProc != IntPtr.Zero ? CallWindowProc(oldProc, hwnd, msg, w, l) : DefWindowProcW(hwnd, msg, w, l);
        }

        /// <summary>Instance window procedure: routes each Win32 message to a dedicated
        /// handler, falling back to DefWindowProcW for messages no handler consumed.</summary>
        private unsafe IntPtr InstanceWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_USER_SHOW_PASSWORD_INPUT: return HandleShowPasswordInput(hwnd);
                case WM_USER_HIDE_PASSWORD_INPUT: return HandleHidePasswordInput();
                case 0x0100: // WM_KEYDOWN
                    {
                        IntPtr? keyResult = HandleKeyDown(hwnd, w);
                        if (keyResult != null) return keyResult.Value;
                    }
                    break;
                case 0x0133: return HandleCtlColorEdit(w); // WM_CTLCOLOREDIT
                case 0x0111: return HandleCommand(); // WM_COMMAND
                case 0x020A: return HandleMouseWheel(hwnd, w, l); // WM_MOUSEWHEEL
                case WM_USER_INVALIDATE: return HandleInvalidate(hwnd, w);
                case 0x0200: return HandleMouseMove(hwnd, l); // WM_MOUSEMOVE
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0203: // WM_LBUTTONDBLCLK
                    return HandleLButtonDown(hwnd, l);
                case 0x0202: return HandleLButtonUp(hwnd); // WM_LBUTTONUP
                case WM_TRAYICON: return HandleTrayIcon(hwnd, l);
                case 0x0112: // WM_SYSCOMMAND
                    {
                        IntPtr? sysResult = HandleSysCommand(hwnd, w);
                        if (sysResult != null) return sysResult.Value;
                    }
                    break;
                case 0x0010: return HandleClose(hwnd); // WM_CLOSE
                case 0x02E0: return HandleDpiChanged(hwnd, w, l); // WM_DPICHANGED
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x0113: return HandleTimer(hwnd); // WM_TIMER
                case 0x0020: // WM_SETCURSOR
                    if (_isPromptingPrintOptions && (_printIsZoomed || _hoveredPrintElement != PrintElement.None || _openDropdown != PrintDropdownType.None))
                    {
                        SetCursor(LoadCursorW(IntPtr.Zero, IDC_HAND));
                        return (IntPtr)1;
                    }
                    break;
                case 0x000F: return HandlePaint(hwnd); // WM_PAINT
                case 0x0002: return HandleDestroy(hwnd); // WM_DESTROY
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        /// <summary>Shows the password input (or visual splitter) panel, expanding the window
        /// when the splitter was active.</summary>
        private IntPtr HandleShowPasswordInput(IntPtr hwnd)
        {
            if (_isPromptingVisualSplitter)
            {
                ResizeWindowForVisualSplitter(hwnd, true);
                return IntPtr.Zero;
            }
            ShowPasswordInputDialog(hwnd);
            return IntPtr.Zero;
        }

        /// <summary>Hides the password input panel.</summary>
        private static IntPtr HandleHidePasswordInput()
        {
            return IntPtr.Zero;
        }

        /// <summary>Handles visual-splitter zoom keyboard shortcuts (Esc / Space / Enter,
        /// +/-/0); returns null when the splitter is not prompting so the message is
        /// passed on to DefWindowProcW.</summary>
        private IntPtr? HandleKeyDown(IntPtr hwnd, IntPtr w)
        {
            int key = w.ToInt32();
            if (_isPromptingPrintOptions)
            {
                return HandlePrintKeyDown(hwnd, key);
            }

            if (!_isPromptingVisualSplitter) return null;

            return HandleVisualSplitterKeyDown(hwnd, key);
        }

        private IntPtr? HandlePrintKeyDown(IntPtr hwnd, int key)
        {
            if (_openDropdown != PrintDropdownType.None && key == 0x1B) // Esc closes dropdown
            {
                _openDropdown = PrintDropdownType.None;
                _hoveredDropdownIndex = -1;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            if (_printIsZoomed)
            {
                if (key == 0x1B || key == 0x20 || key == 0x0D) // Esc / Space / Enter
                {
                    ClosePrintZoom(hwnd);
                }
                else if (key == 0x6B || key == 0xBB) // numpad + / =
                {
                    SetPrintZoomFactor(ZoomFactorStep(_printZoomFactor, +1), PrintZoomImgLeft + (PrintZoomImgW / 2f), PrintZoomImgTop + (PrintZoomImgH / 2f));
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                else if (key == 0x6D || key == 0xBD) // numpad - / -
                {
                    SetPrintZoomFactor(ZoomFactorStep(_printZoomFactor, -1), PrintZoomImgLeft + (PrintZoomImgW / 2f), PrintZoomImgTop + (PrintZoomImgH / 2f));
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                else if (key == 0x30 || key == 0x60) // 0 / numpad 0 → fit
                {
                    _printZoomFactor = 1f;
                    _printZoomPanX = 0f;
                    _printZoomPanY = 0f;
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                return IntPtr.Zero;
            }

            if (key == 0x20) // Space → open zoom
            {
                OpenPrintZoom(hwnd);
                return IntPtr.Zero;
            }

            return null;
        }

        private IntPtr? HandleVisualSplitterKeyDown(IntPtr hwnd, int key)
        {
            if (_visualSplitIsZoomed)
            {
                if (key == 0x1B || key == 0x20 || key == 0x0D) // Esc / Space / Enter
                {
                    CloseVisualSplitZoom(hwnd);
                }
                else if (key == 0x6B || key == 0xBB) // numpad + / =
                {
                    SetVisualSplitZoomFactor(VisualSplitModel.ZoomFactorAfter(_visualSplitZoomFactor, +1), ZoomImgLeft + ZoomImgW / 2f, ZoomImgTop + ZoomImgH / 2f);
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                else if (key == 0x6D || key == 0xBD) // numpad - / -
                {
                    SetVisualSplitZoomFactor(VisualSplitModel.ZoomFactorAfter(_visualSplitZoomFactor, -1), ZoomImgLeft + ZoomImgW / 2f, ZoomImgTop + ZoomImgH / 2f);
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                else if (key == 0x30 || key == 0x60) // 0 / numpad 0 → fit
                {
                    _visualSplitZoomFactor = VisualSplitModel.ZoomMinFactor;
                    _visualSplitZoomPanX = 0f;
                    _visualSplitZoomPanY = 0f;
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
            }
            else if (key == 0x20 || key == 0x0D) // Space / Enter → open zoom
            {
                OpenVisualSplitZoom(hwnd);
            }
            return IntPtr.Zero;
        }

        /// <summary>Colors the password edit control (dark background, white text).</summary>
        private IntPtr HandleCtlColorEdit(IntPtr w)
        {
            IntPtr editHdc = w;
            SetTextColor(editHdc, 0x00FFFFFF); // White
            SetBkColor(editHdc, 0x002D2D2D); // Edit bg (45, 45, 45)
            return _editBgBrush;
        }

        /// <summary>Dispatches button commands (no-op since dialogs handle their own commands).</summary>
        private static IntPtr HandleCommand()
        {
            return IntPtr.Zero;
        }

        /// <summary>Zooms the splitter lightbox at the cursor when zoomed, otherwise
        /// scrolls the status message.</summary>
        private IntPtr HandleMouseWheel(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
            int scrollDir = delta > 0 ? -1 : 1;
            if (_isPromptingPrintOptions)
            {
                return HandlePrintMouseWheel(hwnd, delta, scrollDir, l);
            }
            if (_isPromptingVisualSplitter && _visualSplitIsZoomed)
            {
                return HandleVisualSplitterMouseWheel(hwnd, delta, l);
            }
            return HandleDefaultScrollWheel(hwnd, scrollDir);
        }

        private IntPtr HandlePrintMouseWheel(IntPtr hwnd, int delta, int scrollDir, IntPtr l)
        {
            if (_openDropdown != PrintDropdownType.None)
            {
                int maxVisible = GetOpenDropdownMaxVisible();
                var items = GetOpenDropdownItems();
                int maxScroll = Math.Max(0, items.Count - maxVisible);
                if (maxScroll > 0)
                {
                    _dropdownScrollOffset = Math.Max(0, Math.Min(maxScroll, _dropdownScrollOffset + scrollDir));
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            if (_printIsZoomed)
            {
                int sx = (short)(l.ToInt64() & 0xFFFF);
                int sy = (short)((l.ToInt64() >> 16) & 0xFFFF);
                var pt = new Point(sx, sy);
                ScreenToClient(hwnd, ref pt);
                SetPrintZoomFactor(ZoomFactorStep(_printZoomFactor, delta > 0 ? +1 : -1), pt.X / _dpiScale, pt.Y / _dpiScale);
                InvalidateRect(hwnd, IntPtr.Zero, true);
            }
            return IntPtr.Zero;
        }

        private IntPtr HandleVisualSplitterMouseWheel(IntPtr hwnd, int delta, IntPtr l)
        {
            int sx = (short)(l.ToInt64() & 0xFFFF);
            int sy = (short)((l.ToInt64() >> 16) & 0xFFFF);
            var pt = new Point(sx, sy);
            ScreenToClient(hwnd, ref pt);
            SetVisualSplitZoomFactor(VisualSplitModel.ZoomFactorAfter(_visualSplitZoomFactor, delta > 0 ? +1 : -1), pt.X / _dpiScale, pt.Y / _dpiScale);
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return IntPtr.Zero;
        }

        private IntPtr HandleDefaultScrollWheel(IntPtr hwnd, int scrollDir)
        {
            lock (_stateLock)
            {
                if (!_completed && !_hasError)
                {
                    _scrollOffset += scrollDir * 30;
                    if (_scrollOffset < 0) _scrollOffset = 0;
                }
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return IntPtr.Zero;
        }

        /// <summary>Redraws the window, honoring the erase flag carried in wParam.</summary>
        private static IntPtr HandleInvalidate(IntPtr hwnd, IntPtr w)
        {
            InvalidateRect(hwnd, IntPtr.Zero, w != IntPtr.Zero);
            return IntPtr.Zero;
        }

        /// <summary>Drags the zoom-lightbox pan or the status-message scrollbar, and tracks
        /// hover state of the minimize-to-tray button.</summary>
        private IntPtr HandleMouseMove(IntPtr hwnd, IntPtr l)
        {
            int rawX = (short)(l.ToInt64() & 0xFFFF);
            int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
            int mouseX = (int)(rawX / _dpiScale);
            int mouseY = (int)(rawY / _dpiScale);

            if (_isPromptingPrintOptions)
            {
                return HandlePrintMouseMove(hwnd, mouseX, mouseY);
            }

            if (_isPromptingVisualSplitter && _visualSplitIsZoomed && _visualSplitZoomDragging)
            {
                return HandleVisualSplitterDrag(hwnd, mouseX, mouseY);
            }

            return HandleDefaultMouseMove(hwnd, mouseX, mouseY);
        }

        private IntPtr HandlePrintMouseMove(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (_openDropdown != PrintDropdownType.None)
            {
                return HandleDropdownMouseMove(hwnd, mouseX, mouseY);
            }

            if (_printIsZoomed && _printZoomDragging)
            {
                _printZoomPanX += mouseX - _printZoomDragLastX;
                _printZoomPanY += mouseY - _printZoomDragLastY;
                _printZoomDragLastX = mouseX;
                _printZoomDragLastY = mouseY;
                ClampPrintZoomPan();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            var elem = GetPrintElementAt(mouseX, mouseY);
            if (elem != _hoveredPrintElement)
            {
                _hoveredPrintElement = elem;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return IntPtr.Zero;
        }

        private IntPtr HandleDropdownMouseMove(IntPtr hwnd, int mouseX, int mouseY)
        {
            var popupRect = GetOpenDropdownPopupRect();
            int oldHovered = _hoveredDropdownIndex;
            if (popupRect.ContainsInclusive(mouseX, mouseY))
            {
                int listTop = popupRect.Y + 3;
                int clickedRow = (mouseY - listTop) / PrintDropdownItemHeight;
                int itemIdx = _dropdownScrollOffset + clickedRow;
                var items = GetOpenDropdownItems();
                if (itemIdx >= 0 && itemIdx < items.Count && mouseY >= listTop)
                {
                    _hoveredDropdownIndex = itemIdx;
                }
                else
                {
                    _hoveredDropdownIndex = -1;
                }
            }
            else
            {
                _hoveredDropdownIndex = -1;
            }

            if (oldHovered != _hoveredDropdownIndex)
            {
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return IntPtr.Zero;
        }

        private IntPtr HandleVisualSplitterDrag(IntPtr hwnd, int mouseX, int mouseY)
        {
            _visualSplitZoomPanX += mouseX - _visualSplitZoomDragLastX;
            _visualSplitZoomPanY += mouseY - _visualSplitZoomDragLastY;
            _visualSplitZoomDragLastX = mouseX;
            _visualSplitZoomDragLastY = mouseY;
            ClampVisualSplitZoomPan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return IntPtr.Zero;
        }

        private IntPtr HandleDefaultMouseMove(IntPtr hwnd, int mouseX, int mouseY)
        {
            lock (_stateLock)
            {
                if (!_completed && !_hasError)
                {
                    if (_isDraggingScroll)
                    {
                        UpdateScrollDrag(hwnd, mouseX);
                    }
                    else
                    {
                        bool hovered = ProgressLayout.TrayButton.ContainsInclusive(mouseX, mouseY);
                        if (hovered != _isTrayBtnHovered)
                        {
                            _isTrayBtnHovered = hovered;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Moves the status-message scrollbar thumb while it is being dragged.</summary>
        private void UpdateScrollDrag(IntPtr hwnd, int mouseX)
        {
            string statusMsg = _message;
            float logicalPctW = 0;
            string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
            if (_pctFont != null && _total > 0)
            {
                using var tempBmp = new Bitmap(1, 1);
                using var tempG = Graphics.FromImage(tempBmp);
                logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
            }
            float logicalMaxMsgW = ProgressLayout.MessageAvailableWidth(logicalPctW);

            float fullMsgW = 0f;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                if (_msgFont != null)
                {
                    fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                }
            }

            float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
            if (maxLogicalScroll > 0)
            {
                float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                float travelRange = logicalMaxMsgW - thumbW;
                if (travelRange > 0)
                {
                    float deltaX = mouseX - _dragStartMouseX;
                    float deltaOffset = (deltaX / travelRange) * maxLogicalScroll;
                    _scrollOffset = Math.Max(0f, Math.Min(_dragStartOffset + deltaOffset, maxLogicalScroll));
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
        }

        /// <summary>Routes a click to the visual splitter or the progress-area controls.</summary>
        private IntPtr HandleLButtonDown(IntPtr hwnd, IntPtr l)
        {
            int rawX = (short)(l.ToInt64() & 0xFFFF);
            int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
            int mouseX = (int)(rawX / _dpiScale);
            int mouseY = (int)(rawY / _dpiScale);

            if (_isPromptingMarkdownOptions && HandleMarkdownOptionsClick(hwnd, mouseX, mouseY))
            {
                return IntPtr.Zero;
            }
            if (_isPromptingPrintOptions)
            {
                if (_printIsZoomed && HandlePrintZoomClick(hwnd, mouseX, mouseY))
                {
                    return IntPtr.Zero;
                }
                _pressedPrintElement = GetPrintElementAt(mouseX, mouseY);
                HandlePrintOptionsClick(hwnd, mouseX, mouseY);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }
            if (_isPromptingVisualSplitter && HandleVisualSplitterClick(hwnd, mouseX, mouseY))
            {
                return IntPtr.Zero;
            }
            HandleProgressClick(hwnd, mouseX, mouseY);
            return IntPtr.Zero;
        }

        /// <summary>Handles visual-splitter clicks, dispatching to the zoom lightbox, mode
        /// bar, segment cards, page navigation or action buttons; returns true when the
        /// click hit a splitter element.</summary>
        private bool HandleVisualSplitterClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (HandleVisualSplitterZoomClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterModeBarClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterNPagesClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterSegmentCardsClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterNavBarClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterPreviewZoomClick(hwnd, mouseX, mouseY)) return true;
            if (HandleSplitterActionButtonsClick(hwnd, mouseX, mouseY)) return true;
            return false;
        }

        /// <summary>Handles zoom-lightbox clicks: zoom buttons, drag-to-pan inside the image
        /// and click-outside to close; consumes every click while the lightbox is open.</summary>
        private bool HandleVisualSplitterZoomClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (!_visualSplitIsZoomed) return false;

            float zoomBtnY = VisualSplitterLayout.ZoomButton(0).Y;
            float zoomBtnH = VisualSplitterLayout.ZoomButton(0).Height;
            if (mouseY >= zoomBtnY && mouseY <= zoomBtnY + zoomBtnH)
            {
                float btnInX = VisualSplitterLayout.ZoomButton(0).X; // Zoom out (-)
                float btnOutX = VisualSplitterLayout.ZoomButton(1).X; // Zoom in (+)
                float btnFitX = VisualSplitterLayout.ZoomButton(2).X; // Fit
                float cx = ZoomImgLeft + ZoomImgW / 2f;
                float cy = ZoomImgTop + ZoomImgH / 2f;

                if (mouseX >= btnInX && mouseX <= btnInX + VisualSplitterLayout.ZoomButton(0).Width)
                {
                    SetVisualSplitZoomFactor(VisualSplitModel.ZoomFactorAfter(_visualSplitZoomFactor, -1), cx, cy);
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                    return true;
                }
                if (mouseX >= btnOutX && mouseX <= btnOutX + VisualSplitterLayout.ZoomButton(1).Width)
                {
                    SetVisualSplitZoomFactor(VisualSplitModel.ZoomFactorAfter(_visualSplitZoomFactor, +1), cx, cy);
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                    return true;
                }
                if (mouseX >= btnFitX && mouseX <= btnFitX + VisualSplitterLayout.ZoomButton(2).Width)
                {
                    _visualSplitZoomFactor = VisualSplitModel.ZoomMinFactor;
                    _visualSplitZoomPanX = 0f;
                    _visualSplitZoomPanY = 0f;
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                    return true;
                }
            }

            if (GetVisualSplitZoomImageRect(out var zx, out var zy, out var zw, out var zh) &&
                mouseX >= zx && mouseX <= zx + zw && mouseY >= zy && mouseY <= zy + zh)
            {
                _visualSplitZoomDragging = true;
                _visualSplitZoomDragLastX = mouseX;
                _visualSplitZoomDragLastY = mouseY;
                SetCapture(hwnd);
                return true;
            }

            CloseVisualSplitZoom(hwnd);
            return true;
        }

        /// <summary>Handles clicks on the split mode switcher bar.</summary>
        private bool HandleSplitterModeBarClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (mouseY < VisualSplitterLayout.ModeHitRow.Y || mouseY > VisualSplitterLayout.ModeHitRow.Bottom) return false;

            int mode = _visualSplitMode;
            if (VisualSplitterLayout.ModeButton(0).ContainsInclusive(mouseX, mouseY)) mode = VisualSplitModel.ModeCustom;
            else if (VisualSplitterLayout.ModeButton(1).ContainsInclusive(mouseX, mouseY)) mode = VisualSplitModel.ModeEachPage;
            else if (VisualSplitterLayout.ModeButton(2).ContainsInclusive(mouseX, mouseY)) mode = VisualSplitModel.ModeFixedPages;

            _visualSplitModel.SetMode(mode);
            SyncVisualSplitStateFromModel();
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }

        /// <summary>Handles clicks on the pages-per-segment +/- selector (mode 2 only).</summary>
        private bool HandleSplitterNPagesClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (_visualSplitMode != 2 || mouseY < VisualSplitterLayout.PagesHitRow.Y || mouseY > VisualSplitterLayout.PagesHitRow.Bottom) return false;

            if (VisualSplitterLayout.PagesButton(false).ContainsInclusive(mouseX, mouseY)) // [-]
            {
                _visualSplitModel.AdjustPagesPerSegment(-1);
                SyncVisualSplitStateFromModel();
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return true;
            }
            if (VisualSplitterLayout.PagesButton(true).ContainsInclusive(mouseX, mouseY)) // [+]
            {
                _visualSplitModel.AdjustPagesPerSegment(+1);
                SyncVisualSplitStateFromModel();
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return true;
            }
            return false;
        }

        /// <summary>Handles clicks on the left segment cards list.</summary>
        private bool HandleSplitterSegmentCardsClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            int cardStartY = VisualSplitterLayout.SegmentHitArea(_visualSplitMode == 2).Y;
            if (!VisualSplitterLayout.SegmentHitArea(_visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) return false;

            int cardIdx = (mouseY - cardStartY) / 23;
            if (cardIdx >= _visualSplitSegments.Count) return false;

            _visualSplitModel.SelectSegment(cardIdx);
            SyncVisualSplitStateFromModel();
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }

        /// <summary>Handles clicks on the right-panel page navigation bar.</summary>
        private bool HandleSplitterNavBarClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (!VisualSplitterLayout.NavHitArea(_visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) return false;

            if (VisualSplitterLayout.NavHitButton(0, _visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) // <
            {
                _visualSplitModel.NavigatePreview(-1);
                SyncVisualSplitStateFromModel();
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return true;
            }
            if (VisualSplitterLayout.NavHitButton(1, _visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) // >
            {
                _visualSplitModel.NavigatePreview(+1);
                SyncVisualSplitStateFromModel();
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return true;
            }
            if (VisualSplitterLayout.NavHitButton(2, _visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) // 切開 (split at current preview page)
            {
                SplitVisualSegmentAtCurrentPage();
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return true;
            }
            return false;
        }

        /// <summary>Opens the zoom lightbox when the preview image area is clicked.</summary>
        private bool HandleSplitterPreviewZoomClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (!VisualSplitterLayout.PreviewHitArea(_visualSplitMode == 2).ContainsInclusive(mouseX, mouseY)) return false;
            OpenVisualSplitZoom(hwnd);
            return true;
        }

        /// <summary>Handles clicks on the bottom action buttons (add/delete/clear segments,
        /// confirm/cancel split).</summary>
        private bool HandleSplitterActionButtonsClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (mouseY < VisualSplitterLayout.ActionHitRow.Y || mouseY > VisualSplitterLayout.ActionHitRow.Bottom) return false;

            if (VisualSplitterLayout.ActionButton(0).ContainsInclusive(mouseX, mouseY)) // Add segment
            {
                AddVisualSplitSegment();
            }
            else if (VisualSplitterLayout.ActionButton(1).ContainsInclusive(mouseX, mouseY)) // Delete segment
            {
                DeleteVisualSplitSegment();
            }
            else if (VisualSplitterLayout.ActionButton(2).ContainsInclusive(mouseX, mouseY)) // Clear segments
            {
                ClearVisualSplitSegments();
            }
            else if (VisualSplitterLayout.ActionButton(3).ContainsInclusive(mouseX, mouseY)) // 確定分割
            {
                PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                ResizeWindowForVisualSplitter(hwnd, false);
                lock (_stateLock)
                {
                    _passwordCancelled = false;
                    _isPromptingVisualSplitter = false;
                }
                _passwordEvent.Set();
            }
            else if (VisualSplitterLayout.ActionButton(4).ContainsInclusive(mouseX, mouseY)) // 取消
            {
                PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                ResizeWindowForVisualSplitter(hwnd, false);
                lock (_stateLock)
                {
                    _passwordCancelled = true;
                    _isPromptingVisualSplitter = false;
                }
                _passwordEvent.Set();
            }
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }

        /// <summary>Handles progress-area clicks: the minimize-to-tray button and the
        /// status-message scrollbar thumb.</summary>
        private void HandleProgressClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            lock (_stateLock)
            {
                if (_completed || _hasError) return;

                if (IsTrayButtonHit(mouseX, mouseY))
                {
                    MinimizeToTray(hwnd);
                    _isTrayBtnHovered = false;
                    return;
                }

                TryStartProgressScrollDrag(hwnd, mouseX, mouseY);
            }
        }

        /// <summary>Whether the point is inside the minimize-to-tray button.</summary>
        private static bool IsTrayButtonHit(int mouseX, int mouseY)
            => ProgressLayout.TrayButton.ContainsInclusive(mouseX, mouseY);

        /// <summary>Starts dragging the status-message scrollbar when the click lands on its
        /// thumb track.</summary>
        private void TryStartProgressScrollDrag(IntPtr hwnd, int mouseX, int mouseY)
        {
            float logicalPctW = 0;
            string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
            if (_pctFont != null && _total > 0)
            {
                using var tempBmp = new Bitmap(1, 1);
                using var tempG = Graphics.FromImage(tempBmp);
                logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
            }
            float logicalMaxMsgW = ProgressLayout.MessageAvailableWidth(logicalPctW);

            if (!ProgressLayout.ScrollbarHit(mouseX, mouseY, logicalMaxMsgW)) return;

            string statusMsg = _message;
            float fullMsgW = 0f;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                if (_msgFont != null)
                {
                    fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                }
            }

            float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
            if (maxLogicalScroll <= 0) return;

            float clickX = mouseX - 36f;
            float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
            float thumbX = (_scrollOffset / fullMsgW) * logicalMaxMsgW;
            if (thumbX + thumbW > logicalMaxMsgW) thumbX = logicalMaxMsgW - thumbW;

            float travelRange = logicalMaxMsgW - thumbW;
            float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
            float newOffset = Math.Max(0f, Math.Min(relativePos * maxLogicalScroll, maxLogicalScroll));

            _scrollOffset = newOffset;
            _isDraggingScroll = true;
            _dragStartMouseX = mouseX;
            _dragStartOffset = newOffset;
            SetCapture(hwnd);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Ends zoom-lightbox or scrollbar dragging and releases the capture.</summary>
        private IntPtr HandleLButtonUp(IntPtr hwnd)
        {
            if (_isPromptingPrintOptions)
            {
                if (_printZoomDragging)
                {
                    _printZoomDragging = false;
                    ReleaseCapture();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                if (_pressedPrintElement != PrintElement.None)
                {
                    _pressedPrintElement = PrintElement.None;
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }
            if (_visualSplitZoomDragging)
            {
                _visualSplitZoomDragging = false;
                ReleaseCapture();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }
            lock (_stateLock)
            {
                if (_isDraggingScroll)
                {
                    _isDraggingScroll = false;
                    ReleaseCapture();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Redirects the title bar's minimize button (and the system menu's Minimize)
        /// to the notification area, the same place the self-drawn button sends the window.
        /// Minimizing must not just hide the window: the tray icon carries the progress
        /// tooltip, so dropping it would leave a running job with no visible indicator.
        /// Returns null for every other system command so Windows handles it itself.</summary>
        private IntPtr? HandleSysCommand(IntPtr hwnd, IntPtr w)
        {
            if ((w.ToInt64() & 0xFFF0) != 0xF020) return null; // SC_MINIMIZE

            // A finished or failed job is already on its way out and stops reporting progress,
            // so keep the ordinary minimize for those states instead of pinning a tray icon.
            lock (_stateLock)
            {
                if (_completed || _hasError) return null;
            }

            MinimizeToTray(hwnd);
            return IntPtr.Zero; // Handled: the window hides rather than entering the minimized state.
        }

        /// <summary>Handles tray icon interaction: left click/double-click restores the window,
        /// right click pops up a context menu with restore and cancel actions.</summary>
        private IntPtr HandleTrayIcon(IntPtr hwnd, IntPtr l)
        {
            long msg = l.ToInt64();
            if (msg == 0x0202 || msg == 0x0203) // WM_LBUTTONUP or WM_LBUTTONDBLCLK
            {
                ShowWindow(hwnd, 5); // SW_SHOW
                ShowWindow(hwnd, 9); // SW_RESTORE
                SetForegroundWindow(hwnd);
                RemoveTrayIcon();
            }
            else if (msg == 0x0205 || msg == 0x007B) // WM_RBUTTONUP or WM_CONTEXTMENU
            {
                SetForegroundWindow(hwnd);
                GetCursorPos(out Point pt);

                TrayPopupCommand command = ShowTrayActionMenu(
                    hwnd,
                    pt.X,
                    pt.Y,
                    Loc("cli_tray_restore"),
                    Loc("cli_tray_cancel"));
                if (command != TrayPopupCommand.Unavailable)
                {
                    PostMessageW(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
                }

                if (command == TrayPopupCommand.Restore)
                {
                    ShowWindow(hwnd, 5); // SW_SHOW
                    ShowWindow(hwnd, 9); // SW_RESTORE
                    SetForegroundWindow(hwnd);
                    RemoveTrayIcon();
                }
                else if (command == TrayPopupCommand.Cancel)
                {
                    SendMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE -> HandleClose(hwnd)
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Asks for confirmation before cancelling an in-flight job, then destroys
        /// the window.</summary>
        private IntPtr HandleClose(IntPtr hwnd)
        {
            if (_isPromptingMarkdownOptions)
            {
                ResolveMarkdownDecision(false);
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
            if (_isPromptingPrintOptions)
            {
                ResolvePrintDecision(false);
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }

            bool needsConfirmation = false;
            lock (_stateLock)
            {
                if (!_completed && !_hasError)
                {
                    needsConfirmation = true;
                }
            }

            if (needsConfirmation)
            {
                string text = Localization.T("progress_cancel_confirm", SettingsPresentationController.Shared.Language);
                if (!NativeDialogs.Confirm(hwnd, "Clickra", text, _dpiScale, SettingsPresentationController.Shared.Language))
                {
                    return IntPtr.Zero; // Ignore close
                }

                // User confirmed cancellation
                try { _cts.Cancel(); } catch { }
                lock (_stateLock)
                {
                    _passwordCancelled = true;
                }
                _passwordEvent.Set(); // Wake up background thread if blocked on password prompt
                return IntPtr.Zero; // Wait for background thread to handle cancellation and close the window
            }

            DestroyWindow(hwnd);
            return IntPtr.Zero;
        }

        /// <summary>Rebuilds the back buffer and rescales the window on DPI changes.</summary>
        private IntPtr HandleDpiChanged(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
            _dpiScale = newDpi / 96.0f;
            RecreateScaledFonts();

            int clientW = (int)(520 * _dpiScale);
            int logicalClientHeight = _isPromptingMarkdownOptions
                ? MarkdownOptionsClientHeight
                : GetStandardClientHeight();
            int clientH = (int)(logicalClientHeight * _dpiScale);

            if (_bufferBmp != null)
            {
                _bufferGraphics?.Dispose();
                _bufferBmp.Dispose();
                _bufferBmp = new Bitmap(clientW, clientH);
                _bufferGraphics = Graphics.FromImage(_bufferBmp);
                _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            }

            var rect = Marshal.PtrToStructure<RECT>(l);
            SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
            return IntPtr.Zero;
        }

        /// <summary>Advances the progress-bar animation (width easing and shimmer).</summary>
        private IntPtr HandleTimer(IntPtr hwnd)
        {
            lock (_stateLock)
            {
                if (!_completed && !_hasError && !_isPromptingPassword && !_isPromptingMarkdownOptions)
                {
                    if (_currentDispWidth < _targetWidth)
                    {
                        double diff = _targetWidth - _currentDispWidth;
                        double step = diff * 0.15;
                        if (step < 1.0) step = 1.0;

                        _currentDispWidth += step;
                        if (_currentDispWidth >= _targetWidth) _currentDispWidth = _targetWidth;
                    }

                    _shimmerOffset += 5.0f;
                    if (_shimmerOffset > 448) _shimmerOffset = -120;

                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Paints the window via the shared back buffer.</summary>
        private IntPtr HandlePaint(IntPtr hwnd)
        {
            var ps = new PAINTSTRUCT();
            var hdc = BeginPaint(hwnd, out ps);
            Paint(hdc);
            EndPaint(hwnd, ref ps);
            return IntPtr.Zero;
        }

        /// <summary>Stops the timer, frees resources and quits the message loop.</summary>
        private IntPtr HandleDestroy(IntPtr hwnd)
        {
            KillTimer(hwnd, (IntPtr)1);
            CleanupResources();
            // 程序結束前清掉殘留的 ClickraShell surrogate（dllhost），
            // 避免解除安裝時被「應用程式仍在執行」擋住。
            ClickraShellProcess.KillSurrogateHosts();
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

    }
}
