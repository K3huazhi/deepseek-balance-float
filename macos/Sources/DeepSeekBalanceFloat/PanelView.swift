import SwiftUI
import AppKit

struct PanelView: View {
    @ObservedObject var model: AppModel

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    @State private var window: NSWindow?
    @State private var mouseOrigin: NSPoint?
    @State private var frameOrigin: NSPoint?
    @State private var isDragging = false

    @State private var flyText = ""
    @State private var flyOpacity: Double = 0
    @State private var flyOffset: CGFloat = 0
    @State private var shake: CGFloat = 0

    private static let clockFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "zh_Hans_CN")
        f.dateFormat = "HH:mm"
        return f
    }()

    var body: some View {
        let p = Palette(scheme: scheme)
        VStack(spacing: 0) {
            header(p)
            if model.keys.isEmpty {
                emptyState(p)
            } else if model.isMulti {
                multiBody(p)
            } else {
                singleBody(p)
            }
            Spacer(minLength: 0)
        }
        .frame(width: 300, height: model.panelHeight, alignment: .top)
        .background(PanelBackground())
        .environment(\.palette, p)
        .clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))
        .contentShape(RoundedRectangle(cornerRadius: 26, style: .continuous))
        .simultaneousGesture(dragGesture)
        .contextMenu { contextMenu }
        .background(WindowAccessor { window = $0 })
        .onChange(of: model.dropEvent) { _, event in handleDrop(event, palette: p) }
    }

    // MARK: - 头部

    private func header(_ p: Palette) -> some View {
        HStack(spacing: 7) {
            Button {
                model.openSettings(.general)
            } label: {
                WhaleMark()
                    .frame(width: 25, height: 18.4)
                    .foregroundStyle(p.text.opacity(0.92))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("打开设置")
            .accessibilityLabel("DeepSeek 鲸鱼标志，打开设置")

            HStack(spacing: 3) {
                Text("DeepSeek")
                    .font(.system(size: 12.5, weight: .semibold))
                    .foregroundStyle(p.text)
                Text("余额")
                    .font(.system(size: 12.5, weight: .regular))
                    .foregroundStyle(p.secondary)
            }
            .contentShape(Rectangle())
            .onTapGesture(count: 2) { toggleExpanded() }
            .help("双击展开或收起详情")

            Spacer(minLength: 6)

            TimelineView(.everyMinute) { context in
                Text(Self.clockFormatter.string(from: context.date))
                    .font(.system(size: 10.5, weight: .regular))
                    .monospacedDigit()
                    .foregroundStyle(p.tertiary)
            }

            PanelIconButton(systemName: model.settings.theme.symbol, help: themeHelp) {
                withAnimation(.smooth(duration: 0.2)) { model.cycleTheme() }
            }

            if !model.isMulti && !model.keys.isEmpty {
                PanelIconButton(systemName: model.settings.expanded ? "chevron.up" : "chevron.down",
                                help: model.settings.expanded ? "收起详情" : "展开详情") {
                    toggleExpanded()
                }
            }
        }
        .padding(.leading, 16)
        .padding(.trailing, 9)
        .frame(height: 46)
    }

    private var themeHelp: String {
        switch model.settings.theme {
        case .system: return "显示模式：自动（点击切换）"
        case .light: return "显示模式：亮色（点击切换）"
        case .dark: return "显示模式：暗色（点击切换）"
        }
    }

    private func toggleExpanded() {
        guard !model.isMulti else { return }
        withAnimation(reduceMotion ? nil : .smooth(duration: 0.3)) {
            model.settings.expanded.toggle()
        }
    }

    // MARK: - 单 Key

    private func singleBody(_ p: Palette) -> some View {
        let key = model.primaryKey
        let info = model.balance(for: key)
        let error = model.error(for: key)
        let total = info?.total ?? 0
        let color = p.tone(total, available: info?.available ?? true)

        return VStack(spacing: 0) {
            ZStack(alignment: .topTrailing) {
                VStack(alignment: .leading, spacing: 0) {
                    if let info {
                        AnimatedMoney(
                            symbol: info.symbol,
                            value: info.total,
                            font: .system(size: 40, weight: .semibold),
                            color: color
                        )
                        .offset(x: shake)
                        .accessibilityLabel("可用余额 \(info.symbol)\(BalanceInfo.money(info.total))")
                    } else {
                        Text("—")
                            .font(.system(size: 40, weight: .semibold))
                            .foregroundStyle(p.tertiary)
                            .frame(height: 48, alignment: .leading)
                            .accessibilityLabel(error == nil ? "正在获取余额" : "余额获取失败")
                    }

                    HStack(spacing: 5) {
                        if let error {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .font(.system(size: 8.5, weight: .semibold))
                            Text(error)
                                .lineLimit(1)
                                .truncationMode(.tail)
                        } else {
                            Text(info?.subtitle ?? (model.isRefreshing ? "正在获取…" : "等待刷新"))
                                .lineLimit(1)
                        }
                        Spacer(minLength: 4)
                        if model.isRefreshing, info == nil {
                            ProgressView()
                                .controlSize(.mini)
                        }
                    }
                    .font(.system(size: 10.8))
                    .foregroundStyle(error == nil ? p.secondary : p.negative)
                    .padding(.top, 1)
                }
                .padding(.leading, 16)
                .padding(.trailing, 16)
                .padding(.top, 4)

                WhaleShape()
                    .fill(p.text.opacity(0.055))
                    .frame(width: 104, height: 76.5)
                    .offset(x: 14, y: -4)
                    .allowsHitTesting(false)

                if flyOpacity > 0 {
                    Text(flyText)
                        .font(.system(size: 12.5, weight: .semibold))
                        .monospacedDigit()
                        .foregroundStyle(p.negative)
                        .offset(x: -22, y: flyOffset - 6)
                        .opacity(flyOpacity)
                        .allowsHitTesting(false)
                }
            }
            .frame(height: 76)

            if model.settings.expanded {
                details(p, key: key, info: info, error: error)
            } else {
                Color.clear.frame(height: 16)
            }
        }
    }

    private func details(_ p: Palette, key: KeyItem?, info: BalanceInfo?, error: String?) -> some View {
        let symbol = info?.symbol ?? "¥"
        let today = model.todaySpend(for: key)
        let week = key.map { model.stats.week($0.id) } ?? Array(repeating: 0, count: 7)
        let dates = model.stats.weekDates()
        let weekTotal = week.reduce(0, +)

        return VStack(spacing: 0) {
            Hairline().padding(.horizontal, 16)
            VStack(spacing: 0) {
                metricRow(p, "充值余额", value: info.map { "\($0.symbol)\(BalanceInfo.money($0.topped))" } ?? "—", color: p.text)
                metricRow(p, "赠送余额", value: info.map { "\($0.symbol)\(BalanceInfo.money($0.granted))" } ?? "—", color: p.text)
                metricRow(p, "今日消费",
                          value: (today > 0 ? "-" : "") + symbol + BalanceInfo.money(today),
                          color: today > 0 ? p.negative : p.text)
                HStack(spacing: 0) {
                    Text("账户状态")
                        .font(.system(size: 11.5))
                        .foregroundStyle(p.secondary)
                    Spacer()
                    HStack(spacing: 5) {
                        Circle()
                            .fill(info?.available == false ? p.negative : p.positive)
                            .frame(width: 5, height: 5)
                        Text(info?.available == false ? "余额不足" : "正常")
                            .font(.system(size: 11.5, weight: .medium))
                            .foregroundStyle(info?.available == false ? p.negative : p.positive)
                    }
                }
                .frame(height: 22)
            }
            .padding(.horizontal, 16)
            .padding(.top, 9)

            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 6) {
                    Text("近 7 日消耗")
                        .font(.system(size: 10.5))
                        .foregroundStyle(p.secondary)
                    Spacer()
                    if weekTotal > 0 {
                        Text("\(symbol)\(BalanceInfo.money(weekTotal))")
                            .font(.system(size: 10.5))
                            .monospacedDigit()
                            .foregroundStyle(p.tertiary)
                    }
                }
                WeekBars(values: week, dates: dates, symbol: symbol, palette: p)
                    .frame(height: 44)
                Text(estimateText(total: info?.total ?? 0, week: week))
                    .font(.system(size: 10.3))
                    .foregroundStyle(p.tertiary)
                    .lineLimit(1)
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)

            footer(p)
        }
    }

    private func metricRow(_ p: Palette, _ label: String, value: String, color: Color) -> some View {
        HStack(spacing: 0) {
            Text(label)
                .font(.system(size: 11.5))
                .foregroundStyle(p.secondary)
            Spacer()
            Text(value)
                .font(.system(size: 11.5, weight: .medium))
                .monospacedDigit()
                .foregroundStyle(color)
                .lineLimit(1)
        }
        .frame(height: 22)
    }

    private func estimateText(total: Double, week: [Double]) -> String {
        let active = week.filter { $0 > 0 }
        guard !active.isEmpty else { return "还没有消耗记录，攒够数据后估算可用天数" }
        let average = active.reduce(0, +) / Double(active.count)
        guard average > 0 else { return "还没有消耗记录，攒够数据后估算可用天数" }
        let days = total / average
        if days >= 365 { return "按近期消耗 ≈ 可用一年以上" }
        return "按近 7 日均耗 ≈ 还能用 \(max(1, Int(days.rounded()))) 天"
    }

    // MARK: - 多 Key

    private func multiBody(_ p: Palette) -> some View {
        VStack(spacing: 0) {
            ForEach(Array(model.visibleKeys.enumerated()), id: \.element.id) { index, key in
                multiRow(p, key: key)
                if index < model.visibleKeys.count - 1 {
                    Hairline().padding(.horizontal, 16)
                }
            }
            footer(p)
        }
    }

    private func multiRow(_ p: Palette, key: KeyItem) -> some View {
        let info = model.balance(for: key)
        let error = model.error(for: key)
        let today = model.todaySpend(for: key)
        let color = p.tone(info?.total ?? 0, available: info?.available ?? true)

        return HStack(alignment: .center, spacing: 8) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 4) {
                    Text(key.name)
                        .font(.system(size: 11.5, weight: .medium))
                        .foregroundStyle(p.text)
                        .lineLimit(1)
                    if error != nil {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 8))
                            .foregroundStyle(p.negative)
                    }
                }
                Text(error ?? KeyIdentity.mask(key.key))
                    .font(.system(size: 9.5))
                    .foregroundStyle(error == nil ? p.tertiary : p.negative)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer(minLength: 6)
            VStack(alignment: .trailing, spacing: 1) {
                if let info {
                    AnimatedMoney(
                        symbol: info.symbol,
                        value: info.total,
                        font: .system(size: 20, weight: .semibold),
                        color: color
                    )
                } else {
                    Text("—")
                        .font(.system(size: 20, weight: .semibold))
                        .foregroundStyle(p.tertiary)
                }
                Text("今日消耗  " + (today > 0 ? "-" : "") + (info?.symbol ?? "¥") + BalanceInfo.money(today))
                    .font(.system(size: 9.5))
                    .monospacedDigit()
                    .foregroundStyle(p.tertiary)
            }
        }
        .padding(.horizontal, 16)
        .frame(height: 58)
        .contentShape(Rectangle())
        .onTapGesture { model.openSettings(.keys) }
        .help("\(key.name) · 点击管理 Key")
    }

    // MARK: - 底部

    private func footer(_ p: Palette) -> some View {
        HStack(spacing: 4) {
            Text("更新于 \(model.lastUpdateText)")
                .font(.system(size: 9.8))
                .monospacedDigit()
                .foregroundStyle(p.tertiary)
            Spacer()
            PanelIconButton(systemName: "gearshape", help: "设置") {
                model.openSettings(.general)
            }
            if model.isRefreshing {
                ProgressView()
                    .controlSize(.mini)
                    .frame(width: 24, height: 22)
            } else {
                PanelIconButton(systemName: "arrow.clockwise", help: "立即刷新（⌘R）") {
                    model.refreshAll()
                }
            }
        }
        .padding(.horizontal, 16)
        .frame(height: model.isMulti ? 38 : 34)
    }

    // MARK: - 空状态

    private func emptyState(_ p: Palette) -> some View {
        VStack(spacing: 7) {
            WhaleShape()
                .fill(p.text.opacity(0.10))
                .frame(width: 58, height: 42.7)
            Text(model.storageError == nil ? "还没有 API Key" : "无法读取钥匙串")
                .font(.system(size: 12.5, weight: .semibold))
                .foregroundStyle(p.text)
            Text(model.storageError ?? "添加后即可实时显示账户余额")
                .font(.system(size: 10.5))
                .foregroundStyle(model.storageError == nil ? p.secondary : p.negative)
                .lineLimit(2)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 18)
            AccentButton(title: "添加 API Key") { model.openSettings(.keys) }
                .padding(.top, 2)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 4)
        .padding(.bottom, 8)
    }

    // MARK: - 右键菜单

    @ViewBuilder
    private var contextMenu: some View {
        Button("刷新余额") { model.refreshAll() }
        Button("复制当前余额") { model.copyBalance() }
            .disabled(model.primaryKey == nil)
        Divider()
        Button(model.settings.expanded ? "收起详情" : "展开详情") {
            toggleExpanded()
        }
        .disabled(model.isMulti)
        Button("设置…") { model.openSettings(.general) }
        Button("API Key 管理…") { model.openSettings(.keys) }
        Button("消耗记录…") { model.openSettings(.usage) }
        Divider()
        Toggle("窗口置顶", isOn: Binding(
            get: { model.settings.pinned },
            set: { model.settings.pinned = $0 }
        ))
        Toggle("在菜单栏显示余额", isOn: Binding(
            get: { model.settings.showMenuBar },
            set: { model.settings.showMenuBar = $0 }
        ))
        Toggle("在 Dock 中显示", isOn: Binding(
            get: { model.settings.showDockIcon },
            set: { model.settings.showDockIcon = $0 }
        ))
        Divider()
        Button("隐藏悬浮窗") { model.hidePanelAction?() }
        Button("退出 DeepSeek 余额") { NSApp.terminate(nil) }
    }

    // MARK: - 拖动

    private var dragGesture: some Gesture {
        DragGesture(minimumDistance: 4, coordinateSpace: .global)
            .onChanged { _ in
                guard let window else { return }
                if mouseOrigin == nil {
                    mouseOrigin = NSEvent.mouseLocation
                    frameOrigin = window.frame.origin
                }
                guard let mouseOrigin, let frameOrigin else { return }
                let mouse = NSEvent.mouseLocation
                let dx = mouse.x - mouseOrigin.x
                let dy = mouse.y - mouseOrigin.y
                if !isDragging {
                    guard abs(dx) + abs(dy) >= 3 else { return }
                    isDragging = true
                    NSCursor.closedHand.push()
                }
                window.setFrameOrigin(NSPoint(x: frameOrigin.x + dx, y: frameOrigin.y + dy))
            }
            .onEnded { _ in
                if isDragging {
                    NSCursor.pop()
                    if let window {
                        let target = WindowSnapper.snap(window)
                        if target != window.frame {
                            window.setFrame(target, display: true, animate: true)
                        }
                        NotificationCenter.default.post(name: .panelDidMove, object: window)
                    }
                }
                mouseOrigin = nil
                frameOrigin = nil
                isDragging = false
            }
    }

    // MARK: - 消耗动画

    private func handleDrop(_ event: DropEvent?, palette p: Palette) {
        guard let event, !model.isMulti else { return }
        flyText = "-\(event.symbol)\(BalanceInfo.money(event.amount))"
        flyOpacity = 1
        flyOffset = 0
        withAnimation(.easeOut(duration: 0.9)) {
            flyOffset = -30
        }
        Task { @MainActor in
            try? await Task.sleep(for: .milliseconds(260))
            withAnimation(.easeIn(duration: 0.55)) { flyOpacity = 0 }
        }
        if !reduceMotion {
            shake = -4
            withAnimation(.easeInOut(duration: 0.075).repeatCount(3, autoreverses: true)) {
                shake = 4
            }
            Task { @MainActor in
                try? await Task.sleep(for: .milliseconds(420))
                withAnimation(.easeOut(duration: 0.15)) { shake = 0 }
            }
        }
    }
}

// MARK: - 柱状图

struct WeekBars: View {
    let values: [Double]
    let dates: [Date]
    let symbol: String
    let palette: Palette

    var body: some View {
        let maxValue = max(0.01, values.max() ?? 0)
        HStack(alignment: .bottom, spacing: 8) {
            ForEach(0..<7, id: \.self) { index in
                let value = index < values.count ? values[index] : 0
                let ratio = min(1, max(0, value / maxValue))
                ZStack(alignment: .bottom) {
                    RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                        .fill(palette.barTrack)
                        .frame(height: 44)
                    RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                        .fill(palette.accent.opacity(value > 0 ? 0.92 : 0.0))
                        .frame(height: value > 0 ? max(4, 44 * ratio) : 0)
                }
                .frame(maxWidth: .infinity)
                .help("\(StatsStore.shortDate(dates[index]))  \(symbol)\(BalanceInfo.money(value))")
                .accessibilityLabel("\(StatsStore.shortDate(dates[index])) 消耗 \(symbol)\(BalanceInfo.money(value))")
            }
        }
    }
}
