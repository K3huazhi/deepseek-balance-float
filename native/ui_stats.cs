// DeepSeek 余额悬浮窗 · 消耗记录页（7 天 × 24 小时热力图，按 Key 分别统计）
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Dsb
{
    internal sealed class StatsPage
    {
        private const double W = 276;
        private const double H = 328;
        private const double GridX = 64;
        private const double CellW = 7;
        private const double CellH = 11;
        private const double ColStep = 8;
        private const double RowStep = 14;
        private const double GridTop = 108;

        private readonly List<KeyItem> _keys;
        private readonly Stats _stats;
        private readonly Action _back;
        private readonly SolidColorBrush _text, _sub, _border, _hover, _control;
        private readonly bool _dark;

        private Canvas _canvas;
        private StackPanel _chips;
        private Canvas _grid;
        private TextBlock _summary;
        private Canvas _periods;
        private readonly Border[] _bars = new Border[4];
        private readonly TextBlock[] _amounts = new TextBlock[4];

        private int _selected;

        public StatsPage(List<KeyItem> keys, Stats stats, Action back,
            SolidColorBrush text, SolidColorBrush sub, SolidColorBrush border,
            SolidColorBrush hover, SolidColorBrush control, bool dark)
        {
            _keys = keys;
            _stats = stats;
            _back = back;
            _text = text;
            _sub = sub;
            _border = border;
            _hover = hover;
            _control = control;
            _dark = dark;
            Build();
            Refresh();
        }

        public Canvas View { get { return _canvas; } }

        /// 切换 Key / 数据更新后重绘
        public void Refresh()
        {
            if (_canvas == null) return;
            if (_keys.Count == 0)
            {
                _summary.Text = "还没有保存的 Key";
                _grid.Children.Clear();
                _chips.Children.Clear();
                for (int i = 0; i < 4; i++)
                {
                    if (_bars[i] != null) _bars[i].Width = 2;
                    if (_amounts[i] != null) _amounts[i].Text = "¥0.00";
                }
                return;
            }
            if (_selected < 0 || _selected >= _keys.Count) _selected = 0;

            BuildChips();
            DrawGrid();
        }

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
            TextBlock arrow = Ui.Icon("\uE72B", 11, _sub);
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            back.Child = arrow;
            back.MouseEnter += delegate { back.Background = _hover; };
            back.MouseLeave += delegate { back.Background = Brushes.Transparent; };
            back.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_back != null) _back();
            };
            MainWindow.SetInteractive(back, true);
            Ui.Place(_canvas, back, 15, 8);

            TextBlock title = Ui.Text("消耗记录", 12, _text, Ui.FontUi, FontWeights.SemiBold);
            Ui.Place(_canvas, title, 46, 13);

            // Key 选择条
            _chips = new StackPanel();
            _chips.Orientation = Orientation.Horizontal;
            ScrollViewer sv = new ScrollViewer();
            sv.Width = 240;
            sv.Height = 26;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = _chips;
            sv.Background = Brushes.Transparent;
            Ui.Place(_canvas, sv, 18, 46);

            _summary = Ui.Text("", 10.5, _sub);
            _summary.Width = 240;
            Ui.Place(_canvas, _summary, 18, 80);

            // 小时刻度
            double[] marks = { 0, 6, 12, 18, 23 };
            foreach (double m in marks)
            {
                TextBlock tick = Ui.Text(((int)m).ToString(), 8.5, _sub);
                tick.Width = 16;
                tick.TextAlignment = TextAlignment.Center;
                tick.Opacity = 0.7;
                double x = GridX + m * ColStep + CellW / 2 - 8;
                if (m >= 23) x = Math.Min(x, 246);
                Ui.Place(_canvas, tick, x, 94);
            }

            _grid = new Canvas();
            _grid.Width = 240;
            _grid.Height = 116;
            Ui.Place(_canvas, _grid, 18, GridTop);

            // 图例
            TextBlock low = Ui.Text("少", 8.5, _sub);
            Ui.Place(_canvas, low, 18, 209);
            for (int i = 0; i < 4; i++)
            {
                Border b = new Border();
                b.Width = 9;
                b.Height = 9;
                b.CornerRadius = new CornerRadius(2);
                b.Background = CellBrush(0.2 + 0.27 * i);
                Ui.Place(_canvas, b, 34 + i * 12, 211);
            }
            TextBlock high = Ui.Text("多", 8.5, _sub);
            Ui.Place(_canvas, high, 84, 209);

            // 分时段合计
            _periods = new Canvas();
            _periods.Width = 240;
            _periods.Height = 64;
            Ui.Place(_canvas, _periods, 18, 226);

            string[] names = { "凌晨 00-06", "上午 06-12", "下午 12-18", "晚间 18-24" };
            for (int i = 0; i < 4; i++)
            {
                TextBlock lab = Ui.Text(names[i], 9, _sub);
                lab.Width = 62;
                Ui.Place(_periods, lab, 0, i * 16 + 1);

                Border track = new Border();
                track.Width = 92;
                track.Height = 6;
                track.CornerRadius = new CornerRadius(3);
                track.Background = EmptyBrush();
                Ui.Place(_periods, track, 64, i * 16 + 4);

                Border bar = new Border();
                bar.Width = 2;
                bar.Height = 6;
                bar.CornerRadius = new CornerRadius(3);
                bar.Background = _control;
                Canvas.SetLeft(bar, 64);
                Canvas.SetTop(bar, i * 16 + 4);
                _periods.Children.Add(bar);
                _bars[i] = bar;

                TextBlock amt = Ui.Text("¥0.00", 9.5, _text);
                amt.Width = 62;
                amt.TextAlignment = TextAlignment.Right;
                Ui.Place(_periods, amt, 160, i * 16);
                _amounts[i] = amt;
            }

            TextBlock hint = Ui.Text("按小时记录，最多保留 7 天", 9, _sub);
            hint.Opacity = 0.7;
            Ui.Place(_canvas, hint, 18, 300);
        }

        private void BuildChips()
        {
            _chips.Children.Clear();
            for (int i = 0; i < _keys.Count; i++)
            {
                int idx = i;
                KeyItem k = _keys[i];
                bool active = i == _selected;

                Border chip = new Border();
                chip.Height = 22;
                chip.MinWidth = 44;
                chip.CornerRadius = new CornerRadius(11);
                chip.Padding = new Thickness(9, 0, 9, 0);
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.Cursor = Cursors.Hand;
                chip.Background = active ? _control : Brushes.Transparent;
                chip.BorderBrush = _border;
                chip.BorderThickness = active ? new Thickness(0) : new Thickness(1);

                TextBlock label = Ui.Text(string.IsNullOrEmpty(k.Name) ? Keys.DefaultName(k.Key) : k.Name,
                    9.5, active ? Ui.Brush(Colors.White) : _sub);
                label.VerticalAlignment = VerticalAlignment.Center;
                label.MaxWidth = 64;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                chip.Child = label;

                chip.MouseEnter += delegate
                {
                    if (idx != _selected) chip.Background = _hover;
                };
                chip.MouseLeave += delegate
                {
                    if (idx != _selected) chip.Background = Brushes.Transparent;
                };
                chip.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _selected = idx;
                    Refresh();
                };
                MainWindow.SetInteractive(chip, true);
                _chips.Children.Add(chip);
            }
        }

        private void DrawGrid()
        {
            _grid.Children.Clear();

            KeyItem key = _keys[_selected];
            KeyStat st = _stats.For(key.Key);
            double[,] heat = _stats.Heat(st);

            double max = 0.0001;
            for (int d = 0; d < 7; d++)
                for (int h = 0; h < 24; h++)
                    if (heat[d, h] > max) max = heat[d, h];

            double total = _stats.HeatSum(heat);
            _summary.Text = total <= 0.004
                ? "近 7 天暂无消耗记录"
                : string.Format("近 7 天 ¥{0:0.00} · 日均 ¥{1:0.00}", total, total / 7);

            // 分时段合计
            double[] per = new double[4];
            for (int d = 0; d < 7; d++)
                for (int h = 0; h < 24; h++)
                    per[h / 6] += heat[d, h];
            double perMax = 0.0001;
            for (int i = 0; i < 4; i++) if (per[i] > perMax) perMax = per[i];
            for (int i = 0; i < 4; i++)
            {
                if (_bars[i] == null) continue;
                double w = per[i] <= 0.004 ? 2 : Math.Max(3, 92 * per[i] / perMax);
                _bars[i].Width = w;
                _amounts[i].Text = "¥" + per[i].ToString("0.00");
                _amounts[i].Opacity = per[i] <= 0.004 ? 0.5 : 1;
            }

            for (int d = 0; d < 7; d++)
            {
                DateTime day = DateTime.Today.AddDays(d - 6);

                TextBlock dl = Ui.Text(day.ToString("MM/dd"), 8.5, _sub);
                dl.Width = 40;
                dl.TextAlignment = TextAlignment.Right;
                dl.Opacity = d == 6 ? 0.95 : 0.7;
                Ui.Place(_grid, dl, 0, d * RowStep + 1);

                for (int h = 0; h < 24; h++)
                {
                    double v = heat[d, h];
                    Border cell = new Border();
                    cell.Width = CellW;
                    cell.Height = CellH;
                    cell.CornerRadius = new CornerRadius(2);
                    cell.Background = v <= 0.0005 ? EmptyBrush() : CellBrush(v / max);
                    Canvas.SetLeft(cell, GridX - 18 + h * ColStep);
                    Canvas.SetTop(cell, d * RowStep);
                    _grid.Children.Add(cell);
                }
            }
        }

        private SolidColorBrush CellBrush(double ratio)
        {
            if (ratio <= 0) ratio = 0.05;
            if (ratio > 1) ratio = 1;
            Color c = _control.Color;
            byte alpha = (byte)(38 + 217 * ratio);
            return Ui.Brush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        private SolidColorBrush EmptyBrush()
        {
            Color c = _dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0, 0, 0);
            return Ui.Brush(Color.FromArgb(0x0E, c.R, c.G, c.B));
        }
    }
}
