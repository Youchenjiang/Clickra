using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Clickra_Fluent;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow { get; private set; }

    /// <summary>單一實例 key：同一個 key 只允許一個「目前」實例，其餘啟動導向它。</summary>
    private const string InstanceKey = "ClickraFluentMain";

    /// <summary>目前開啟的視窗。WinUI 3 關閉最後一個視窗後不會自動結束程序，
    /// 需要自己追蹤並在最後一個視窗關閉時結束，否則解除安裝會被「應用程式仍在執行」擋住。</summary>
    private static readonly List<Window> OpenWindows = new();

    /// <summary>taskId → 轉換視窗實例（同進程），供 dashboard 任務紀錄取消/查看定位。</summary>
    private static readonly Dictionary<string, TaskProgressPage> TaskPages = new(StringComparer.OrdinalIgnoreCase);

    internal static void RegisterTaskPage(string taskId, TaskProgressPage page) => TaskPages[taskId] = page;
    internal static void UnregisterTaskPage(string taskId) => TaskPages.Remove(taskId);
    internal static TaskProgressPage? FindTaskPage(string taskId)
        => TaskPages.TryGetValue(taskId, out var page) ? page : null;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>登記一個視窗；最後一個視窗關閉時結束整個程序。</summary>
    internal static void TrackWindow(Window window)
    {
        OpenWindows.Add(window);

        // 最後一個視窗要關閉時：先攔截並隱藏視窗，在背景執行緒清掉殘留的右鍵選單
        // COM surrogate（dllhost，帶套件身分），再結束程序——否則即使程序結束，
        // 解除安裝仍會被「應用程式仍在執行」擋住。
        // 隱藏再清理是為了避免 WinUI 3 關窗時內容卸載造成的白屏閃爍
        // （microsoft-ui-xaml#7892）：若直接在 Closed 裡同步跑清理，
        // 視窗會先變白、卡住幾百毫秒才消失。
        window.AppWindow.Closing += (_, e) =>
        {
            var page = GetTaskProgressPage(window);
            if (page != null && page.IsConversionActive)
            {
                // 轉換進行中：關窗絕不中斷任務——卡在輸入 prompt 則暫存，否則縮到系統匣繼續。
                e.Cancel = true;
                window.AppWindow.Hide();
                if (page.IsWaitingOnPrompt)
                {
                    page.ParkAndClose();
                }
                else
                {
                    page.MarkBackgrounded();
                    TrayService.Instance.AddBackgroundWindow(window, page.TaskLabel);
                }
                return;
            }

            if (OpenWindows.Count > 1)
            {
                // 還有其他視窗開著，正常關閉即可，不用攔截。
                return;
            }

            // 最後一個視窗要關閉時：先攔截並隱藏視窗，在背景執行緒清掉殘留的右鍵選單
            // COM surrogate（dllhost，帶套件身分），再結束程序——否則即使程序結束，
            // 解除安裝仍會被「應用程式仍在執行」擋住。
            // 隱藏再清理是為了避免 WinUI 3 關窗時內容卸載造成的白屏閃爍
            // （microsoft-ui-xaml#7892）：若直接在 Closed 裡同步跑清理，
            // 視窗會先變白、卡住幾百毫秒才消失。
            e.Cancel = true;
            window.AppWindow.Hide();
            Task.Run(() =>
            {
                try
                {
                    ClickraShellProcess.KillSurrogateHosts();
                }
                finally
                {
                    Environment.Exit(0); // skipcq: CS-W1005 — WinUI single-instance: Application.Current.Exit() unreliable (microsoft-ui-xaml#5931)
                }
            });
        };

        window.Closed += (_, _) =>
        {
            OpenWindows.Remove(window);
            TrayService.Instance.RemoveBackgroundWindow(window);
            if (OpenWindows.Count == 0)
            {
                // 兜底路徑（正常關閉會被上面的 AppWindow.Closing 攔截）：
                // 關閉事件觸發時視窗已不存在，此時 Application.Current.Exit() 無效
                // （microsoft-ui-xaml#5931），直接用 Environment.Exit 確保程序結束。
                Environment.Exit(0); // skipcq: CS-W1005 — WinUI fallback: last window closed, must force-terminate
            }
        };
    }

    /// <summary>找出視窗內容中的 TaskProgressPage（主視窗的 Grid+Frame 或獨立任務視窗的 Frame）。</summary>
    internal static TaskProgressPage? GetTaskProgressPage(Window window)
    {
        if (window is MainWindow main && main.MainFrame.Content is TaskProgressPage page)
        {
            return page;
        }
        return (window.Content as Frame)?.Content as TaskProgressPage;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 單一實例檢查：重複啟動（含右鍵轉檔）把啟動請求導向既有實例後退出，
        // 避免累積多個 dashboard / 任務視窗。
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            try
            {
                mainInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
            }
            catch
            {
                // 導向失敗時仍以單一實例為準：直接退出，不開啟第二個視窗。
            }
            Environment.Exit(0); // skipcq: CS-W1005 — single-instance redirect failed, must not open second instance
            return;
        }

        AppInstance.GetCurrent().Activated += OnInstanceActivated;

        // 清除先前右鍵操作殘留的 ClickraShell surrogate（dllhost），避免解除安裝被擋。
        ClickraShellProcess.KillSurrogateHosts();

        string launchArguments = string.IsNullOrWhiteSpace(args.Arguments)
            ? string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(QuoteArgument))
            : args.Arguments;
        _window = new MainWindow(launchArguments);
        MainWindow = _window;
        TrackWindow(_window);
        _window.Activate();
    }

    /// <summary>收到導向過來的啟動請求（重複啟動或右鍵轉檔），交給主視窗處理。</summary>
    private static void OnInstanceActivated(object? sender, AppActivationArguments activationArgs)
    {
        string launchArguments = "";
        if (activationArgs.Data is ILaunchActivatedEventArgs launch)
        {
            launchArguments = launch.Arguments ?? "";
        }

        // unpackaged 啟動的 Arguments 第一項是執行檔路徑，剝掉非命令的前綴。
        var tokens = ConvertCommandRegistry.SplitCommandLine(launchArguments);
        if (tokens.Count > 0 && !ConvertCommandRegistry.IsKnownCommand(tokens[0]))
        {
            tokens = tokens.Skip(1).ToList();
        }
        string normalized = string.Join(" ", tokens.Select(QuoteArgument));

        // 統一模型：任何轉換請求（右鍵、重複啟動帶參數）都交給獨立的轉換視窗處理，
        // dashboard 只負責管理/選檔。
        if (App.MainWindow is MainWindow window)
        {
            // 事件可能不在 UI 執行緒上，統一丟回視窗的 dispatcher。
            window.DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    OpenTaskProgressWindow(normalized);
                }
                else
                {
                    window.Activate();
                }
            });
        }
    }

    /// <summary>開啟一個獨立的 TaskProgressPage 視窗處理導向過來的任務請求。</summary>
    internal static void OpenTaskProgressWindow(string launchArguments)
    {
        // 與 MainWindow 相同的 Fluent 外殼：Mica 背景 + 自訂標題列 + 應用程式圖示。
        // （之前只開裸 Window，內容是純色深灰、沒有 Mica，看起來像舊版 Win32。）
        var window = new Window { Title = "Clickra", SystemBackdrop = new MicaBackdrop() };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // skipcq: CS-W1091 — ms-appx:/// is the standard WinUI 3 package URI scheme for embedded assets.
        var titleBar = new TitleBar { Title = "Clickra", IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.png")) } };

        var frame = new Frame();
        Grid.SetRow(frame, 1);
        root.Children.Add(titleBar);
        root.Children.Add(frame);

        window.Content = root;
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            window.AppWindow.SetIcon(iconPath);
        }

        frame.Navigate(typeof(TaskProgressPage), launchArguments);
        if (frame.Content is TaskProgressPage taskPage)
        {
            taskPage.HostWindow = window;
        }
        TrackWindow(window);
        window.Activate();

        // 一般轉換用緊湊尺寸，內容自然填滿視窗（不會在大窗裡漂）；
        // 分割介面需要整片空間時由 TaskProgressPage 暫時放大（TaskProgressPage.PromptSplitAsync）。
        // 對「程式碼建立的裸 Window」，Resize 要在 Activate 之後才生效。
        window.AppWindow.Resize(TaskProgressPage.CompactWindowSize);
    }

    internal static string QuoteArgument(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
