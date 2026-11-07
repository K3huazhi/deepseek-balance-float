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
        private int _showCount;
        private IntPtr _hwnd;
        private bool _mica;

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
            string theme = _store.Get("theme", Theme.Auto);
            bool dark = Theme.ResolveDark(theme);
            _pal = Palette.For(dark, _mica);
            BuildUi();
            SourceInitialized += OnSourceInit;
            Loaded += OnLoaded;
            Closing += (s, e) => _stats.Save();
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

            // 鲸鱼图标：点击进入设置页
            Viewbox whale = MakeWhale(20, 15, _bSub, 0.88);
            Border whaleBtn = new Border();
            whaleBtn.Width = 26;
            whaleBtn.Height = 26;
            whaleBtn.CornerRadius = new CornerRadius(5);
            whaleBtn.Background = Brushes.Transparent;
            whaleBtn.Child = whale;
            whaleBtn.Tag = (Action)OpenSettings;
            SetInteractive(whaleBtn, true);
            whaleBtn.Cursor = Cursors.Hand;
            whaleBtn.MouseEnter += delegate { whaleBtn.Background = _bHover; };
            whaleBtn.MouseLeave += delegate { whaleBtn.Background = Brushes.Transparent; };
            whaleBtn.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenSettings();
            };
            Ui.Place(_root, whaleBtn, 15, 8);

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

            // 主界面内容收进独立 Canvas，便于与设置页做切换动画
            _mainView = new Canvas();
            _mainView.Width = WinW;
            _mainView.Height = HOpen;
            List<UIElement> moving = new List<UIElement>();
            foreach (UIElement el in _root.Children)
                if (el != _card && el != _wave) moving.Add(el);
            foreach (UIElement el in moving)
            {
                _root.Children.Remove(el);
                _mainView.Children.Add(el);
            }
            _root.Children.Insert(2, _mainView);
            BuildSettings();

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
            SetInteractive(b, true);
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
            SetInteractive(_dRefreshed, true);
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
            Native.ApplyBackdrop(_hwnd, IsDark);
            Native.SetTopmost(_hwnd, _store.GetBool("pinned", true));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestoreGeometry();
            UpdateClock();
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += delegate { OnTick(); };
            _timer.Start();
            UpdateThemeButton();
            RefreshUi();
            if (_keys.Count == 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    SetError("点左上角鲸鱼 → API Key 管理，添加一个 Key");
                    ShowKeyPage();
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
            if (_store.Get("theme", Theme.Auto) == Theme.Auto)
            {
                bool want = SunTimes.IsNight(DateTime.Now);
                if (want != IsDark) SwitchTheme(want, true, new Point(WinW / 2, Height / 2));
            }
        }

        private DateTime _nextFetch = DateTime.MinValue;

        private void UpdateClock()
        {
            _tClock.Text = DateTime.Now.ToString("HH:mm");
            if (DateTime.Now >= _nextFetch && !_busy && _keys.Count > 0) FetchAsync();
        }

        private bool IsDark { get { return _pal.Text.R > 128; } }

        // ------------------------------------------------------------ 主题
        /// 主题按钮三态轮询：亮色 → 暗色 → 自动 → 亮色
        private void ToggleTheme()
        {
            string cur = _store.Get("theme", Theme.Auto);
            string next = cur == Theme.Light ? Theme.Dark
                        : cur == Theme.Dark ? Theme.Auto
                        : Theme.Light;
            _store.Set("theme", next);
            bool dark = Theme.ResolveDark(next);
            if (dark != IsDark) SwitchTheme(dark, true, new Point(245, 20));
            else UpdateThemeButton();
        }

        /// 主题按钮图标 = 当前模式：太阳 / 月亮 / Auto
        private void UpdateThemeButton()
        {
            if (_themeBtn == null) return;
            TextBlock t = _themeBtn.Child as TextBlock;
            if (t == null) return;
            string mode = _store.Get("theme", Theme.Auto);
            if (mode == Theme.Auto)
            {
                t.Text = "Auto";
                t.FontFamily = new FontFamily(Ui.FontNum);
                t.FontSize = 8.5;
                t.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                t.Text = mode == Theme.Dark ? "\uE708" : "\uE706";
                t.FontFamily = new FontFamily(Ui.FontIcon);
                t.FontSize = 11;
                t.FontWeight = FontWeights.Normal;
            }
        }

        private void SwitchTheme(bool dark, bool animate)
        {
            SwitchTheme(dark, animate, new Point(245, 20));
        }

        private void SwitchTheme(bool dark, bool animate, Point origin)
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
            // 波纹从触发点位置扩散（点击哪个按钮就从哪里开始）
            Canvas.SetLeft(_waveCircle, origin.X - 600);
            Canvas.SetTop(_waveCircle, origin.Y - 600);
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
            UpdateThemeButton();
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
            _nextFetch = DateTime.Now.AddSeconds(Interval());
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

        /// 自动刷新间隔（秒）。界面没有暴露配置项，保持固定值，避免误配成很久才刷一次。
        private const int RefreshSeconds = 5;

        private int Interval()
        {
            return RefreshSeconds;
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
            if (_activeView == _mainView) AnimateHeight(h);   // 非主界面时不要改窗口高度
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
        /// 标记「可点击元素」，按下时不启动拖动，避免点击被拖动吃掉
        public static readonly DependencyProperty InteractiveProperty =
            DependencyProperty.RegisterAttached("Interactive", typeof(bool), typeof(MainWindow),
                new PropertyMetadata(false));

        public static void SetInteractive(UIElement el, bool value)
        {
            el.SetValue(InteractiveProperty, value);
        }

        public static bool GetInteractive(UIElement el)
        {
            return (bool)el.GetValue(InteractiveProperty);
        }

        private static bool IsInteractiveHit(DependencyObject src)
        {
            DependencyObject d = src;
            while (d != null && !(d is Window))
            {
                FrameworkElement fe = d as FrameworkElement;
                if (fe != null && GetInteractive(fe)) return true;
                Border b = d as Border;
                if (b != null && b.Tag is Action) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void OnRootDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractiveHit(e.OriginalSource as DependencyObject)) return;

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
                // 阈值 5px：手抖不会把窗口拖走，也不会吃掉按钮点击
                if (Math.Abs(left - _lastLeft) < 5 && Math.Abs(top - _lastTop) < 5) return;
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
            if (_activeView == _mainView) FetchAsync();
            e.Handled = true;
        }

        // ------------------------------------------------------- 设置页
        private Canvas _mainView;
        private Canvas _settingsView;
        private Canvas _keyView;
        private KeyPage _keyPage;
        private Canvas _activeView;
        private bool _keyFromSettings;
        private SolidColorBrush _bInput;
        private const double HSettings = HOpen;

        private Border _swAutostart, _swPin;
        private Border _segLight, _segDark, _segAuto;

        /// 诊断：验证设置页与主题切换的代码路径（--diag-ui）
        public void RunDiag()
        {
            RunDiag(false);
        }

        public void RunDiag(bool stay, bool keys)
        {
            if (keys)
            {
                ShowKeyPage();
                return;
            }
            RunDiag(stay);
        }

        public void RunDiag(bool stay)
        {
            string log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsb_diag.txt");
            Action<string> w = delegate (string s)
            {
                try { System.IO.File.AppendAllText(log, s + Environment.NewLine); } catch { }
            };
            System.Windows.Threading.DispatcherTimer tm = new System.Windows.Threading.DispatcherTimer();
            tm.Interval = TimeSpan.FromMilliseconds(600);
            int step = 0;
            tm.Tick += delegate
            {
                step++;
                switch (step)
                {
                    case 1:
                        OpenSettings();
                        w("open settings h=" + Height + " view=" + (_settingsView != null));
                        break;
                    case 3:
                        if (stay) { tm.Stop(); return; }
                        SetTheme("dark");
                        w("set dark -> " + _store.Get("theme", "?"));
                        break;
                    case 5:
                        SetTheme("auto");
                        w("set auto -> " + _store.Get("theme", "?"));
                        break;
                    case 7:
                        CloseSettings();
                        w("close settings h=" + Height);
                        break;
                    case 9:
                        tm.Stop();
                        Application.Current.Shutdown();
                        break;
                }
            };
            tm.Start();
        }

        private double MainHeight()
        {
            if (_multiView) return 54 + 46 * Math.Max(1, _showCount);
            return _expanded ? HOpen : HCollapsed;
        }

        private Canvas _fxLayer;
        private Ellipse[] _rings;

        private void BuildFx()
        {
            _fxLayer = new Canvas();
            _fxLayer.Width = WinW;
            _fxLayer.Height = HOpen;
            _fxLayer.Visibility = Visibility.Collapsed;
            _fxLayer.IsHitTestVisible = false;

            // 三圈水波纹，圆心在鲸鱼图标上
            _rings = new Ellipse[3];
            for (int i = 0; i < _rings.Length; i++)
            {
                Ellipse ring = new Ellipse();
                ring.Width = 760;
                ring.Height = 760;
                ring.StrokeThickness = 2.4;
                ring.Fill = Brushes.Transparent;
                ring.RenderTransformOrigin = new Point(0.5, 0.5);
                ring.RenderTransform = new ScaleTransform(0.01, 0.01);
                Canvas.SetLeft(ring, 28 - 380);
                Canvas.SetTop(ring, 21 - 380);
                _fxLayer.Children.Add(ring);
                _rings[i] = ring;
            }

            _root.Children.Add(_fxLayer);
        }

        private bool _busyTransition;

        /// 动画冷却：期间忽略重复进入/返回，避免波形与页面状态互相打架
        private void LockTransition(int ms)
        {
            _busyTransition = true;
            DispatcherTimer gate = new DispatcherTimer();
            gate.Interval = TimeSpan.FromMilliseconds(ms);
            gate.Tick += delegate
            {
                gate.Stop();
                _busyTransition = false;
            };
            gate.Start();
        }

        private void OpenSettings()
        {
            if (_busyTransition || _activeView == _settingsView) return;
            LockTransition(250);
            UpdateSettingsState();
            AnimateHeight(HSettings);
            PlayRipple(new Point(28, 21), delegate { SwapToView(_settingsView); });
        }

        /// 水波纹：同心圆环从鲸鱼位置扩散并淡出；中点时切换到设置页
        private void PlayRipple(Point origin, Action onMid)
        {
            _fxLayer.Visibility = Visibility.Visible;
            Color ringColor = _pal.Text;

            for (int i = 0; i < _rings.Length; i++)
            {
                Ellipse ring = _rings[i];
                Canvas.SetLeft(ring, origin.X - 380);
                Canvas.SetTop(ring, origin.Y - 380);
                ring.Stroke = Ui.Brush(ringColor);
                ScaleTransform st = ring.RenderTransform as ScaleTransform;
                if (st == null) { st = new ScaleTransform(0.01, 0.01); ring.RenderTransform = st; }
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ring.BeginAnimation(UIElement.OpacityProperty, null);
                st.ScaleX = 0.01;
                st.ScaleY = 0.01;
                ring.Opacity = 0;

                int delay = i * 95;
                TimeSpan dur = TimeSpan.FromMilliseconds(760);
                QuadraticEase ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                DoubleAnimation grow = new DoubleAnimation(0.01, 1, dur);
                grow.BeginTime = TimeSpan.FromMilliseconds(delay);
                grow.EasingFunction = ease;
                DoubleAnimation fade = new DoubleAnimation(0.6, 0, dur);
                fade.BeginTime = TimeSpan.FromMilliseconds(delay);
                fade.EasingFunction = ease;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                ring.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            DispatcherTimer mid = new DispatcherTimer();
            mid.Interval = TimeSpan.FromMilliseconds(210);
            mid.Tick += delegate
            {
                mid.Stop();
                if (onMid != null) onMid();
            };
            mid.Start();

            DispatcherTimer end = new DispatcherTimer();
            end.Interval = TimeSpan.FromMilliseconds(1100);
            end.Tick += delegate
            {
                end.Stop();
                _fxLayer.Visibility = Visibility.Collapsed;
            };
            end.Start();
        }

        /// 波纹中点：旧页淡出，新页带轻微缩放淡入
        private void SwapToView(Canvas target)
        {
            Canvas from = _activeView;
            if (from == null || from == target) return;
            from.IsHitTestVisible = false;
            DoubleAnimation outFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90));
            outFade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            outFade.Completed += delegate { from.Visibility = Visibility.Collapsed; };
            from.BeginAnimation(UIElement.OpacityProperty, outFade);

            _activeView = target;
            target.Visibility = Visibility.Visible;
            target.IsHitTestVisible = true;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform sc = new ScaleTransform(0.965, 0.965);
            target.RenderTransform = sc;
            DoubleAnimation sca = new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(260));
            sca.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, sca);
            DoubleAnimation scaY = new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(260));
            scaY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, scaY);
            DoubleAnimation inFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            inFade.BeginTime = TimeSpan.FromMilliseconds(80);
            target.BeginAnimation(UIElement.OpacityProperty, inFade);
        }

        /// 横向滑动切换（设置 ↔ API Key）
        private void SlideTo(Canvas target, bool forward)
        {
            if (target == null || target == _activeView) return;
            Canvas from = _activeView;
            _activeView = target;
            AnimateHeight(HOpen);

            target.Visibility = Visibility.Visible;
            target.IsHitTestVisible = true;
            target.BeginAnimation(UIElement.OpacityProperty, null);   // 清掉上次淡出留下的动画值
            target.Opacity = 1;
            TranslateTransform tt = new TranslateTransform(forward ? WinW : -WinW, 0);
            target.RenderTransform = tt;
            DoubleAnimation mv = new DoubleAnimation(forward ? WinW : -WinW, 0, TimeSpan.FromMilliseconds(280));
            mv.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, mv);

            if (from != null)
            {
                from.IsHitTestVisible = false;
                TranslateTransform ft = new TranslateTransform(0, 0);
                from.RenderTransform = ft;
                DoubleAnimation fm = new DoubleAnimation(0, forward ? -WinW * 0.35 : WinW * 0.35, TimeSpan.FromMilliseconds(280));
                fm.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                ft.BeginAnimation(TranslateTransform.XProperty, fm);
                DoubleAnimation ff = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                ff.Completed += delegate { from.Visibility = Visibility.Collapsed; };
                from.BeginAnimation(UIElement.OpacityProperty, ff);
            }
        }

        private void CloseSettings()
        {
            if (_busyTransition) return;
            LockTransition(250);
            if (_activeView != _settingsView) { ShowView(_mainView, MainHeight()); return; }
            AnimateHeight(MainHeight());
            PlayRipple(new Point(25, 22), delegate { SwapToView(_mainView); });
        }

        private void ShowKeyPage()
        {
            if (_busyTransition || _activeView == _keyView) return;
            LockTransition(250);
            _keyFromSettings = _activeView == _settingsView;
            SlideTo(_keyView, true);
        }

        private void CloseKeyPage()
        {
            if (_busyTransition) return;
            LockTransition(250);
            if (_keyFromSettings) SlideTo(_settingsView, false);
            else SlideTo(_mainView, false);
        }

        /// 视图切换：淡出旧视图，淡入新视图，并平滑改变窗口高度
        private void ShowView(Canvas target, double height)
        {
            if (target == null || target == _activeView) return;
            Canvas from = _activeView;
            _activeView = target;

            AnimateHeight(height);
            target.Visibility = Visibility.Visible;
            target.IsHitTestVisible = true;

            TranslateTransform inTt = new TranslateTransform(0, 10);
            target.RenderTransform = inTt;
            DoubleAnimation inMove = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220));
            inMove.BeginTime = TimeSpan.FromMilliseconds(40);
            inMove.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            inTt.BeginAnimation(TranslateTransform.YProperty, inMove);
            DoubleAnimation inFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            inFade.BeginTime = TimeSpan.FromMilliseconds(40);
            target.BeginAnimation(UIElement.OpacityProperty, inFade);

            if (from != null)
            {
                from.IsHitTestVisible = false;
                TranslateTransform outTt = new TranslateTransform(0, 0);
                from.RenderTransform = outTt;
                DoubleAnimation outMove = new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(150));
                outTt.BeginAnimation(TranslateTransform.YProperty, outMove);
                DoubleAnimation outFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
                outFade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                outFade.Completed += delegate { from.Visibility = Visibility.Collapsed; };
                from.BeginAnimation(UIElement.OpacityProperty, outFade);
            }
        }

        private void BuildSettings()
        {
            _settingsView = new Canvas();
            _settingsView.Width = WinW;
            _settingsView.Height = HSettings;
            _settingsView.Visibility = Visibility.Collapsed;
            _settingsView.Opacity = 0;
            _settingsView.Background = Brushes.Transparent;

            // 返回
            Border back = new Border();
            back.Width = 26;
            back.Height = 26;
            back.CornerRadius = new CornerRadius(5);
            back.Background = Brushes.Transparent;
            back.Cursor = Cursors.Hand;
            Viewbox backWhale = MakeWhale(20, 15, _bSub, 0.88);
            back.Child = backWhale;
            back.Tag = (Action)CloseSettings;
            SetInteractive(back, true);
            back.MouseEnter += delegate { back.Background = _bHover; };
            back.MouseLeave += delegate { back.Background = Brushes.Transparent; };
            back.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                CloseSettings();
            };
            Ui.Place(_settingsView, back, 15, 8);

            TextBlock title = Ui.Text("设置", 12, _bText, Ui.FontUi, FontWeights.SemiBold);
            Ui.Place(_settingsView, title, 46, 13);

            AddSettingsDivider(46);

            // 开机自启
            AddSettingsLabel("开机自启", 60);
            _swAutostart = MakeSwitch(38, delegate
            {
                ToggleAutostart();
                UpdateSettingsState();
            });
            Ui.Place(_settingsView, _swAutostart, WinW - 18 - 38, 55);

            // 窗口置顶
            AddSettingsLabel("窗口置顶", 102);
            _swPin = MakeSwitch(38, delegate
            {
                TogglePin();
                UpdateSettingsState();
            });
            Ui.Place(_settingsView, _swPin, WinW - 18 - 38, 97);

            // 显示模式
            AddSettingsLabel("显示模式", 144);
            _segLight = MakeSegment("亮色", 118, 136, Theme.Light);
            _segDark = MakeSegment("暗色", 166, 136, Theme.Dark);
            _segAuto = MakeSegment("自动", 214, 136, Theme.Auto);

            AddSettingsDivider(176);

            // API Key 管理
            Border keyRow = new Border();
            keyRow.Width = WinW - 36;
            keyRow.Height = 36;
            keyRow.CornerRadius = new CornerRadius(6);
            keyRow.Background = Brushes.Transparent;
            keyRow.Cursor = Cursors.Hand;
            TextBlock keyLabel = Ui.Text("API Key 管理", 12, _bText);
            keyLabel.HorizontalAlignment = HorizontalAlignment.Left;
            keyLabel.VerticalAlignment = VerticalAlignment.Center;
            TextBlock keyArrow = Ui.Icon("\uE76C", 9, _bSub);
            keyArrow.HorizontalAlignment = HorizontalAlignment.Right;
            keyArrow.VerticalAlignment = VerticalAlignment.Center;
            keyArrow.Opacity = 0.7;
            DockPanel keyLine = new DockPanel();
            DockPanel.SetDock(keyArrow, Dock.Right);
            keyLine.Children.Add(keyArrow);
            keyLine.Children.Add(keyLabel);
            keyRow.Child = keyLine;
            SetInteractive(keyRow, true);
            keyRow.MouseEnter += delegate { keyRow.Background = _bHover; };
            keyRow.MouseLeave += delegate { keyRow.Background = Brushes.Transparent; };
            keyRow.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ShowKeyPage();
            };
            Ui.Place(_settingsView, keyRow, 18, 186);

            // 退出
            Border quitRow = new Border();
            quitRow.Width = WinW - 36;
            quitRow.Height = 36;
            quitRow.CornerRadius = new CornerRadius(6);
            quitRow.Background = Brushes.Transparent;
            quitRow.Cursor = Cursors.Hand;
            TextBlock quitLabel = Ui.Text("退出程序", 12,
                Ui.Brush(IsDark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C)));
            quitLabel.VerticalAlignment = VerticalAlignment.Center;
            quitRow.Child = quitLabel;
            SetInteractive(quitRow, true);
            quitRow.MouseEnter += delegate { quitRow.Background = _bHover; };
            quitRow.MouseLeave += delegate { quitRow.Background = Brushes.Transparent; };
            quitRow.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Application.Current.Shutdown();
            };
            Ui.Place(_settingsView, quitRow, 18, 226);

            TextBlock ver = Ui.Text("DeepSeek 余额悬浮窗 v2.0.0", 9.5, _bSub);
            ver.Opacity = 0.65;
            Ui.Place(_settingsView, ver, 18, 292);

            _root.Children.Add(_settingsView);

            _bInput = Ui.Brush(IsDark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0));
            _keyPage = new KeyPage(_keys,
                delegate { _stats.Save(); FetchAsync(); },
                delegate { CloseKeyPage(); },
                _bText, _bSub, _bBorder, _bHover, _bAccent, _bInput, IsDark);
            _keyView = _keyPage.View;
            _keyView.Visibility = Visibility.Collapsed;
            _keyView.Opacity = 0;
            _root.Children.Add(_keyView);

            BuildFx();
            _activeView = _mainView;
        }

        private void AddSettingsLabel(string text, double y)
        {
            TextBlock t = Ui.Text(text, 12, _bText);
            Ui.Place(_settingsView, t, 18, y);
        }

        private void AddSettingsDivider(double y)
        {
            Rectangle line = new Rectangle();
            line.Width = WinW - 36;
            line.Height = 1;
            line.Fill = _bBorder;
            Ui.Place(_settingsView, line, 18, y);
        }

        private Border MakeSwitch(double x, Action onToggle)
        {
            Border sw = new Border();
            sw.Width = 38;
            sw.Height = 20;
            sw.CornerRadius = new CornerRadius(10);
            sw.Cursor = Cursors.Hand;
            Ellipse knob = new Ellipse();
            knob.Width = 14;
            knob.Height = 14;
            knob.Fill = Brushes.White;
            knob.Margin = new Thickness(3);
            sw.Child = knob;
            sw.Tag = onToggle;
            SetInteractive(sw, true);
            sw.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onToggle();
            };
            return sw;
        }

        private void ApplySwitch(Border sw, bool on)
        {
            if (sw == null) return;
            sw.Background = on
                ? _bAccent
                : Ui.Brush(IsDark ? Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0, 0, 0));
            Ellipse knob = sw.Child as Ellipse;
            if (knob != null)
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private Border MakeSegment(string text, double x, double y, string mode)
        {
            Border b = new Border();
            b.Width = 44;
            b.Height = 24;
            b.CornerRadius = new CornerRadius(6);
            b.Cursor = Cursors.Hand;
            b.Tag = mode;
            SetInteractive(b, true);
            TextBlock t = Ui.Text(text, 11, _bSub);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Point origin = new Point(Canvas.GetLeft(b) + b.Width / 2, Canvas.GetTop(b) + b.Height / 2);
                SetTheme(mode, origin);
                UpdateSettingsState();
            };
            Ui.Place(_settingsView, b, x, y);
            return b;
        }

        private void StyleSegment(Border b, bool active)
        {
            if (b == null) return;
            TextBlock t = b.Child as TextBlock;
            b.Background = active ? _bAccent : Brushes.Transparent;
            b.BorderBrush = _bBorder;
            b.BorderThickness = active ? new Thickness(0) : new Thickness(1);
            if (t != null) t.Foreground = active ? Ui.Brush(Colors.White) : _bSub;
        }

        private void UpdateSettingsState()
        {
            if (_settingsView == null) return;
            ApplySwitch(_swAutostart, Autostart.Enabled);
            ApplySwitch(_swPin, _store.GetBool("pinned", true));
            string mode = _store.Get("theme", Theme.Auto);
            StyleSegment(_segLight, mode == Theme.Light);
            StyleSegment(_segDark, mode == Theme.Dark);
            StyleSegment(_segAuto, mode == Theme.Auto);
        }

        private void SetTheme(string mode)
        {
            SetTheme(mode, new Point(245, 20));
        }

        private void SetTheme(string mode, Point origin)
        {
            _store.Set("theme", mode);
            bool dark = Theme.ResolveDark(mode);
            if (dark != IsDark) SwitchTheme(dark, true, origin);
            UpdateThemeButton();
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

    }
}
