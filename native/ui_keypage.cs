// DeepSeek 余额悬浮窗 · 内嵌 API Key 管理页（276 × 328，与主窗口同窗口切换）
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;

namespace Dsb
{
    internal sealed class KeyPage
    {
        private const double W = 276;
        private const double H = 328;

        private readonly List<KeyItem> _keys;
        private readonly Action _changed;
        private readonly Action _back;
        private readonly SolidColorBrush _text, _sub, _border, _hover, _accent, _input;
        private readonly bool _dark;

        private Canvas _canvas;
        private TextBox _addBox;
        private TextBlock _placeholder, _hint;
        private StackPanel _rows;
        private Border _renameBar, _confirmBar;
        private TextBox _renameBox;
        private TextBlock _confirmText;
        private int _renaming = -1, _pending = -1;
        public KeyPage(List<KeyItem> keys, Action changed, Action back,
            SolidColorBrush text, SolidColorBrush sub, SolidColorBrush border,
            SolidColorBrush hover, SolidColorBrush accent, SolidColorBrush input, bool dark)
        {
            _keys = keys;
            _changed = changed;
            _back = back;
            _text = text;
            _sub = sub;
            _border = border;
            _hover = hover;
            _accent = accent;
            _input = input;
            _dark = dark;
            Build();
        }

        public Canvas View { get { return _canvas; } }

        private void Build()
        {
            _canvas = new Canvas();
            _canvas.Width = W;
            _canvas.Height = H;
            _canvas.Background = Brushes.Transparent;

            // 返回
            Border back = new Border();
            back.Width = 26;
            back.Height = 26;
            back.CornerRadius = new CornerRadius(5);
            back.Background = Brushes.Transparent;
            back.Cursor = Cursors.Hand;
            TextBlock backIcon = Ui.Icon("\uE72B", 11, _sub);
            backIcon.HorizontalAlignment = HorizontalAlignment.Center;
            backIcon.VerticalAlignment = VerticalAlignment.Center;
            back.Child = backIcon;
            back.MouseEnter += delegate { back.Background = _hover; };
            back.MouseLeave += delegate { back.Background = Brushes.Transparent; };
            back.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_back != null) _back();
            };
            MainWindow.SetInteractive(back, true);
            Ui.Place(_canvas, back, 15, 8);

            TextBlock title = Ui.Text("API Key", 12, _text, Ui.FontUi, FontWeights.SemiBold);
            Ui.Place(_canvas, title, 46, 13);

            // 输入框
            _addBox = new TextBox();
            _addBox.Width = 168;
            _addBox.Height = 32;
            _addBox.BorderThickness = new Thickness(0);
            _addBox.Background = Brushes.Transparent;
            _addBox.Foreground = _text;
            _addBox.CaretBrush = _text;
            _addBox.FontFamily = new FontFamily(Ui.FontUi);
            _addBox.FontSize = 11.5;
            _addBox.VerticalContentAlignment = VerticalAlignment.Center;
            _addBox.Padding = new Thickness(8, 0, 8, 0);
            _addBox.TextChanged += delegate { UpdatePlaceholder(); };
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
            field.Width = 168;
            field.Height = 32;
            field.CornerRadius = new CornerRadius(6);
            field.Background = _input;
            field.BorderBrush = _border;
            field.BorderThickness = new Thickness(1);
            field.Child = _addBox;
            MainWindow.SetInteractive(field, true);
            Ui.Place(_canvas, field, 18, 48);

            _placeholder = Ui.Text("右键粘贴 sk-...", 11, _sub);
            _placeholder.Opacity = 0.55;
            _placeholder.IsHitTestVisible = false;
            Ui.Place(_canvas, _placeholder, 28, 56);

