using System;
using Avalonia;
using Avalonia.Media;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

/// <summary>
/// Shared zoom preview controller (spec 4.6 / 5.3):
/// Provides full-screen overlay zooming and page/sheet turning for Split and Print.
/// </summary>
public sealed class ZoomPreviewViewModel : ViewModelBase
{
    private bool _isOpen;
    private IImage? _image;
    private string _title = "";
    private string _subTitle = "";
    private double _aspect = 297.0 / 210.0;
    private double _zoom = 1;
    private double _viewportWidth, _viewportHeight, _renderScaling = 1;
    private const double ZoomStep = 1.25, ZoomMax = 8;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen != value)
            {
                _isOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public IImage? Image
    {
        get => _image;
        set
        {
            _image = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            if (value is { Size: { Width: > 0, Height: > 0 } size })
            {
                Aspect = size.Height / size.Width;
            }
        }
    }

    public bool HasImage => _image is not null;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string SubTitle
    {
        get => _subTitle;
        set
        {
            _subTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSubTitle));
        }
    }

    public bool HasSubTitle => !string.IsNullOrEmpty(_subTitle);

    public double Aspect
    {
        get => _aspect;
        set
        {
            if (Math.Abs(_aspect - value) < 0.001) return;
            _aspect = value;
            RefreshSize();
        }
    }

    private bool _canPrevious;
    public bool CanPrevious
    {
        get => _canPrevious;
        set { _canPrevious = value; OnPropertyChanged(); }
    }

    private bool _canNext;
    public bool CanNext
    {
        get => _canNext;
        set { _canNext = value; OnPropertyChanged(); }
    }

    public Action? OnPrevious { get; set; }
    public Action? OnNext { get; set; }
    public Action? OnClose { get; set; }

    public void Previous() => OnPrevious?.Invoke();
    public void Next() => OnNext?.Invoke();
    public void Close()
    {
        IsOpen = false;
        OnClose?.Invoke();
    }

    private double WholePageWidth =>
        _viewportWidth <= 0 || _viewportHeight <= 0 ? 0 : Math.Min(_viewportWidth, _viewportHeight / Aspect);

    private double FitWidthZoom => WholePageWidth <= 0 ? 1 : Math.Max(1, _viewportWidth / WholePageWidth);

    public double PageWidth => Math.Max(1, WholePageWidth * _zoom);
    public double PageHeight => Math.Max(1, WholePageWidth * _zoom * Aspect);

    public string ZoomText
    {
        get
        {
            if (Math.Abs(_zoom - 1) < 0.01) return Strings.T("ui_preview_whole_page");
            if (Math.Abs(_zoom - FitWidthZoom) < 0.01) return Strings.T("ui_preview_fit_width");
            return $"{Math.Round(_zoom * 100)}%";
        }
    }

    public void ToggleFit() => SetZoom(Math.Abs(_zoom - 1) < 0.01 ? FitWidthZoom : 1);
    public void ZoomIn() => SetZoom(StepZoom(+1));
    public void ZoomOut() => SetZoom(StepZoom(-1));

    private double StepZoom(int direction)
    {
        double next = direction > 0 ? _zoom * ZoomStep : _zoom / ZoomStep;
        double fit = FitWidthZoom;
        if (direction > 0 && _zoom < fit - 0.01 && next > fit + 0.01) next = fit;
        if (direction < 0 && _zoom > fit + 0.01 && next < fit - 0.01) next = fit;
        return Math.Clamp(next, 1, ZoomMax);
    }

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 1, ZoomMax);
        RefreshSize();
    }

    public void SetViewport(double width, double height, double renderScaling)
    {
        bool wasFit = Math.Abs(_zoom - FitWidthZoom) < 0.01 && Math.Abs(_zoom - 1) >= 0.01;
        _viewportWidth = Math.Max(0, width);
        _viewportHeight = Math.Max(0, height);
        _renderScaling = renderScaling > 0 ? renderScaling : 1;
        if (wasFit) _zoom = FitWidthZoom;
        RefreshSize();
    }

    public double NeededRenderWidth => PageWidth * _renderScaling;

    public void RefreshSize()
    {
        OnPropertyChanged(nameof(PageWidth));
        OnPropertyChanged(nameof(PageHeight));
        OnPropertyChanged(nameof(ZoomText));
    }

    // Custom toolbar action (e.g. Split cut toggle)
    private bool _hasCustomAction;
    public bool HasCustomAction
    {
        get => _hasCustomAction;
        set { _hasCustomAction = value; OnPropertyChanged(); }
    }

    private string _customActionText = "";
    public string CustomActionText
    {
        get => _customActionText;
        set { _customActionText = value; OnPropertyChanged(); }
    }

    private string _customActionActiveText = "";
    public string CustomActionActiveText
    {
        get => _customActionActiveText;
        set { _customActionActiveText = value; OnPropertyChanged(); }
    }

    private string _customActionTooltip = "";
    public string CustomActionTooltip
    {
        get => _customActionTooltip;
        set { _customActionTooltip = value; OnPropertyChanged(); }
    }

    private bool _isCustomActionActive;
    public bool IsCustomActionActive
    {
        get => _isCustomActionActive;
        set { _isCustomActionActive = value; OnPropertyChanged(); }
    }

    public Action? OnCustomAction { get; set; }
    public void ExecuteCustomAction() => OnCustomAction?.Invoke();
}
