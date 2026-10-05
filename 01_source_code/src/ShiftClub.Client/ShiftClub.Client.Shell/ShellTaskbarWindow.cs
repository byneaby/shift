using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ShiftClub.Client.Shell;

public sealed record TaskbarAppItem(int Pid, string Title, string ProcessName, string? ExePath = null);

/// <summary>Mac-style centered dock: hover scale, RMB actions, drag reorder.</summary>
public sealed class ShellTaskbarWindow : Window
{
    private const double DockHeight = 72;
    private const double IconSize = 52;
    private const double IconGap = 6;

    private readonly Border _dock;
    private readonly Border _startBtn;
    private readonly Popup _startPopup;
    private readonly StackPanel _iconsHost;
    private readonly TextBlock _clockText;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<string> _pinnedKeys = new();
    private readonly string _pinsPath;

    private Func<IReadOnlyList<TaskbarAppItem>>? _appsProvider;
    private string _lastFingerprint = "";
    private Border? _dragChip;
    private Point _dragStart;
    private bool _dragging;
    private bool _suppressClick;

    public event Action? ShowShellRequested;
    public event Action? OpenGamesRequested;
    public event Action? OpenTaskManagerRequested;
    public event Action? CallAdminRequested;
    public event Action? LockRequested;
    public event Action? RestartPcRequested;
    public event Action? ShutdownPcRequested;
    public event Action? NvidiaRequested;

    public ShellTaskbarWindow()
    {
        Title = "SHIFT Dock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Height = DockHeight + 28;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _pinsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ShiftClub", "Client", "dock-pins.json");
        LoadPins();

