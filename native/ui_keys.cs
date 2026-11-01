// DeepSeek 余额悬浮窗 · API Key 管理窗口
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Dsb
{
    internal sealed class KeyWindow : Window
    {
        private const double W = 372;
        private const double H = 458;

        private readonly List<KeyItem> _keys;
        private readonly Action _changed;
        private readonly Palette _pal;
        private readonly bool _mica = Native.MicaSupported;

        private SolidColorBrush _bText, _bSub, _bCard, _bBorder, _bHover, _bInput, _bAccent;
        private Canvas _root;
        private Border _card;
        private TextBox _addBox;
        private TextBlock _err;
        private StackPanel _rows;
        private Border _renameBar, _confirmBar;
        private TextBox _renameBox;
        private int _renaming = -1, _pending = -1;
        private IntPtr _hwnd;

        public KeyWindow(List<KeyItem> keys, Stats stats, Action changed)
        {
            _keys = keys;
            _changed = changed;
            Store store = new Store();
            string theme = store.Get("theme", "auto");
            bool dark = theme == "auto" ? SystemDark() : theme == "dark";
            _pal = Palette.For(dark, _mica);
            Build();
            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                HwndSource src = HwndSource.FromHwnd(_hwnd);
                if (src != null && src.CompositionTarget != null)
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;
                Native.ApplyBackdrop(_hwnd, dark);
            };
            Loaded += delegate { CenterOnOwner(); };
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

        private void CenterOnOwner()
        {
            if (Owner == null) return;
            Left = Owner.Left + (Owner.Width - W) / 2;
            Top = Owner.Top + (Owner.Height - H) / 2;
            double sw = SystemParameters.WorkArea.Width, sh = SystemParameters.WorkArea.Height;
            Left = Math.Max(8, Math.Min(Left, sw - W - 8));
            Top = Math.Max(8, Math.Min(Top, sh - H - 8));
        }

        private void Build()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Width = W;
            Height = H;
            Title = "DeepSeek API Key";

            _bText = Ui.Brush(_pal.Text);
            _bSub = Ui.Brush(_pal.Sub);
            _bCard = Ui.Brush(_pal.Card);
            _bBorder = Ui.Brush(_pal.Border);
            _bHover = Ui.Brush(_pal.Hover);
            _bInput = Ui.Brush(IsDarkNow() ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0));
            _bAccent = Ui.Brush(IsDarkNow() ? Color.FromRgb(0x5B, 0x76, 0xFF) : Color.FromRgb(0x4D, 0x6B, 0xFE));

            _root = new Canvas();
            _root.Width = W;
            _root.Height = H;
            _root.Background = Brushes.Transparent;

            _card = new Border();
            _card.Width = W;
            _card.Height = H;
            _card.CornerRadius = new CornerRadius(10);
            _card.Background = _bCard;
            _card.BorderBrush = _bBorder;
            _card.BorderThickness = new Thickness(1);
            _root.Children.Add(_card);

            _card.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource is TextBox || e.OriginalSource is TextBlock) return;
                try { DragMove(); } catch { }
            };

            TextBlock title = Ui.Text("API Key", 12, _bText, Ui.FontUi, FontWeights.SemiBold);
            Ui.Place(_root, title, 18, 14);

            Border close = IconButton("\uE711", W - 18 - 26, 7, delegate { Close(); });
            close.Child = Ui.Icon("\uE711", 11, _bSub);

            // 输入框
            _addBox = new TextBox();
            _addBox.Width = W - 36 - 64 - 8;
            _addBox.Height = 34;
            _addBox.BorderThickness = new Thickness(0);
            _addBox.Background = Brushes.Transparent;
            _addBox.Foreground = _bText;
            _addBox.CaretBrush = _bText;
            _addBox.FontFamily = new FontFamily(Ui.FontUi);
            _addBox.FontSize = 12;
            _addBox.VerticalContentAlignment = VerticalAlignment.Center;
            _addBox.Padding = new Thickness(8, 0, 8, 0);
            _addBox.TextChanged += delegate { UpdateHint(); };
            _addBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { AddCurrent(); e.Handled = true; }
            };
            _addBox.PreviewMouseRightButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                string clip = "";
                try { if (Clipboard.ContainsText()) clip = Clipboard.GetText().Trim(); }
                catch { }
                if (clip.Length > 0)
                {
                    _addBox.Text = clip;
                    _addBox.CaretIndex = clip.Length;
                    e.Handled = true;
                }
            };

            Border field = new Border();
            field.Width = _addBox.Width;
            field.Height = 34;
            field.CornerRadius = new CornerRadius(6);
            field.Background = _bInput;
            field.BorderBrush = _bBorder;
            field.BorderThickness = new Thickness(1);
            field.Child = _addBox;
            Ui.Place(_root, field, 18, 44);

            TextBlock placeholder = Ui.Text("右键粘贴 sk-...", 12, _bSub);
            placeholder.Opacity = 0.55;
            placeholder.IsHitTestVisible = false;
            Ui.Place(_root, placeholder, 29, 52);
            _placeholder = placeholder;

            Border add = TextButton("添加", 18 + field.Width + 8, 44, 64, 34, delegate { AddCurrent(); });
            add.Child = Ui.Text("添加", 12, _bText);

            TextBlock hint = Ui.Text("右键输入框直接粘贴 · 最多保存 10 个，最多同时显示 3 个", 10, _bSub);
            Ui.Place(_root, hint, 18, 84);

            Rectangle sep = new Rectangle();
            sep.Width = W - 36;
            sep.Height = 1;
            sep.Fill = _bBorder;
            Ui.Place(_root, sep, 18, 102);

            _rows = new StackPanel();
            _rows.Width = W - 36;
            ScrollViewer sv = new ScrollViewer();
            sv.Width = W - 36;
            sv.Height = 236;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = _rows;
            sv.Background = Brushes.Transparent;
            Ui.Place(_root, sv, 18, 110);

            _err = Ui.Text("", 10, _bSub);
            _err.Width = W - 36;
            _err.TextTrimming = TextTrimming.CharacterEllipsis;
            Ui.Place(_root, _err, 18, 354);

            BuildRenameBar();
            BuildConfirmBar();

            Border done = TextButton("完成", W - 18 - 72, 418, 72, 28, delegate { Close(); });
            done.Child = Ui.Text("完成", 12, _bText);

            Content = _root;
            RefreshRows();
            UpdateHint();
            SetHint("Key 存在 Windows 凭据管理器，界面只显示首尾，中间 6 位隐藏");
        }

        private TextBlock _placeholder;

        private bool IsDarkNow() { return _pal.Text.R > 128; }

        private Border IconButton(string glyph, double x, double y, Action click)
        {
            Border b = new Border();
            b.Width = 26;
            b.Height = 26;
            b.CornerRadius = new CornerRadius(5);
            b.Background = Brushes.Transparent;
            b.MouseEnter += delegate { b.Background = _bHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Ui.Place(_root, b, x, y);
            return b;
        }

        private Border TextButton(string text, double x, double y, double w, double h, Action click)
        {
            Border b = new Border();
            b.Width = w;
            b.Height = h;
            b.CornerRadius = new CornerRadius(6);
            b.Background = Brushes.Transparent;
            b.BorderBrush = _bBorder;
            b.BorderThickness = new Thickness(1);
            b.MouseEnter += delegate { b.Background = _bHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Ui.Place(_root, b, x, y);
            return b;
        }

        private void UpdateHint()
        {
            if (_placeholder != null)
                _placeholder.Visibility = string.IsNullOrEmpty(_addBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetHint(string text, bool error = false)
        {
            _err.Text = text;
            _err.Foreground = error
                ? Ui.Brush(IsDarkNow() ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C))
                : _bSub;
        }

        // ------------------------------------------------------------ 行
        private void RefreshRows()
        {
            _rows.Children.Clear();
            for (int i = 0; i < _keys.Count; i++)
            {
                KeyItem k = _keys[i];
                int index = i;

                Border row = new Border();
                row.Height = 46;
                row.CornerRadius = new CornerRadius(5);
                row.Background = Brushes.Transparent;
                row.Margin = new Thickness(0, 1, 0, 1);
                row.MouseEnter += delegate { row.Background = _bHover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

                Canvas c = new Canvas();
                c.Width = W - 36;
                c.Height = 46;

                TextBlock name = Ui.Text(string.IsNullOrEmpty(k.Name) ? Keys.DefaultName(k.Key) : k.Name,
                    12, _bText, Ui.FontUi, FontWeights.SemiBold);
                name.Width = c.Width - 128;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.IsHitTestVisible = true;
                name.Cursor = Cursors.Hand;
                name.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    BeginRename(index);
                };
                Ui.Place(c, name, 8, 7);

                TextBlock masked = Ui.Text(Keys.Mask(k.Key), 10, _bSub);
                masked.Opacity = 0.75;
                masked.Width = c.Width - 128;
                masked.TextTrimming = TextTrimming.CharacterEllipsis;
                Ui.Place(c, masked, 8, 26);

                // 删除（开关左侧）
                Border del = new Border();
                del.Width = 24;
                del.Height = 24;
                del.CornerRadius = new CornerRadius(5);
                del.Background = Brushes.Transparent;
                del.Cursor = Cursors.Hand;
                TextBlock delIcon = Ui.Icon("\uE74D", 10, _bSub);
                delIcon.HorizontalAlignment = HorizontalAlignment.Center;
                delIcon.VerticalAlignment = VerticalAlignment.Center;
                del.Child = delIcon;
                del.MouseEnter += delegate { del.Background = _bHover; delIcon.Foreground = Ui.Brush(IsDarkNow() ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C)); };
                del.MouseLeave += delegate { del.Background = Brushes.Transparent; delIcon.Foreground = _bSub; };
                del.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _pending = index;
                    _confirmBar.Visibility = Visibility.Visible;
                    _err.Visibility = Visibility.Collapsed;
                    RefreshConfirmText();
                };
                Ui.Place(c, del, c.Width - 6 - 38 - 10 - 24, 11);

                // 拨动开关
                Border sw = new Border();
                sw.Width = 38;
                sw.Height = 20;
                sw.CornerRadius = new CornerRadius(10);
                sw.Cursor = Cursors.Hand;
                sw.Background = k.Show ? _bAccent : Ui.Brush(IsDarkNow() ? Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0, 0, 0));
                Ellipse knob = new Ellipse();
                knob.Width = 14;
                knob.Height = 14;
                knob.Fill = Brushes.White;
                knob.HorizontalAlignment = k.Show ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Margin = new Thickness(3);
                sw.Child = knob;
                sw.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    ToggleShow(index);
                };
                Ui.Place(c, sw, c.Width - 6 - 38, 13);

                row.Child = c;
                _rows.Children.Add(row);
            }
            if (_keys.Count == 0)
            {
                TextBlock empty = Ui.Text("还没有保存的 Key", 11, _bSub);
                empty.Margin = new Thickness(0, 80, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _rows.Children.Add(empty);
            }
        }

        // ------------------------------------------------------------ 改名条
        private void BuildRenameBar()
        {
            _renameBar = new Border();
            _renameBar.Width = W - 36;
            _renameBar.Height = 34;
            _renameBar.CornerRadius = new CornerRadius(6);
            _renameBar.Background = _bInput;
            _renameBar.BorderBrush = _bBorder;
            _renameBar.BorderThickness = new Thickness(1);
            _renameBar.Visibility = Visibility.Collapsed;
            Ui.Place(_root, _renameBar, 18, 350);

            Canvas inner = new Canvas();
            inner.Width = W - 36;
            inner.Height = 34;

            TextBlock label = Ui.Text("名称", 10, _bSub);
            Ui.Place(inner, label, 10, 11);

            _renameBox = new TextBox();
            _renameBox.Width = W - 36 - 42 - 8 - 52 - 52 - 10;
            _renameBox.Height = 24;
            _renameBox.BorderThickness = new Thickness(0);
            _renameBox.Background = Brushes.Transparent;
            _renameBox.Foreground = _bText;
            _renameBox.CaretBrush = _bText;
            _renameBox.FontFamily = new FontFamily(Ui.FontUi);
            _renameBox.FontSize = 12;
            _renameBox.VerticalContentAlignment = VerticalAlignment.Center;
            _renameBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
                if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
            };
            Ui.Place(inner, _renameBox, 42, 5);

            Border ok = SmallButton("确定", inner.Width - 6 - 46 - 6 - 46, 5, delegate { CommitRename(); });
            Border cancel = SmallButton("取消", inner.Width - 6 - 46, 5, delegate { CancelRename(); });

            _renameBar.Child = inner;
        }

        private Border SmallButton(string text, double x, double y, Action click)
        {
            Border b = new Border();
            b.Width = 46;
            b.Height = 24;
            b.CornerRadius = new CornerRadius(5);
            b.BorderBrush = _bBorder;
            b.BorderThickness = new Thickness(1);
            b.Background = Brushes.Transparent;
            b.MouseEnter += delegate { b.Background = _bHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Canvas parent = _renameBar.Child as Canvas;
            TextBlock t = Ui.Text(text, 11, _bText);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            if (parent != null) Ui.Place(parent, b, x, y);
            return b;
        }

        private void BeginRename(int index)
        {
            if (index < 0 || index >= _keys.Count) return;
            _renaming = index;
            _renameBox.Text = _keys[index].Name;
            _renameBar.Visibility = Visibility.Visible;
            _err.Visibility = Visibility.Collapsed;
            _renameBox.Focus();
            _renameBox.SelectAll();
        }

        private void CommitRename()
        {
            if (_renaming < 0 || _renaming >= _keys.Count) { CancelRename(); return; }
            string t = (_renameBox.Text ?? "").Trim();
            _keys[_renaming].Name = t.Length == 0 ? Keys.DefaultName(_keys[_renaming].Key) : t;
            Keys.Save(_keys);
            _renaming = -1;
            _renameBar.Visibility = Visibility.Collapsed;
            _err.Visibility = Visibility.Visible;
            RefreshRows();
            if (_changed != null) _changed();
        }

        private void CancelRename()
        {
            _renaming = -1;
            _renameBar.Visibility = Visibility.Collapsed;
            _err.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------ 删除确认
        private TextBlock _confirmText;

        private void BuildConfirmBar()
        {
            _confirmBar = new Border();
            _confirmBar.Width = W - 36;
            _confirmBar.Height = 30;
            _confirmBar.Visibility = Visibility.Collapsed;
            Ui.Place(_root, _confirmBar, 18, 382);

            Canvas inner = new Canvas();
            inner.Width = W - 36;
            inner.Height = 30;

            _confirmText = Ui.Text("删除这个 Key？", 11, _bText);
            Ui.Place(inner, _confirmText, 0, 7);

            Border cancel = new Border();
            cancel.Width = 58; cancel.Height = 26;
            cancel.CornerRadius = new CornerRadius(5);
            cancel.BorderBrush = _bBorder;
            cancel.BorderThickness = new Thickness(1);
            cancel.Cursor = Cursors.Hand;
            cancel.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _pending = -1;
                _confirmBar.Visibility = Visibility.Collapsed;
                _err.Visibility = Visibility.Visible;
            };
            TextBlock ct = Ui.Text("取消", 11, _bText);
            ct.HorizontalAlignment = HorizontalAlignment.Center;
            ct.VerticalAlignment = VerticalAlignment.Center;
            cancel.Child = ct;
            Ui.Place(inner, cancel, inner.Width - 58 - 8 - 58, 2);

            Border ok = new Border();
            ok.Width = 58; ok.Height = 26;
            ok.CornerRadius = new CornerRadius(5);
            ok.Background = Ui.Brush(IsDarkNow() ? Color.FromRgb(0x3A, 0x1C, 0x1C) : Color.FromRgb(0xFC, 0xE4, 0xE6));
            ok.BorderBrush = Ui.Brush(IsDarkNow() ? Color.FromArgb(0x66, 0xFF, 0x99, 0xA4) : Color.FromArgb(0x44, 0xC4, 0x2B, 0x1C));
            ok.BorderThickness = new Thickness(1);
            ok.Cursor = Cursors.Hand;
            ok.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ConfirmDelete();
            };
            TextBlock ot = Ui.Text("删除", 11, Ui.Brush(IsDarkNow() ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C)));
            ot.HorizontalAlignment = HorizontalAlignment.Center;
            ot.VerticalAlignment = VerticalAlignment.Center;
            ok.Child = ot;
            Ui.Place(inner, ok, inner.Width - 58, 2);

            _confirmBar.Child = inner;
        }

        private void RefreshConfirmText()
        {
            if (_pending >= 0 && _pending < _keys.Count)
                _confirmText.Text = "删除「" + (_keys[_pending].Name ?? "Key") + "」？";
        }

        private void ConfirmDelete()
        {
            if (_pending >= 0 && _pending < _keys.Count)
            {
                _keys.RemoveAt(_pending);
                Keys.Save(_keys);
                _pending = -1;
                _confirmBar.Visibility = Visibility.Collapsed;
                _err.Visibility = Visibility.Visible;
                RefreshRows();
                if (_changed != null) _changed();
            }
        }

        // ------------------------------------------------------------ 操作
        private void AddCurrent()
        {
            string key = (_addBox.Text ?? "").Trim();
            if (key.Length == 0) { SetHint("先粘贴一个 Key", true); return; }
            if (!key.StartsWith("sk-", StringComparison.Ordinal) || key.Length < 20)
            { SetHint("Key 格式不对，应以 sk- 开头", true); return; }
            foreach (KeyItem k in _keys)
                if (k.Key == key) { SetHint("这个 Key 已经保存过了", true); return; }
            if (_keys.Count >= Keys.MaxKeys) { SetHint("最多保存 10 个 Key", true); return; }

            KeyItem item = new KeyItem();
            item.Key = key;
            item.Name = Keys.DefaultName(key);
            item.Show = _keys.Count == 0;
            _keys.Add(item);
            Keys.Save(_keys);
            _addBox.Text = "";
            SetHint("已添加");
            RefreshRows();
            if (_changed != null) _changed();
        }

        private void ToggleShow(int index)
        {
            if (index < 0 || index >= _keys.Count) return;
            KeyItem k = _keys[index];
            if (k.Show)
            {
                int shown = 0;
                foreach (KeyItem x in _keys) if (x.Show) shown++;
                if (shown <= 1) { SetHint("至少要显示一个 Key", true); return; }
                k.Show = false;
            }
            else
            {
                int shown = 0;
                foreach (KeyItem x in _keys) if (x.Show) shown++;
                if (shown >= Keys.MaxShow) { SetHint("最多同时显示 3 个 Key", true); return; }
                k.Show = true;
            }
            Keys.Save(_keys);
            SetHint("Key 存在 Windows 凭据管理器，界面只显示首尾，中间 6 位隐藏");
            RefreshRows();
            if (_changed != null) _changed();
        }
    }
}
