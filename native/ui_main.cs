// DeepSeek 余额悬浮窗 · WPF 界面（.NET Framework 4.8，无第三方依赖）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dsb
{
    internal sealed class Palette
    {
        public Color Text, Sub, Card, Solid, Border, Hover;

        public static Palette For(bool dark, bool mica)
        {
            Palette p = new Palette();
            if (dark)
            {
                p.Text = Color.FromRgb(0xFF, 0xFF, 0xFF);
                p.Sub = Color.FromRgb(0xBF, 0xBF, 0xBF);
                p.Solid = Color.FromRgb(0x1F, 0x1F, 0x1F);
                p.Card = mica ? Color.FromArgb(0xD9, 0x20, 0x20, 0x20) : Color.FromArgb(0xF2, 0x20, 0x20, 0x20);
                p.Border = Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF);
                p.Hover = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                p.Text = Color.FromRgb(0x1B, 0x1B, 0x1F);
                p.Sub = Color.FromRgb(0x5D, 0x5D, 0x5D);
                p.Solid = Color.FromRgb(0xF4, 0xF4, 0xF4);
                p.Card = mica ? Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xF2, 0xF3, 0xF3, 0xF3);
                p.Border = Color.FromArgb(0x14, 0, 0, 0);
                p.Hover = Color.FromArgb(0x12, 0, 0, 0);
            }
            return p;
        }
    }

    internal static class Ui
    {
        public const string FontUi = "Microsoft YaHei UI";
        public const string FontNum = "Segoe UI";
        public const string FontIcon = "Segoe Fluent Icons";

        public static SolidColorBrush Brush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            return b;
        }

        public static TextBlock Text(string content, double size, SolidColorBrush fg,
            string family = FontUi, FontWeight? weight = null)
        {
            TextBlock t = new TextBlock();
            t.Text = content;
            t.FontSize = size;
            t.Foreground = fg;
            t.FontFamily = new FontFamily(family);
            if (weight.HasValue) t.FontWeight = weight.Value;
            t.UseLayoutRounding = true;
            t.IsHitTestVisible = false;
            return t;
        }

        public static TextBlock Icon(string glyph, double size, SolidColorBrush fg)
        {
            TextBlock t = Text(glyph, size, fg, FontIcon);
            return t;
        }

        public static void Place(Canvas c, UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }

        public static void AnimateColor(SolidColorBrush brush, Color to, double ms)
        {
            if (brush == null) return;
            ColorAnimation anim = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
            anim.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        public static void Fade(UIElement e, double to, double ms, EventHandler done = null)
        {
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (done != null) a.Completed += done;
            e.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static Style MenuStyle(SolidColorBrush bg, SolidColorBrush fg,
            SolidColorBrush hover, SolidColorBrush border)
        {
            Style s = new Style(typeof(MenuItem));
            ControlTemplate tpl = new ControlTemplate(typeof(MenuItem));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(Border.SnapsToDevicePixelsProperty, true);
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(10, 5, 22, 5));
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            Trigger hl = new Trigger();
            hl.Property = MenuItem.IsHighlightedProperty;
            hl.Value = true;
            hl.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
            tpl.Triggers.Add(hl);
            Trigger dis = new Trigger();
            dis.Property = MenuItem.IsEnabledProperty;
            dis.Value = false;
            dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Bd"));
            tpl.Triggers.Add(dis);
            s.Setters.Add(new Setter(MenuItem.TemplateProperty, tpl));
            s.Setters.Add(new Setter(MenuItem.ForegroundProperty, fg));
            s.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
            s.Setters.Add(new Setter(MenuItem.FontFamilyProperty, new FontFamily(FontUi)));
            s.Setters.Add(new Setter(MenuItem.FontSizeProperty, 12.0));
            return s;
        }
    }

    // ============================================================ 主窗口
    internal sealed partial class MainWindow : Window
    {
        public const double WinW = 276;
        public const double HCollapsed = 120;
        public const double HOpen = 328;

        private readonly Store _store = new Store();
        private readonly List<KeyItem> _keys;
        private readonly Stats _stats;
        private readonly DispatcherTimer _timer = new DispatcherTimer();

        private Palette _pal;
        private SolidColorBrush _bText, _bSub, _bCard, _bBorder, _bHover, _bAccent;
        private Canvas _root, _details, _multi;
        private Border _card, _foldBtn, _themeBtn;
        private Canvas _wave;
        private Rectangle _waveFill;
        private Ellipse _waveCircle;
        private TextBlock _tTitle, _tClock, _tBal, _tSub, _tFly;
        private TextBlock _dTopped, _dGranted, _dToday, _dState, _dDays, _dUpdated, _dRefreshed;
        private Rectangle[] _bars = new Rectangle[7];
        private StackPanel _multiPanel;
        private TextBlock _mTitle;
        private double _shownValue;
        private string _symbol = "¥";
        private string _lastUpdate = "";
        private bool _expanded, _multiView, _busy;
        private int _shownCount;
        private int _showCount;
        private IntPtr _hwnd;
        private bool _mica;
        private ContextMenu _menu;

        // 拖动 / 吸附
        private const double SnapDistance = 12;
        private bool _dragging;
        private bool _dragMoved;
        private double _grabX, _grabY;
        private double _lastLeft, _lastTop;

        public MainWindow()
        {
            _store = new Store();
            _keys = Keys.Load();
            _stats = new Stats(_store);
            _mica = Native.MicaSupported;
            _expanded = _store.GetBool("expanded", false);
            string theme = _store.Get("theme", "auto");
            bool dark = theme == "auto" ? SystemDark() : theme == "dark";
            _pal = Palette.For(dark, _mica);
            BuildUi();
            SourceInitialized += OnSourceInit;
            Loaded += OnLoaded;
            Closing += (s, e) => _stats.Save();
        }

        private static bool SystemDark()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ 构建界面
        private void BuildUi()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Title = "DeepSeek 余额";
            Width = WinW;
            Height = _expanded ? HOpen : HCollapsed;
            WindowStartupLocation = WindowStartupLocation.Manual;

            _bText = Ui.Brush(_pal.Text);
            _bSub = Ui.Brush(_pal.Sub);
            _bCard = Ui.Brush(_pal.Card);
            _bBorder = Ui.Brush(_pal.Border);
            _bHover = Ui.Brush(_pal.Hover);
            _bAccent = Ui.Brush(AccentFor(0));

            _root = new Canvas();
            _root.Width = WinW;
            _root.Height = Math.Max(Height, HOpen);
            _root.Background = Brushes.Transparent;

            _card = new Border();
            _card.Width = WinW;
            _card.Height = Height;
            _card.CornerRadius = new CornerRadius(10);
            _card.Background = _bCard;
            _card.BorderBrush = _bBorder;
            _card.BorderThickness = new Thickness(1);
            _root.Children.Add(_card);

            // 鲸鱼图标
            Viewbox whale = MakeWhale(20, 15, _bSub, 0.88);
            Canvas.SetLeft(whale, 18);
            Canvas.SetTop(whale, 13);
            _root.Children.Add(whale);

            Viewbox mark = MakeWhale(64, 47, _bText, 0.05);
            Canvas.SetLeft(mark, 198);
            Canvas.SetTop(mark, 54);
            _root.Children.Add(mark);

            _tTitle = Ui.Text("DeepSeek 余额", 12, _bSub);
            Ui.Place(_root, _tTitle, 46, 13);

            _tClock = Ui.Text("", 11, _bSub, Ui.FontNum);
            _tClock.Width = 62;
            _tClock.TextAlignment = TextAlignment.Right;
            _tClock.Opacity = 0.85;
            Ui.Place(_root, _tClock, 132, 14);

            _foldBtn = MakeIconButton("\uE70D", 202, 7, delegate { ToggleExpanded(); });
            _themeBtn = MakeIconButton("\uE708", 232, 7, delegate { ToggleTheme(); });

            _tBal = Ui.Text("", 30, _bAccent, Ui.FontNum, FontWeights.SemiBold);
            Ui.Place(_root, _tBal, 18, 34);

            _tSub = Ui.Text("正在获取…", 11, _bSub);
            _tSub.Width = 240;
            _tSub.TextTrimming = TextTrimming.CharacterEllipsis;
            Ui.Place(_root, _tSub, 18, 90);

            _tFly = Ui.Text("", 13, _bAccent, Ui.FontNum, FontWeights.SemiBold);
            _tFly.Opacity = 0;
            Ui.Place(_root, _tFly, 200, 56);

            BuildDetails();
            BuildMulti();

            // 换肤波纹遮罩（卡片之上、文字之下）
            _wave = new Canvas();
            _wave.Width = WinW;
            _wave.Height = Height;
            _wave.Visibility = Visibility.Collapsed;
            _wave.IsHitTestVisible = false;
            _wave.Clip = new RectangleGeometry(new Rect(0, 0, WinW, HOpen), 10, 10);

            _waveFill = new Rectangle();
            _waveFill.Width = WinW;
            _waveFill.Height = Height;
            _wave.Children.Add(_waveFill);

            _waveCircle = new Ellipse();
            _waveCircle.Width = 1200;
            _waveCircle.Height = 1200;
            _waveCircle.RenderTransformOrigin = new Point(0.5, 0.5);
            _waveCircle.RenderTransform = new ScaleTransform(0, 0);
            Canvas.SetLeft(_waveCircle, 245 - 600);
            Canvas.SetTop(_waveCircle, 20 - 600);
            _wave.Children.Add(_waveCircle);

            _root.Children.Insert(1, _wave);

            Content = _root;
            MouseLeftButtonDown += OnRootDown;
            MouseRightButtonUp += OnRightUp;
            MouseMove += OnRootMove;
            MouseLeftButtonUp += OnRootUp;
        }

        private static Viewbox MakeWhale(double w, double h, Brush fill, double opacity)
        {
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
            p.Data = Geometry.Parse(WhalePath);
            p.Fill = fill;
            p.Stretch = Stretch.Fill;
            p.Opacity = opacity;
            Viewbox vb = new Viewbox();
            vb.Width = w;
            vb.Height = h;
            vb.Stretch = Stretch.Fill;
            vb.Child = p;
            vb.IsHitTestVisible = false;
            return vb;
        }

        private Border MakeIconButton(string glyph, double x, double y, Action click)
        {
            Border b = new Border();
            b.Width = 26;
            b.Height = 26;
            b.CornerRadius = new CornerRadius(5);
            b.Background = Brushes.Transparent;
            TextBlock t = Ui.Icon(glyph, 11, _bSub);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            b.Tag = click;
            b.MouseEnter += delegate { b.Background = _bHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                click();
            };
            Ui.Place(_root, b, x, y);
            return b;
        }

        private void BuildDetails()
        {
            _details = new Canvas();
            _details.Width = WinW;
            _details.Height = 208;
            _details.Opacity = _expanded ? 1 : 0;
            _details.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_details, 0);
            Canvas.SetTop(_details, 120);
            _root.Children.Add(_details);

            Rectangle line = new Rectangle();
            line.Width = 240;
            line.Height = 1;
            line.Fill = _bBorder;
            Ui.Place(_details, line, 18, 0);

            LabelRow("充值余额", 12, out _dTopped);
            LabelRow("赠送余额", 34, out _dGranted);
            LabelRow("今日消费", 56, out _dToday);
            LabelRow("账户状态", 78, out _dState);

            TextBlock week = Ui.Text("近 7 日消耗", 11, _bSub);
            Ui.Place(_details, week, 18, 102);

            for (int i = 0; i < 7; i++)
            {
                Rectangle r = new Rectangle();
                r.Width = 18;
                r.Fill = _bAccent;
                r.RadiusX = 2;
                r.RadiusY = 2;
                r.Height = 2;
                r.VerticalAlignment = VerticalAlignment.Bottom;
                Canvas.SetLeft(r, 18 + i * 37);
                Canvas.SetTop(r, 148);
                _details.Children.Add(r);
                _bars[i] = r;
            }

            _dDays = Ui.Text("", 11, _bSub);
            _dDays.Width = 240;
            Ui.Place(_details, _dDays, 18, 158);

            _dUpdated = Ui.Text("", 10, _bSub);
            _dUpdated.Opacity = 0.8;
            Ui.Place(_details, _dUpdated, 18, 182);

            _dRefreshed = Ui.Text("刷新", 11, _bAccent);
            _dRefreshed.Width = 40;
            _dRefreshed.TextAlignment = TextAlignment.Right;
            _dRefreshed.Cursor = Cursors.Hand;
            _dRefreshed.IsHitTestVisible = true;
            _dRefreshed.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                FetchAsync();
            };
            Ui.Place(_details, _dRefreshed, 218, 180);
        }

        private void LabelRow(string label, double y, out TextBlock value)
        {
            TextBlock l = Ui.Text(label, 11, _bSub);
            Ui.Place(_details, l, 18, y + 2);
            TextBlock v = Ui.Text("", 12, _bText, Ui.FontNum);
            v.Width = 200;
            v.TextAlignment = TextAlignment.Right;
            Ui.Place(_details, v, 58, y);
            value = v;
        }

        private void BuildMulti()
        {
            _multi = new Canvas();
            _multi.Width = WinW;
            _multi.Height = 280;
            _multi.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(_multi, 0);
            Canvas.SetTop(_multi, 44);
            _root.Children.Add(_multi);

            _multiPanel = new StackPanel();
            _multiPanel.Width = 240;
            Canvas.SetLeft(_multiPanel, 18);
            Canvas.SetTop(_multiPanel, 0);
            _multi.Children.Add(_multiPanel);

            _mTitle = Ui.Text("", 11, _bSub);
            _mTitle.Visibility = Visibility.Collapsed;
            Ui.Place(_root, _mTitle, 18, 48);
        }

        // ------------------------------------------------------------ 生命周期
        private void OnSourceInit(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource src = HwndSource.FromHwnd(_hwnd);
            if (src != null && src.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            Native.ExtendGlass(_hwnd);
            Native.ApplyBackdrop(_hwnd, _pal.Text.R < 128);
            Native.SetTopmost(_hwnd, _store.GetBool("pinned", true));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestoreGeometry();
            UpdateClock();
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += delegate { OnTick(); };
            _timer.Start();
            RefreshUi();
            if (_keys.Count == 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    SetError("右键菜单 → API Key，添加一个 Key");
                    OpenKeyManager();
                }), DispatcherPriority.Background);
            }
            else
            {
                FetchAsync();
            }
        }

        private void RestoreGeometry()
        {
            Rect primary = SystemParameters.WorkArea;
            double x, y;
            string sx = _store.Get("x", ""), sy = _store.Get("y", "");
            if (!double.TryParse(sx, NumberStyles.Any, CultureInfo.InvariantCulture, out x))
                x = primary.Right - WinW - 28;
            if (!double.TryParse(sy, NumberStyles.Any, CultureInfo.InvariantCulture, out y))
                y = 28;

            // 先落到保存位置，再按该位置所在显示器修正
            Left = x;
            Top = y;
            Rect wa = WorkAreaAt(x + WinW / 2, y + Height / 2);
            x = Math.Max(wa.Left, Math.Min(x, Math.Max(wa.Left, wa.Right - WinW)));
            y = Math.Max(wa.Top, Math.Min(y, Math.Max(wa.Top, wa.Bottom - Height)));
            Left = x;
            Top = y;
        }

        private void OnTick()
        {
            UpdateClock();
            if (_store.Get("theme", "auto") == "auto")
            {
                bool d = SystemDark();
                if (d != IsDark)
                {
                    _pal = Palette.For(d, _mica);
                    ApplyThemeInstant();
                }
            }
            int wait = 60;
            try { wait = Math.Max(5, int.Parse(_store.Get("interval", "60"))); }
            catch { wait = 60; }
            _nextFetch = DateTime.Now.AddSeconds(wait);
        }

        private DateTime _nextFetch = DateTime.MinValue;

        private void UpdateClock()
        {
            _tClock.Text = DateTime.Now.ToString("HH:mm");
            if (DateTime.Now >= _nextFetch && !_busy && _keys.Count > 0) FetchAsync();
        }

        private bool IsDark { get { return _pal.Text.R > 128; } }

        // ------------------------------------------------------------ 主题
        private void ToggleTheme()
        {
            bool toDark = !IsDark;
            _store.Set("theme", toDark ? "dark" : "light");
            SwitchTheme(toDark, true);
        }

        private void SwitchTheme(bool dark, bool animate)
        {
            Palette next = Palette.For(dark, _mica);
            Palette old = _pal;
            _pal = next;
            _bAccent.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bAccent.Color = AccentFor(_balance);

            if (!animate)
            {
                ApplyThemeInstant();
                return;
            }

            _bCard.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bCard.Color = next.Solid;

            _wave.Visibility = Visibility.Visible;
            _wave.Height = Height;
            _wave.Clip = new RectangleGeometry(new Rect(0, 0, WinW, Height), 10, 10);
            _waveFill.Height = Height;
            _waveFill.Fill = Ui.Brush(old.Solid);
            _waveCircle.Fill = Ui.Brush(next.Solid);

            ScaleTransform st = _waveCircle.RenderTransform as ScaleTransform;
            if (st == null) { st = new ScaleTransform(0, 0); _waveCircle.RenderTransform = st; }
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = 0;
            st.ScaleY = 0;

            DoubleAnimation grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(470));
            grow.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            grow.Completed += delegate
            {
                _wave.Visibility = Visibility.Collapsed;
                st.ScaleX = 0;
                st.ScaleY = 0;
                _bCard.BeginAnimation(SolidColorBrush.ColorProperty, null);
                _bCard.Color = _pal.Card;
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            Ui.AnimateColor(_bText, next.Text, 470);
            Ui.AnimateColor(_bSub, next.Sub, 470);
            Ui.AnimateColor(_bBorder, next.Border, 470);
            Ui.AnimateColor(_bHover, next.Hover, 470);

            Native.ApplyBackdrop(_hwnd, dark);
            AnimateIcon(_themeBtn, dark ? "\uE706" : "\uE708");
        }

        private void ApplyThemeInstant()
        {
            _bText.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bSub.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bBorder.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bHover.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bCard.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bText.Color = _pal.Text;
            _bSub.Color = _pal.Sub;
            _bBorder.Color = _pal.Border;
            _bHover.Color = _pal.Hover;
            _bCard.Color = _pal.Card;
            Native.ApplyBackdrop(_hwnd, IsDark);
        }

        private void AnimateIcon(Border btn, string glyph)
        {
            TextBlock t = btn.Child as TextBlock;
            if (t == null) return;
            t.Text = glyph;
            ScaleTransform st = t.RenderTransform as ScaleTransform;
            if (st == null) { st = new ScaleTransform(1, 1); t.RenderTransform = st; t.RenderTransformOrigin = new Point(0.5, 0.5); }
            DoubleAnimation a = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(260));
            a.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private Color AccentFor(double total)
        {
            bool dark = IsDark;
            if (total > 5) return dark ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x0F, 0x7B, 0x3F);
            if (total > 0.5) return dark ? Color.FromRgb(0xFC, 0xE1, 0x00) : Color.FromRgb(0x9D, 0x5D, 0x00);
            return dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C);
        }

        // ------------------------------------------------------------ 展开
        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            _store.SetBool("expanded", _expanded);
            AnimateHeight(_expanded ? HOpen : HCollapsed);
            if (_expanded)
            {
                _details.Visibility = Visibility.Visible;
                Ui.Fade(_details, 1, 200);
            }
            else
            {
                Ui.Fade(_details, 0, 200, delegate { _details.Visibility = Visibility.Collapsed; });
            }
        }

        private void AnimateHeight(double to)
        {
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(200));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(HeightProperty, a);
            _card.Height = to;
            if (_wave != null)
            {
                _wave.Height = to;
                _wave.Clip = new RectangleGeometry(new Rect(0, 0, WinW, to), 10, 10);
                _waveFill.Height = to;
            }
            _root.Height = Math.Max(to, HOpen);
        }

        // ------------------------------------------------------------ 数据
        private double _balance;

        private void FetchAsync()
        {
            if (_busy) return;
            List<KeyItem> shown = new List<KeyItem>();
            foreach (KeyItem k in _keys) if (k.Show) shown.Add(k);
            if (shown.Count == 0)
            {
                SetError("右键菜单 → API Key，添加一个 Key");
                return;
            }
            if (shown.Count > Keys.MaxShow) shown = shown.GetRange(0, Keys.MaxShow);

            _busy = true;
            string url = _store.Get("url", "https://api.deepseek.com/user/balance");
            Thread t = new Thread(delegate ()
            {
                List<Row> rows = new List<Row>();
                BalanceInfo primary = null;
                string primaryKey = null;
                foreach (KeyItem k in shown)
                {
                    try
                    {
                        BalanceInfo bi = Api.Fetch(k.Key, url);
                        KeyStat st = _stats.For(k.Key);
                        _stats.Record(st, bi.Total);
                        Row r = new Row();
                        r.Label = k.Name;
                        r.Value = bi.Symbol + bi.Total.ToString("0.00", CultureInfo.InvariantCulture);
                        r.Total = bi.Total;
                        r.Spend = _stats.Today(st) > 0
                            ? "-" + bi.Symbol + _stats.Today(st).ToString("0.00", CultureInfo.InvariantCulture)
                            : bi.Symbol + "0.00";
                        r.Ok = bi.Available;
                        rows.Add(r);
                        if (primary == null) { primary = bi; primaryKey = k.Key; }
                    }
                    catch (ApiException ax)
                    {
                        Row r = new Row();
                        r.Label = k.Name;
                        r.Value = "—";
                        r.Total = 0;
                        r.Spend = ax.Status == 401 ? "Key 无效" : ax.Message;
                        r.Ok = false;
                        rows.Add(r);
                    }
                    catch (Exception)
                    {
                        Row r = new Row();
                        r.Label = k.Name;
                        r.Value = "—";
                        r.Total = 0;
                        r.Spend = "读取失败";
                        r.Ok = false;
                        rows.Add(r);
                    }
                }
                _stats.Save();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    ApplyRows(rows, primary, primaryKey);
                }), DispatcherPriority.Background);
            });
            t.IsBackground = true;
            t.Start();
        }

        internal sealed class Row
        {
            public string Label = "";
            public string Value = "";
            public string Spend = "";
            public double Total;
            public bool Ok = true;
        }

        private void ApplyRows(List<Row> rows, BalanceInfo primary, string primaryKey)
        {
            _busy = false;
            _nextFetch = DateTime.Now.AddSeconds(Math.Max(5, Interval()));
            if (primary == null)
            {
                SetError("全部 Key 都读取失败");
                return;
            }

            _showCount = 0;
            foreach (KeyItem k in _keys) if (k.Show) _showCount++;
            bool multi = _showCount >= 2;
            if (multi != _multiView)
            {
                _multiView = multi;
                ApplyLayoutMode();
            }

            if (multi)
            {
                BuildMultiRows(rows);
            }
            else
            {
                ApplyPrimary(primary, primaryKey);
            }
            _lastUpdate = DateTime.Now.ToString("HH:mm:ss");
            if (_dUpdated != null) _dUpdated.Text = "更新于 " + _lastUpdate;
            _busy = false;
        }

        private int Interval()
        {
            try { return Math.Max(5, int.Parse(_store.Get("interval", "60"))); }
            catch { return 60; }
        }

        private void ApplyPrimary(BalanceInfo bi, string key)
        {
            double old = _balance;
            _balance = bi.Total;
            _symbol = bi.Symbol;
            _bAccent.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _bAccent.Color = AccentFor(bi.Total);

            string sub = "可用余额" + (bi.Granted > 0.0001 ? " · 赠送 " + bi.Symbol + bi.Granted.ToString("0.00", CultureInfo.InvariantCulture) : "");
            if (!bi.Available) sub = "余额不足，API 调用会被拒绝";
            _tSub.Text = sub;
            _tSub.Foreground = _bSub;

            AnimateNumber(bi.Total);
            if (old > 0 && bi.Total < old - 0.0001) PlaySpend(old - bi.Total);

            _dTopped.Text = bi.Symbol + bi.Topped.ToString("0.00", CultureInfo.InvariantCulture);
            _dGranted.Text = bi.Symbol + bi.Granted.ToString("0.00", CultureInfo.InvariantCulture);
            KeyStat st = _stats.For(key);
            double today = _stats.Today(st);
            _dToday.Text = (today > 0 ? "-" : "") + bi.Symbol + today.ToString("0.00", CultureInfo.InvariantCulture);
            _dToday.Foreground = today > 0
                ? Ui.Brush(IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C))
                : _bText;
            _dState.Text = bi.Available ? "正常" : "余额不足";
            _dState.Foreground = bi.Available
                ? Ui.Brush(IsDark ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x0F, 0x7B, 0x3F))
                : Ui.Brush(IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));

            double[] week = _stats.Week(st);
            double max = 0.01;
            foreach (double v in week) if (v > max) max = v;
            for (int i = 0; i < 7; i++)
            {
                double h = week[i] <= 0 ? 2 : Math.Max(3, week[i] / max * 32);
                _bars[i].Height = h;
                Canvas.SetTop(_bars[i], 150 - h);
            }
            double active = 0; int cnt = 0;
            foreach (double v in week) if (v > 0) { active += v; cnt++; }
            if (cnt == 0) _dDays.Text = "还没有消耗记录，攒够数据后估算可用天数";
            else
            {
                double avg = active / cnt;
                double left = avg > 0 ? bi.Total / avg : 0;
                _dDays.Text = left >= 365 ? "按近期消耗 ≈ 可用一年以上"
                    : "按近 7 日均耗 ≈ 还能用 " + Math.Max(1, (int)Math.Round(left)) + " 天";
            }
        }

        private void BuildMultiRows(List<Row> rows)
        {
            _multiPanel.Children.Clear();
            int i = 0;
            foreach (Row r in rows)
            {
                Canvas rowCanvas = new Canvas();
                rowCanvas.Width = 240;
                rowCanvas.Height = 44;

                TextBlock name = Ui.Text(r.Label, 11, _bSub);
                name.Width = 140;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Ui.Place(rowCanvas, name, 0, 2);

                TextBlock val = Ui.Text(r.Value, 18, Ui.Brush(AccentFor(r.Total)), Ui.FontNum, FontWeights.SemiBold);
                val.Width = 110;
                val.TextAlignment = TextAlignment.Right;
                Ui.Place(rowCanvas, val, 130, 0);

                TextBlock spend = Ui.Text("今日消耗  " + r.Spend, 11, _bSub);
                spend.Opacity = r.Ok ? 1 : 0.6;
                Ui.Place(rowCanvas, spend, 0, 24);

                if (!r.Ok) rowCanvas.Opacity = 0.55;
                _multiPanel.Children.Add(rowCanvas);
                i++;
            }
        }

        private void ApplyLayoutMode()
        {
            double h = _multiView ? 54 + 46 * Math.Max(1, _showCount) : (_expanded ? HOpen : HCollapsed);
            AnimateHeight(h);
            _details.Visibility = _multiView ? Visibility.Collapsed : (_expanded ? Visibility.Visible : Visibility.Collapsed);
            _multi.Visibility = _multiView ? Visibility.Visible : Visibility.Collapsed;
            _tBal.Visibility = _multiView ? Visibility.Collapsed : Visibility.Visible;
            _tSub.Visibility = _multiView ? Visibility.Collapsed : Visibility.Visible;
            _foldBtn.Visibility = _multiView ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AnimateNumber(double target)
        {
            DoubleAnimation a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(420));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            a.CurrentTimeInvalidated += delegate
            {
                double v = _shownValue;
                _shownValue = v;
                _tBal.Text = (v < 0 ? "-" : "") + _symbol + Math.Abs(v).ToString("0.00", CultureInfo.InvariantCulture);
            };
            a.Completed += delegate
            {
                _shownValue = target;
                _tBal.Text = (target < 0 ? "-" : "") + _symbol + Math.Abs(target).ToString("0.00", CultureInfo.InvariantCulture);
            };
            BeginAnimation(ShownProperty, a);
        }

        public static readonly DependencyProperty ShownProperty =
            DependencyProperty.Register("Shown", typeof(double), typeof(MainWindow),
                new PropertyMetadata(0.0));

        private void PlaySpend(double gap)
        {
            _tFly.Text = "-" + _symbol + gap.ToString("0.00", CultureInfo.InvariantCulture);
            _tFly.Opacity = 0;
            TranslateTransform tt = _tFly.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(0, 0); _tFly.RenderTransform = tt; }
            tt.Y = 0;
            DoubleAnimation up = new DoubleAnimation(0, -34, TimeSpan.FromMilliseconds(900));
            up.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty, up);
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900));
            fade.BeginTime = TimeSpan.FromMilliseconds(140);
            _tFly.BeginAnimation(OpacityProperty, fade);

            TranslateTransform shake = new TranslateTransform(0, 0);
            _tBal.RenderTransform = shake;
            DoubleAnimation sh = new DoubleAnimation(0, 4, TimeSpan.FromMilliseconds(60)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
            shake.BeginAnimation(TranslateTransform.XProperty, sh);
        }

        private void SetError(string text)
        {
            _busy = false;
            _tSub.Text = text;
            _tSub.Visibility = Visibility.Visible;
            _tSub.Foreground = Ui.Brush(IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));
        }

        private void RefreshUi()
        {
            _tBal.Text = "";
            _tSub.Text = "正在获取…";
        }

        // ------------------------------------------------------------ 交互
        private void OnRootDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            Border border = e.OriginalSource as Border;
            if (border != null && border.Tag is Action) return;
            TextBlock tb = e.OriginalSource as TextBlock;
            if (tb != null && tb.IsHitTestVisible) return;

            Point c = CursorDip();
            _grabX = c.X - Left;
            _grabY = c.Y - Top;
            _lastLeft = Left;
            _lastTop = Top;
            _dragMoved = false;
            _dragging = true;
            CaptureMouse();
        }

        private void OnRootMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point c = CursorDip();
            double left = c.X - _grabX;
            double top = c.Y - _grabY;

            if (!_dragMoved)
            {
                if (Math.Abs(left - _lastLeft) < 1 && Math.Abs(top - _lastTop) < 1) return;
                _dragMoved = true;
            }
            ApplyPosition(left, top);
        }

        private void OnRootUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                if (_dragMoved) SavePosition();
                _dragMoved = false;
            }
            _stats.Save();
        }

        private void SavePosition()
        {
            _store.Set("x", Left.ToString("0", CultureInfo.InvariantCulture));
            _store.Set("y", Top.ToString("0", CultureInfo.InvariantCulture));
        }

        /// 光标位置（DIP，屏幕坐标）
        private Point CursorDip()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return new Point(Left, Top);
            PresentationSource src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return new Point(p.X, p.Y);
            Matrix m = src.CompositionTarget.TransformFromDevice;
            return m.Transform(new Point(p.X, p.Y));
        }

        /// 指定 DIP 点所在显示器的工作区（DIP）
        private Rect WorkAreaAt(double dipX, double dipY)
        {
            PresentationSource src = PresentationSource.FromVisual(this);
            Matrix toDev = src != null && src.CompositionTarget != null
                ? src.CompositionTarget.TransformToDevice : Matrix.Identity;
            Point px = toDev.Transform(new Point(dipX, dipY));
            Native.RECT work;
            if (!Native.TryWorkArea((int)Math.Round(px.X), (int)Math.Round(px.Y), out work))
                return SystemParameters.WorkArea;
            Matrix fromDev = src != null && src.CompositionTarget != null
                ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            Point tl = fromDev.Transform(new Point(work.Left, work.Top));
            Point br = fromDev.Transform(new Point(work.Right, work.Bottom));
            return new Rect(tl, br);
        }

        /// 贴近屏幕边缘时吸附，并限制在工作区内
        private void ApplyPosition(double left, double top)
        {
            Rect wa = WorkAreaAt(left + Width / 2, top + Height / 2);

            if (Math.Abs(left - wa.Left) <= SnapDistance) left = wa.Left;
            else if (Math.Abs(left + Width - wa.Right) <= SnapDistance) left = wa.Right - Width;

            if (Math.Abs(top - wa.Top) <= SnapDistance) top = wa.Top;
            else if (Math.Abs(top + Height - wa.Bottom) <= SnapDistance) top = wa.Bottom - Height;

            left = Math.Max(wa.Left, Math.Min(left, Math.Max(wa.Left, wa.Right - Width)));
            top = Math.Max(wa.Top, Math.Min(top, Math.Max(wa.Top, wa.Bottom - Height)));

            Left = left;
            Top = top;
        }

        private void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            ShowMenu();
            e.Handled = true;
        }

        private Window _menuWin;

        private void CloseMenu()
        {
            if (_menuWin != null)
            {
                Window w = _menuWin;
                _menuWin = null;
                try { w.Close(); } catch { }
            }
        }

        private void ShowMenu()
        {
            CloseMenu();

            bool dark = IsDark;
            SolidColorBrush bg = Ui.Brush(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFA, 0xFA, 0xFA));
            SolidColorBrush fg = Ui.Brush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1F));
            SolidColorBrush hover = Ui.Brush(dark ? Color.FromRgb(0x3C, 0x3C, 0x3C) : Color.FromRgb(0xEC, 0xEC, 0xEC));
            SolidColorBrush border = Ui.Brush(dark ? Color.FromRgb(0x46, 0x46, 0x46) : Color.FromRgb(0xE1, 0xE1, 0xE1));

            StackPanel items = new StackPanel();
            items.Width = 168;

            string cur = _store.Get("theme", "auto");
            bool pinned = _store.GetBool("pinned", true);
            bool auto = Autostart.Enabled;

            AddMenuRow(items, fg, hover, "立即刷新", delegate { FetchAsync(); });
            AddMenuRow(items, fg, hover, "API Key 管理…", delegate { OpenKeyManager(); });
            AddMenuSep(items, border);
            AddMenuRow(items, fg, hover, (cur == "light" ? "●  " : "     ") + "亮色", delegate { SetTheme("light"); });
            AddMenuRow(items, fg, hover, (cur == "dark" ? "●  " : "     ") + "暗色", delegate { SetTheme("dark"); });
            AddMenuRow(items, fg, hover, (cur == "auto" ? "●  " : "     ") + "跟随系统", delegate { SetTheme("auto"); });
            AddMenuSep(items, border);
            AddMenuRow(items, fg, hover, pinned ? "取消置顶" : "置顶显示", delegate { TogglePin(); });
            AddMenuRow(items, fg, hover, (auto ? "✓  " : "     ") + "开机自启", delegate { ToggleAutostart(); });
            AddMenuSep(items, border);
            AddMenuRow(items, fg, hover, "退出", delegate { Close(); Application.Current.Shutdown(); });

            Border card = new Border();
            card.CornerRadius = new CornerRadius(8);
            card.Background = bg;
            card.BorderBrush = border;
            card.BorderThickness = new Thickness(1);
            card.Padding = new Thickness(4);
            card.Child = items;

            Window w = new Window();
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.Topmost = true;
            w.SizeToContent = SizeToContent.WidthAndHeight;
            w.Content = card;

            Point p = Mouse.GetPosition(this);
            double sw = SystemParameters.WorkArea.Width;
            double sh = SystemParameters.WorkArea.Height;
            double top = Top + p.Y;
            if (top + 300 > sh) top = Math.Max(8, sh - 300);
            w.Left = Math.Min(Left + p.X, sw - 190);
            w.Top = top;

            w.Deactivated += delegate { CloseMenu(); };
            _menuWin = w;
            w.Show();
            w.Activate();
        }

        private void AddMenuRow(Panel host, Brush fg, Brush hover, string text, Action action)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(5);
            row.Background = Brushes.Transparent;
            row.Padding = new Thickness(10, 5, 12, 5);
            TextBlock t = Ui.Text(text, 12, (SolidColorBrush)fg);
            row.Child = t;
            row.MouseEnter += delegate { row.Background = hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                CloseMenu();
                action();
            };
            host.Children.Add(row);
        }

        private void AddMenuSep(Panel host, Brush brush)
        {
            Border line = new Border();
            line.Height = 1;
            line.Margin = new Thickness(8, 4, 8, 4);
            line.Background = brush;
            host.Children.Add(line);
        }

        private void SetTheme(string mode)
        {
            _store.Set("theme", mode);
            bool dark = mode == "auto" ? SystemDark() : mode == "dark";
            if (dark != IsDark) SwitchTheme(dark, true);
        }

        private void TogglePin()
        {
            bool pinned = !_store.GetBool("pinned", true);
            _store.SetBool("pinned", pinned);
            Native.SetTopmost(_hwnd, pinned);
        }

        private void ToggleAutostart()
        {
            string err;
            Autostart.Toggle(out err);
            if (!string.IsNullOrEmpty(err))
                Native.Error("设置开机自启失败：\n\n" + err);
        }

        private void OpenKeyManager()
        {
            KeyWindow w = new KeyWindow(_keys, _stats, delegate
            {
                _stats.Save();
                _showCount = 0;
                foreach (KeyItem k in _keys) if (k.Show) _showCount++;
                bool multi = _showCount >= 2;
                if (multi != _multiView) { _multiView = multi; ApplyLayoutMode(); }
                FetchAsync();
            });
            w.Owner = this;
            w.ShowDialog();
        }
    }
}