            Border add = new Border();
            add.Width = 64;
            add.Height = 32;
            add.CornerRadius = new CornerRadius(6);
            add.Background = Brushes.Transparent;
            add.BorderBrush = _border;
            add.BorderThickness = new Thickness(1);
            add.Cursor = Cursors.Hand;
            add.MouseEnter += delegate { add.Background = _hover; };
            add.MouseLeave += delegate { add.Background = Brushes.Transparent; };
            add.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AddCurrent();
            };
            TextBlock addText = Ui.Text("添加", 11.5, _text);
            addText.HorizontalAlignment = HorizontalAlignment.Center;
            addText.VerticalAlignment = VerticalAlignment.Center;
            add.Child = addText;
            MainWindow.SetInteractive(add, true);
            Ui.Place(_canvas, add, 194, 48);

            _hint = Ui.Text("最多保存 10 个，最多同时显示 3 个", 9.5, _sub);
            _hint.Width = 240;
            _hint.TextTrimming = TextTrimming.CharacterEllipsis;
            Ui.Place(_canvas, _hint, 18, 86);

            Rectangle sep = new Rectangle();
            sep.Width = 240;
            sep.Height = 1;
            sep.Fill = _border;
            Ui.Place(_canvas, sep, 18, 104);

            _rows = new StackPanel();
            _rows.Width = 240;
            ScrollViewer sv = new ScrollViewer();
            sv.Width = 240;
            sv.Height = 140;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = _rows;
            sv.Background = Brushes.Transparent;
            Ui.Place(_canvas, sv, 18, 110);

            BuildRenameBar();
            BuildConfirmBar();
            RefreshRows();
            UpdatePlaceholder();
        }

        // ---------------------------------------------------------------- 行
        private void RefreshRows()
        {
            _rows.Children.Clear();
            for (int i = 0; i < _keys.Count; i++)
            {
                KeyItem k = _keys[i];
                int index = i;

                Border row = new Border();
                row.Height = 40;
                row.CornerRadius = new CornerRadius(5);
                row.Background = Brushes.Transparent;
                row.MouseEnter += delegate { row.Background = _hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

                Canvas c = new Canvas();
                c.Width = 240;
                c.Height = 40;

                TextBlock name = Ui.Text(string.IsNullOrEmpty(k.Name) ? Keys.DefaultName(k.Key) : k.Name,
                    11.5, _text, Ui.FontUi, FontWeights.SemiBold);
                name.Width = 140;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.IsHitTestVisible = true;
                name.Cursor = Cursors.Hand;
                name.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    BeginRename(index);
                };
                Ui.Place(c, name, 8, 4);

                TextBlock masked = Ui.Text(Keys.Mask(k.Key), 9.5, _sub);
                masked.Opacity = 0.75;
                masked.Width = 140;
                masked.TextTrimming = TextTrimming.CharacterEllipsis;
                Ui.Place(c, masked, 8, 22);

                // 复制 Key
                Border copy = new Border();
                copy.Width = 20;
                copy.Height = 20;
                copy.CornerRadius = new CornerRadius(4);
                copy.Background = Brushes.Transparent;
                copy.Cursor = Cursors.Hand;
                TextBlock copyIcon = Ui.Icon("\uE8C8", 9, _sub);
                copyIcon.HorizontalAlignment = HorizontalAlignment.Center;
                copyIcon.VerticalAlignment = VerticalAlignment.Center;
                copy.Child = copyIcon;
                copy.MouseEnter += delegate { copy.Background = _hover; };
                copy.MouseLeave += delegate { copy.Background = Brushes.Transparent; };
                copy.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    CopyKey(k);
                };
                MainWindow.SetInteractive(copy, true);
                Ui.Place(c, copy, 150, 10);

                // 删除
                Border del = new Border();
                del.Width = 20;
                del.Height = 20;
                del.CornerRadius = new CornerRadius(4);
                del.Background = Brushes.Transparent;
                del.Cursor = Cursors.Hand;
                TextBlock delIcon = Ui.Icon("\uE74D", 9, _sub);
                delIcon.HorizontalAlignment = HorizontalAlignment.Center;
                delIcon.VerticalAlignment = VerticalAlignment.Center;
                del.Child = delIcon;
                del.MouseEnter += delegate { del.Background = _hover; };
                del.MouseLeave += delegate { del.Background = Brushes.Transparent; };
                del.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _pending = index;
                    _confirmBar.Visibility = Visibility.Visible;
                    _hint.Visibility = Visibility.Collapsed;
                    RefreshConfirmText();
                };
                MainWindow.SetInteractive(del, true);
                Ui.Place(c, del, 176, 10);

                // 开关
                Border sw = new Border();
                sw.Width = 34;
                sw.Height = 18;
                sw.CornerRadius = new CornerRadius(9);
                sw.Cursor = Cursors.Hand;
                sw.Background = k.Show ? _accent
                    : Ui.Brush(_dark ? Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0, 0, 0));
                Ellipse knob = new Ellipse();
                knob.Width = 12;
                knob.Height = 12;
                knob.Fill = Brushes.White;
                knob.Margin = new Thickness(3);
                knob.HorizontalAlignment = k.Show ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                sw.Child = knob;
                sw.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    ToggleShow(index);
                };
                MainWindow.SetInteractive(sw, true);
                Ui.Place(c, sw, 206, 11);

                row.Child = c;
                _rows.Children.Add(row);
            }
            if (_keys.Count == 0)
            {
                TextBlock empty = Ui.Text("还没有保存的 Key", 11, _sub);
                empty.Margin = new Thickness(0, 36, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _rows.Children.Add(empty);
            }
        }

        // -------------------------------------------------------------- 改名
        private void BuildRenameBar()
        {
            _renameBar = new Border();
            _renameBar.Width = 240;
            _renameBar.Height = 32;
            _renameBar.CornerRadius = new CornerRadius(6);
            _renameBar.Background = _input;
            _renameBar.BorderBrush = _border;
            _renameBar.BorderThickness = new Thickness(1);
            _renameBar.Visibility = Visibility.Collapsed;
            MainWindow.SetInteractive(_renameBar, true);

            Canvas inner = new Canvas();
            inner.Width = 240;
            inner.Height = 32;

            TextBlock label = Ui.Text("名称", 9.5, _sub);
            Ui.Place(inner, label, 8, 11);

            _renameBox = new TextBox();
            _renameBox.Width = 120;
            _renameBox.Height = 22;
            _renameBox.BorderThickness = new Thickness(0);
            _renameBox.Background = Brushes.Transparent;
            _renameBox.Foreground = _text;
            _renameBox.CaretBrush = _text;
            _renameBox.FontFamily = new FontFamily(Ui.FontUi);
            _renameBox.FontSize = 11.5;
            _renameBox.VerticalContentAlignment = VerticalAlignment.Center;
            _renameBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
                if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
            };
            Ui.Place(inner, _renameBox, 36, 5);

            _renameBar.Child = inner;
            Ui.Place(_canvas, _renameBar, 18, 252);

            Border ok = MiniButton("确定", 158, 4, delegate { CommitRename(); });
            Border cancel = MiniButton("取消", 196, 4, delegate { CancelRename(); });
        }

        private Border MiniButton(string text, double x, double y, Action click)
        {
            Border b = new Border();
            b.Width = 36;
            b.Height = 22;
            b.CornerRadius = new CornerRadius(4);
            b.BorderBrush = _border;
            b.BorderThickness = new Thickness(1);
            b.Background = Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.Background = _hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                click();
            };
            TextBlock t = Ui.Text(text, 10, _text);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            MainWindow.SetInteractive(b, true);
            Canvas parent = _renameBar.Child as Canvas;
            if (parent != null) Ui.Place(parent, b, x, y);
            return b;
        }

        private void BeginRename(int index)
        {
            if (index < 0 || index >= _keys.Count) return;
            _renaming = index;
            _renameBox.Text = _keys[index].Name;
            _renameBar.Visibility = Visibility.Visible;
            _hint.Visibility = Visibility.Collapsed;
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
            _hint.Visibility = Visibility.Visible;
            RefreshRows();
            if (_changed != null) _changed();
        }

        private void CancelRename()
        {
            _renaming = -1;
            _renameBar.Visibility = Visibility.Collapsed;
            _hint.Visibility = Visibility.Visible;
        }

        // -------------------------------------------------------------- 删除
        private void BuildConfirmBar()
        {
            _confirmBar = new Border();
            _confirmBar.Width = 240;
            _confirmBar.Height = 30;
            _confirmBar.Visibility = Visibility.Collapsed;
            MainWindow.SetInteractive(_confirmBar, true);
            Ui.Place(_canvas, _confirmBar, 18, 288);

            Canvas inner = new Canvas();
            inner.Width = 240;
            inner.Height = 30;

            _confirmText = Ui.Text("删除这个 Key？", 10.5, _text);
            Ui.Place(inner, _confirmText, 0, 8);

            Border cancel = new Border();
            cancel.Width = 54;
            cancel.Height = 24;
            cancel.CornerRadius = new CornerRadius(4);
            cancel.BorderBrush = _border;
            cancel.BorderThickness = new Thickness(1);
            cancel.Cursor = Cursors.Hand;
            cancel.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _pending = -1;
                _confirmBar.Visibility = Visibility.Collapsed;
                _hint.Visibility = Visibility.Visible;
            };
            TextBlock ct = Ui.Text("取消", 10, _text);
            ct.HorizontalAlignment = HorizontalAlignment.Center;
            ct.VerticalAlignment = VerticalAlignment.Center;
            cancel.Child = ct;
            MainWindow.SetInteractive(cancel, true);
            Ui.Place(inner, cancel, 122, 3);

            Border ok = new Border();
            ok.Width = 54;
            ok.Height = 24;
            ok.CornerRadius = new CornerRadius(4);
            ok.Background = Ui.Brush(_dark ? Color.FromRgb(0x3A, 0x1C, 0x1C) : Color.FromRgb(0xFC, 0xE4, 0xE6));
            ok.BorderBrush = Ui.Brush(_dark ? Color.FromArgb(0x66, 0xFF, 0x99, 0xA4) : Color.FromArgb(0x44, 0xC4, 0x2B, 0x1C));
            ok.BorderThickness = new Thickness(1);
            ok.Cursor = Cursors.Hand;
            ok.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ConfirmDelete();
            };
            TextBlock ot = Ui.Text("删除", 10,
                Ui.Brush(_dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C)));
            ot.HorizontalAlignment = HorizontalAlignment.Center;
            ot.VerticalAlignment = VerticalAlignment.Center;
            ok.Child = ot;
            MainWindow.SetInteractive(ok, true);
            Ui.Place(inner, ok, 182, 3);

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
                _hint.Visibility = Visibility.Visible;
                RefreshRows();
                if (_changed != null) _changed();
            }
        }

        /// 把完整 Key 复制到剪贴板（界面线程内有限次重试，避免挂起）
        private void CopyKey(KeyItem k)
        {
            if (k == null) return;
            string text = k.Key ?? "";
            string label = string.IsNullOrEmpty(k.Name) ? "Key" : k.Name;

            bool ok = false;
            for (int i = 0; i < 5 && !ok; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    ok = true;
                }
                catch
                {
                    // 有时数据已写入但关闭剪贴板失败，以内容为准再判断
                    try { ok = Clipboard.GetText() == text; } catch { }
                    if (!ok) System.Threading.Thread.Sleep(90);
                }
            }
            SetHint(ok ? "已复制 " + label + " 到剪贴板" : "剪贴板被占用，请稍后再试", !ok);
        }

        // -------------------------------------------------------------- 操作
        private void UpdatePlaceholder()
        {
            if (_placeholder != null)
                _placeholder.Visibility = string.IsNullOrEmpty(_addBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetHint(string text, bool error)
        {
            _hint.Text = text;
            _hint.Foreground = error
                ? Ui.Brush(_dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C))
                : _sub;
        }

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
            SetHint("已添加", false);
            RefreshRows();
            if (_changed != null) _changed();
        }

        private void ToggleShow(int index)
        {
            if (index < 0 || index >= _keys.Count) return;
            KeyItem k = _keys[index];
            int shown = 0;
            foreach (KeyItem x in _keys) if (x.Show) shown++;
            if (k.Show)
            {
                if (shown <= 1) { SetHint("至少要显示一个 Key", true); return; }
                k.Show = false;
            }
            else
            {
                if (shown >= Keys.MaxShow) { SetHint("最多同时显示 3 个 Key", true); return; }
                k.Show = true;
            }
            Keys.Save(_keys);
            SetHint("最多保存 10 个，最多同时显示 3 个", false);
            RefreshRows();
            if (_changed != null) _changed();
        }

        /// 主题切换后刷新硬编码颜色
        public void RefreshTheme(bool dark)
        {
            RefreshRows();
        }
    }
}