        _dock = new Border
        {
            Height = DockHeight,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x6A, 0x00)),
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
            Background = new LinearGradientBrush(
                Color.FromArgb(0xE8, 0x14, 0x14, 0x16),
                Color.FromArgb(0xE0, 0x1A, 0x1A, 0x1E),
                90),
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = new ScaleTransform(1, 1)
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _startBtn = MakeStartButton();
        row.Children.Add(_startBtn);

        _iconsHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        row.Children.Add(_iconsHost);

        row.Children.Add(MakeTrayChip("\uE8BD", "Помощь", () => CallAdminRequested?.Invoke()));
        row.Children.Add(MakeTrayChip("\uE7F4", "Задачи", () => OpenTaskManagerRequested?.Invoke()));
        _clockText = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0)
        };
        row.Children.Add(_clockText);
        _dock.Child = row;

        _startPopup = new Popup
        {
            PlacementTarget = _startBtn,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            HorizontalOffset = -100,
            Child = BuildStartMenu()
        };

        Content = new Grid { Children = { _dock } };

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) =>
        {
            _clockText.Text = DateTime.Now.ToString("HH:mm");
            RefreshApps(force: false);
            // No Topmost/Layout spam — that jittered the dock.
            WindowsTaskbar.Hide();
        };

        Deactivated += (_, _) => _startPopup.IsOpen = false;
        Loaded += (_, _) =>
        {
            LayoutToScreenIfNeeded();
            ApplyTopmostQuiet();
            AnimateDockIn();
            _shownOnce = true;
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            WindowsTaskbar.Show();
        };
    }

    private bool _shownOnce;
    private double _lastLayoutW = -1;
    private double _lastLayoutTop = -1;

    public void SetAppsProvider(Func<IReadOnlyList<TaskbarAppItem>> provider) =>
        _appsProvider = provider;

    public void EnsureVisible()
    {
        if (!IsVisible)
        {
            Show();
            if (!_shownOnce)
            {
                AnimateDockIn();
                _shownOnce = true;
            }
            else
            {
                Opacity = 1;
            }

            ApplyTopmostQuiet();
            LayoutToScreenIfNeeded();
        }

        if (!_refreshTimer.IsEnabled)
            _refreshTimer.Start();

        RefreshApps(force: false);
    }

    public void HideBar()
    {
        _startPopup.IsOpen = false;
        Hide();
    }

    private void ApplyTopmostQuiet()
    {
        if (!Topmost)
            Topmost = true;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        catch { /* ignore */ }
    }

    private void LayoutToScreenIfNeeded()
    {
        var w = SystemParameters.PrimaryScreenWidth;
        var top = SystemParameters.PrimaryScreenHeight - (DockHeight + 28);
        if (Math.Abs(_lastLayoutW - w) < 0.5 && Math.Abs(_lastLayoutTop - top) < 0.5 && Math.Abs(Left) < 0.5)
            return;
        Width = w;
        Left = 0;
        Height = DockHeight + 28;
        Top = top;
        _lastLayoutW = w;
        _lastLayoutTop = top;
    }

    private void AnimateDockIn()
    {
        if (_dock.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(0.85, 0.85);
            _dock.RenderTransform = st;
        }

        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        _dock.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
    }

    private Border MakeStartButton()
    {
        var btn = new Border
        {
            Width = IconSize,
            Height = IconSize,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 0, IconGap, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Пуск",
            Child = new TextBlock
            {
                Text = "S",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Background = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0x8A, 0x2B),
                Color.FromRgb(0xFF, 0x6A, 0x00),
                45),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        AttachHoverScale(btn);
        btn.MouseLeftButtonUp += (_, _) =>
        {
            _startPopup.IsOpen = !_startPopup.IsOpen;
            if (_startPopup.IsOpen)
            {
                Topmost = true;
                Activate();
            }
        };
        return btn;
    }

    private Border MakeTrayChip(string glyph, string tip, Action onClick)
    {
        var border = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Text = glyph,
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        AttachHoverScale(border, 1.12);
        border.MouseEnter += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => onClick();
        return border;
    }

    private void RefreshApps(bool force)
    {
        if (_dragging)
            return;

        IReadOnlyList<TaskbarAppItem> running = Array.Empty<TaskbarAppItem>();
        try { running = _appsProvider?.Invoke() ?? Array.Empty<TaskbarAppItem>(); }
        catch { /* ignore */ }

        var fingerprint = string.Join("|", _pinnedKeys)
                          + "#"
                          + string.Join(",", running.Select(a => $"{a.Pid}:{a.ProcessName}"));
        if (!force && fingerprint == _lastFingerprint)
            return;
        _lastFingerprint = fingerprint;

        _iconsHost.Children.Clear();

        // Shell always first among icons
        _iconsHost.Children.Add(MakeDockIcon(new DockEntry(
            Key: "shell",
            Title: "SHIFT",
            Subtitle: "Оболочка",
            Pid: null,
            ProcessName: null,
            ExePath: null,
            IsPinned: true,
            IsShell: true,
            Launch: () => ShowShellRequested?.Invoke())));

        // Pinned (except shell which is implicit)
        foreach (var key in _pinnedKeys.Where(k => k != "shell"))
        {
            if (key == "nvidia")
            {
                var nvidiaLive = running.FirstOrDefault(r =>
                    r.ProcessName.Equals(NvidiaControlLauncher.ProcessName, StringComparison.OrdinalIgnoreCase));
                _iconsHost.Children.Add(MakeDockIcon(new DockEntry(
                    Key: "nvidia",
                    Title: "NVIDIA",
                    Subtitle: "Панель управления",
                    Pid: nvidiaLive?.Pid,
                    ProcessName: NvidiaControlLauncher.ProcessName,
                    ExePath: nvidiaLive?.ExePath ?? NvidiaControlLauncher.ResolveExecutable(),
                    IsPinned: true,
                    IsShell: false,
                    Launch: () => NvidiaControlLauncher.TryLaunch())));
                continue;
            }

            if (!key.StartsWith("proc:", StringComparison.OrdinalIgnoreCase))
                continue;
            var procName = key["proc:".Length..];
            var live = running.FirstOrDefault(r =>
                r.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
            _iconsHost.Children.Add(MakeDockIcon(new DockEntry(
                Key: key,
                Title: live?.Title ?? procName,
                Subtitle: procName,
                Pid: live?.Pid,
                ProcessName: procName,
                ExePath: live?.ExePath,
                IsPinned: true,
                IsShell: false,
                Launch: () =>
                {
                    if (live?.Pid is int pid) FocusProcess(pid);
                    else TryStartByProcessName(procName);
                })));
        }

        // Running apps not already shown
        var shown = new HashSet<string>(_pinnedKeys, StringComparer.OrdinalIgnoreCase) { "shell", "nvidia" };
        foreach (var app in running.Take(14))
        {
            var key = "proc:" + app.ProcessName;
            if (shown.Contains(key) || shown.Contains("nvidia")
                && app.ProcessName.Equals(NvidiaControlLauncher.ProcessName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_pinnedKeys.Any(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (app.ProcessName.Equals(NvidiaControlLauncher.ProcessName, StringComparison.OrdinalIgnoreCase)
                && _pinnedKeys.Contains("nvidia"))
                continue;

            shown.Add(key);
            var item = app;
            _iconsHost.Children.Add(MakeDockIcon(new DockEntry(
                Key: key,
                Title: item.Title,
                Subtitle: item.ProcessName,
                Pid: item.Pid,
                ProcessName: item.ProcessName,
                ExePath: item.ExePath,
                IsPinned: false,
                IsShell: false,
                Launch: () => FocusProcess(item.Pid))));
        }
    }

    private Border MakeDockIcon(DockEntry entry)
    {
        var accent = entry.IsShell
            ? Color.FromArgb(0x55, 0xFF, 0x6A, 0x00)
            : entry.Key == "nvidia"
                ? Color.FromArgb(0x55, 0x76, 0xB9, 0x00)
                : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);

        ImageSource? icon = null;
        if (!entry.IsShell)
        {
            if (!string.IsNullOrWhiteSpace(entry.ExePath))
                icon = ExeIconCache.Get(entry.ExePath);
            else if (entry.Pid is int pid)
                icon = ExeIconCache.GetForProcess(pid);
        }

        UIElement face;
        if (icon is not null)
        {
            face = new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 36,
                Height = 36,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            var label = entry.Title;
            if (label.Length > 10)
                label = label[..9] + "…";
            face = new TextBlock
            {
                Text = entry.IsShell ? "S" : (entry.Key == "nvidia" ? "N" : label[..Math.Min(2, label.Length)].ToUpperInvariant()),
                FontSize = entry.IsShell || entry.Key == "nvidia" ? 18 : 11,
                FontWeight = FontWeights.Bold,
                FontStyle = entry.IsShell ? FontStyles.Italic : FontStyles.Normal,
                Foreground = entry.IsShell
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x2B))
                    : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        var content = new Grid();
        content.Children.Add(face);
        if (entry.Pid is not null)
        {
            content.Children.Add(new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }

        var chip = new Border
        {
            Width = IconSize,
            Height = IconSize,
            CornerRadius = new CornerRadius(icon is not null ? 12 : 16),
            Margin = new Thickness(0, 0, IconGap, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = string.IsNullOrWhiteSpace(entry.Subtitle) ? entry.Title : $"{entry.Title}\n{entry.Subtitle}",
            Tag = entry,
            Background = new SolidColorBrush(accent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = content,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        AttachHoverScale(chip);
        AttachContextMenu(chip, entry);
        AttachDragReorder(chip, entry);

        chip.MouseLeftButtonUp += (_, e) =>
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                e.Handled = true;
                return;
            }
            entry.Launch();
        };

        return chip;
    }

    private void AttachHoverScale(Border chip, double peak = 1.22)
    {
        chip.MouseEnter += (_, _) =>
        {
            if (chip.RenderTransform is not ScaleTransform st)
                return;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(peak, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(peak, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        };
        chip.MouseLeave += (_, _) =>
        {
            if (chip.RenderTransform is not ScaleTransform st)
                return;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        };
    }

    private void AttachContextMenu(Border chip, DockEntry entry)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x6A, 0x00))
        };

        menu.Items.Add(MakeMenuCmd("Открыть / на передний план", () => entry.Launch()));
        if (entry.Pid is int pid)
        {
            menu.Items.Add(MakeMenuCmd("Свернуть", () => MinimizeProcess(pid)));
            menu.Items.Add(MakeMenuCmd("Закрыть", () => CloseProcess(pid)));
        }

        if (!entry.IsShell)
        {
            menu.Items.Add(new Separator());
            if (entry.IsPinned || _pinnedKeys.Contains(entry.Key))
                menu.Items.Add(MakeMenuCmd("Открепить", () => Unpin(entry.Key)));
            else
                menu.Items.Add(MakeMenuCmd("Закрепить в Dock", () => Pin(entry.Key)));
        }

        chip.ContextMenu = menu;
    }

    private static MenuItem MakeMenuCmd(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void AttachDragReorder(Border chip, DockEntry entry)
    {
        if (entry.IsShell)
            return;

        chip.MouseLeftButtonDown += (_, e) =>
        {
            _dragChip = chip;
            _dragStart = e.GetPosition(_iconsHost);
            _dragging = false;
            chip.CaptureMouse();
        };
        chip.MouseMove += (_, e) =>
        {
            if (_dragChip != chip || e.LeftButton != MouseButtonState.Pressed)
                return;
            var pos = e.GetPosition(_iconsHost);
            if (!_dragging && (pos - _dragStart).Length > 8)
                _dragging = true;
            if (!_dragging)
                return;

            // Find insert index under cursor
            var x = pos.X;
            var idx = 0;
            double acc = 0;
            foreach (UIElement child in _iconsHost.Children)
            {
                if (child is not FrameworkElement fe)
                    continue;
                var w = fe.ActualWidth + IconGap;
                if (x < acc + w / 2)
                    break;
                acc += w;
                idx++;
            }

            var current = _iconsHost.Children.IndexOf(chip);
            if (current >= 0 && idx != current && idx >= 0 && idx <= _iconsHost.Children.Count)
            {
                _iconsHost.Children.Remove(chip);
                if (idx > current) idx--;
                idx = Math.Clamp(idx, 0, _iconsHost.Children.Count);
                _iconsHost.Children.Insert(idx, chip);
            }
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (_dragChip == chip)
            {
                chip.ReleaseMouseCapture();
                if (_dragging)
                {
                    _suppressClick = true;
                    PersistOrderFromUi();
                }
                _dragging = false;
                _dragChip = null;
            }
        };
    }

    private void PersistOrderFromUi()
    {
        var keys = new List<string>();
        foreach (UIElement child in _iconsHost.Children)
        {
            if (child is Border { Tag: DockEntry de } && !de.IsShell)
            {
                if (de.IsPinned || _pinnedKeys.Contains(de.Key) || de.Key == "nvidia")
                    keys.Add(de.Key);
            }
        }

        // Keep unpinned running apps out of pin list; only reorder pins that are pinned.
        var pinnedOrdered = keys.Where(k => _pinnedKeys.Contains(k) || k == "nvidia").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!_pinnedKeys.Contains("nvidia") && pinnedOrdered.Contains("nvidia"))
            pinnedOrdered.Remove("nvidia");

        // If nvidia was always intended pinned — ensure it stays if it was
        if (_pinnedKeys.Contains("nvidia") && !pinnedOrdered.Contains("nvidia"))
        {
            // keep relative — already handled by UI order
        }

        _pinnedKeys.Clear();
        foreach (var k in pinnedOrdered)
            _pinnedKeys.Add(k);
        if (!_pinnedKeys.Contains("nvidia"))
            _pinnedKeys.Insert(0, "nvidia");
        SavePins();
        _lastFingerprint = "";
        RefreshApps(force: true);
    }

    private void Pin(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == "shell")
            return;
        if (!_pinnedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            _pinnedKeys.Add(key);
        SavePins();
        _lastFingerprint = "";
        RefreshApps(force: true);
    }

    private void Unpin(string key)
    {
        if (key == "shell")
            return;
        _pinnedKeys.RemoveAll(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        SavePins();
        _lastFingerprint = "";
        RefreshApps(force: true);
    }

    private void LoadPins()
    {
        try
        {
            if (File.Exists(_pinsPath))
            {
                var json = File.ReadAllText(_pinsPath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list is { Count: > 0 })
                {
                    _pinnedKeys.Clear();
                    _pinnedKeys.AddRange(list.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
                }
            }
        }
        catch { /* ignore */ }

        if (!_pinnedKeys.Contains("nvidia", StringComparer.OrdinalIgnoreCase))
            _pinnedKeys.Insert(0, "nvidia");
    }

    private void SavePins()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_pinsPath)!);
            File.WriteAllText(_pinsPath, JsonSerializer.Serialize(_pinnedKeys));
        }
        catch { /* ignore */ }
    }

    private Border BuildStartMenu()
    {
        var menu = new Border
        {
            Width = 300,
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x6A, 0x00)),
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x14, 0x14, 0x16)),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black }
        };

        var stack = new StackPanel();
        stack.Children.Add(new StackPanel
        {
            Margin = new Thickness(8, 4, 8, 12),
            Children =
            {
                new TextBlock
                {
                    Text = "SHIFT",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00))
                },
                new TextBlock
                {
                    Text = "Пуск",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x8B, 0x90)),
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        });
        stack.Children.Add(MakeMenuItem("Показать SHIFT", "Вернуться в оболочку", () =>
        {
            _startPopup.IsOpen = false;
            ShowShellRequested?.Invoke();
        }));
        stack.Children.Add(MakeMenuItem("Игры", "Каталог игр и лаунчеров", () =>
        {
            _startPopup.IsOpen = false;
            OpenGamesRequested?.Invoke();
        }));
        stack.Children.Add(MakeMenuItem("Панель NVIDIA", "Разрешение и частота Гц", () =>
        {
            _startPopup.IsOpen = false;
            NvidiaControlLauncher.TryLaunch();
        }));
        stack.Children.Add(MakeMenuItem("Диспетчер задач", "Если игра зависла", () =>
        {
            _startPopup.IsOpen = false;
            OpenTaskManagerRequested?.Invoke();
        }));
        stack.Children.Add(MakeMenuItem("Помощь", "Вызвать администратора", () =>
        {
            _startPopup.IsOpen = false;
            CallAdminRequested?.Invoke();
        }));
        stack.Children.Add(MakeMenuItem("Блок", "Заблокировать экран", () =>
        {
            _startPopup.IsOpen = false;
            LockRequested?.Invoke();
        }));
        stack.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(8, 8, 8, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF))
        });
        stack.Children.Add(MakeMenuItem("Перезагрузка", "Перезагрузить этот ПК", () =>
        {
            _startPopup.IsOpen = false;
            RestartPcRequested?.Invoke();
        }));
        stack.Children.Add(MakeMenuItem("Выключение", "Выключить этот ПК", () =>
        {
            _startPopup.IsOpen = false;
            ShutdownPcRequested?.Invoke();
        }));
        menu.Child = stack;
        return menu;
    }

    private static Border MakeMenuItem(string title, string subtitle, Action onClick)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent
        };
        border.Child = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5))
                },
                new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x8B, 0x90)),
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        };
        border.MouseEnter += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6A, 0x00));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => onClick();
        return border;
    }

    private static void FocusProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var hwnd = p.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
                return;
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }
        catch { /* ignore */ }
    }

    private static void MinimizeProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.MainWindowHandle != IntPtr.Zero)
                ShowWindow(p.MainWindowHandle, 6); // SW_MINIMIZE
        }
        catch { /* ignore */ }
    }

    private static void CloseProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.CloseMainWindow())
                p.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }
    }

    private void TryStartByProcessName(string processName)
    {
        if (processName.Equals(NvidiaControlLauncher.ProcessName, StringComparison.OrdinalIgnoreCase))
            NvidiaControlLauncher.TryLaunch();
    }

    private sealed record DockEntry(
        string Key,
        string Title,
        string Subtitle,
        int? Pid,
        string? ProcessName,
        string? ExePath,
        bool IsPinned,
        bool IsShell,
        Action Launch);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
