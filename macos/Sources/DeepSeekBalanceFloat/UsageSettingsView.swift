import SwiftUI

struct UsageSettingsView: View {
    @ObservedObject var model: AppModel
    @Environment(\.palette) private var p

    @State private var selectedID: String?

    private var selectedKey: KeyItem? {
        if let selectedID, let key = model.keys.first(where: { $0.id == selectedID }) { return key }
        return model.keys.first
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                SettingsPageHeader(
                    title: "消耗记录",
                    subtitle: "按 Key 记录最近 7 天的小时消耗。数据由前后两次余额差值推算，仅本机保存。"
                )

                if model.keys.isEmpty {
                    ContentUnavailableView {
                        Label("还没有消耗记录", systemImage: "chart.bar.xaxis")
                    } description: {
                        Text("添加 API Key 并保持悬浮窗运行后，消耗会自动累积。")
                    } actions: {
                        Button("添加 API Key") { model.openSettings(.keys) }
                    }
                    .frame(maxWidth: .infinity, minHeight: 320)
                } else {
                    chips
                    if let key = selectedKey {
                        summary(key)
                        heatCard(key)
                        sessionCard(key)
                    }
                }
            }
            .padding(28)
            .frame(maxWidth: 660, alignment: .leading)
        }
        .onAppear { syncSelection() }
        .onChange(of: model.keys.map(\.id)) { _, _ in syncSelection() }
    }

    private func syncSelection() {
        if let selectedID, model.keys.contains(where: { $0.id == selectedID }) { return }
        selectedID = model.visibleKeys.first?.id ?? model.keys.first?.id
    }

    private var chips: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(model.keys) { key in
                    KeyChip(title: key.name, selected: key.id == selectedKey?.id) {
                        selectedID = key.id
                    }
                }
            }
            .padding(.vertical, 1)
        }
    }

    private func summary(_ key: KeyItem) -> some View {
        let week = model.stats.week(key.id)
        let total = week.reduce(0, +)
        let average = total / 7
        return HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text("近 7 天")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Text("¥\(BalanceInfo.money(total))")
                .font(.system(size: 22, weight: .semibold))
                .monospacedDigit()
            Text("日均 ¥\(BalanceInfo.money(average))")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
            Spacer()
            Text("更新于 \(model.lastUpdateText)")
                .font(.system(size: 10))
                .monospacedDigit()
                .foregroundStyle(.tertiary)
        }
    }

    private func heatCard(_ key: KeyItem) -> some View {
        let matrix = model.stats.heat(key.id)
        let hasData = model.stats.hasAnyData(key.id)
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("7 天 × 24 小时")
                    .font(.system(size: 11.5, weight: .semibold))
                Spacer()
                Text("深色表示消耗更多")
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
            }
            if hasData {
                HeatmapView(matrix: matrix, dates: model.stats.weekDates(), symbol: "¥")
                HStack(spacing: 5) {
                    Text("少").font(.system(size: 9)).foregroundStyle(.tertiary)
                    ForEach(0..<5, id: \.self) { index in
                        RoundedRectangle(cornerRadius: 2, style: .continuous)
                            .fill(p.accent.opacity(0.15 + 0.18 * Double(index)))
                            .frame(width: 10, height: 10)
                    }
                    Text("多").font(.system(size: 9)).foregroundStyle(.tertiary)
                    Spacer()
                    Text("按小时记录，最多保留 7 天")
                        .font(.system(size: 9.5))
                        .foregroundStyle(.tertiary)
                }
            } else {
                VStack(spacing: 6) {
                    Image(systemName: "clock.badge.questionmark")
                        .font(.system(size: 20))
                        .foregroundStyle(.tertiary)
                    Text("这个 Key 还没有消耗记录")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                    Text("保持程序运行，发生消费后这里会自动生成热力图。")
                        .font(.system(size: 10.5))
                        .foregroundStyle(.tertiary)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 34)
            }
        }
        .padding(16)
        .background {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Color.primary.opacity(0.045))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.07), lineWidth: 0.8)
        }
    }

    private func sessionCard(_ key: KeyItem) -> some View {
        let buckets = model.stats.buckets(key.id)
        let labels = ["凌晨 00–06", "上午 06–12", "下午 12–18", "晚间 18–24"]
        let maxValue = max(0.01, buckets.max() ?? 0)
        return VStack(alignment: .leading, spacing: 12) {
            Text("分时段汇总")
                .font(.system(size: 11.5, weight: .semibold))
            ForEach(0..<4, id: \.self) { index in
                HStack(spacing: 12) {
                    Text(labels[index])
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .frame(width: 82, alignment: .leading)
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            Capsule().fill(Color.primary.opacity(0.06)).frame(height: 6)
                            Capsule()
                                .fill(p.accent.opacity(0.85))
                                .frame(width: buckets[index] > 0 ? max(4, geo.size.width * buckets[index] / maxValue) : 0,
                                       height: 6)
                        }
                        .frame(maxHeight: .infinity, alignment: .center)
                    }
                    .frame(height: 14)
                    Text("¥\(BalanceInfo.money(buckets[index]))")
                        .font(.system(size: 11))
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                        .frame(width: 64, alignment: .trailing)
                }
            }
        }
        .padding(16)
        .background {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Color.primary.opacity(0.045))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.07), lineWidth: 0.8)
        }
    }
}

struct HeatmapView: View {
    let matrix: [[Double]]
    let dates: [Date]
    let symbol: String

    @Environment(\.palette) private var p

    private var maxValue: Double {
        max(0.01, matrix.flatMap { $0 }.max() ?? 0)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 3) {
                Color.clear.frame(width: 38, height: 10)
                ForEach(0..<24, id: \.self) { hour in
                    Text(hour % 6 == 0 ? "\(hour)" : "")
                        .font(.system(size: 8))
                        .foregroundStyle(.tertiary)
                        .frame(width: 15)
                }
            }

            ForEach(0..<7, id: \.self) { day in
                HStack(spacing: 3) {
                    Text(StatsStore.shortDate(dates[day]))
                        .font(.system(size: 8.5))
                        .monospacedDigit()
                        .foregroundStyle(.tertiary)
                        .frame(width: 38, alignment: .leading)
                    ForEach(0..<24, id: \.self) { hour in
                        let value = matrix[day][hour]
                        RoundedRectangle(cornerRadius: 2.5, style: .continuous)
                            .fill(cellColor(value))
                            .frame(width: 15, height: 15)
                            .help("\(StatsStore.shortDate(dates[day])) \(String(format: "%02d", hour)):00  \(symbol)\(BalanceInfo.money(value))")
                    }
                }
            }
        }
    }

    private func cellColor(_ value: Double) -> Color {
        guard value > 0 else { return p.barTrack }
        let ratio = min(1, value / maxValue)
        return p.accent.opacity(0.20 + 0.80 * pow(ratio, 0.55))
    }
}
