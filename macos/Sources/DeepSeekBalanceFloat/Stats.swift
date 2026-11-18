import Foundation

struct KeyStat: Codable, Equatable {
    var days: [String: Double] = [:]
    var hours: [String: Double] = [:]
    var last: Double?
    /// 上一次采样的时间；旧数据没有该字段，首次恢复时只更新基准。
    var lastAt: Date?
}

/// 本地消耗统计。数据保存在 `~/Library/Application Support/DeepSeekBalanceFloat/stats.json`，
/// 按 Key 指纹分别记录，最多保留 90 天日汇总与 7 天小时明细。
final class StatsStore {
    private(set) var map: [String: KeyStat] = [:]
    private let fileURL: URL

    var fileURLForDisplay: URL { fileURL }
    private let calendar: Calendar

    init(fileURL: URL? = nil) {
        let fm = FileManager.default
        let dir = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            .appendingPathComponent("DeepSeekBalanceFloat", isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        self.fileURL = fileURL ?? dir.appendingPathComponent("stats.json")
        self.calendar = .current
        load()
    }

    // MARK: - 记录

    /// 两次采样间隔超过该值时，视为睡眠/退出/重新显示，只更新基准不记账。
    static let maximumSampleGap: TimeInterval = 60

    /// 记录一次余额；返回本次推算出的消耗（无消耗返回 0）。
    /// - Parameter at: 采样时间。只有与前一次采样间隔 ≤ 60 秒时才把余额差记到 `at` 所在的小时。
    @discardableResult
    func record(uid: String, total: Double, at: Date = Date()) -> Double {
        var stat = map[uid] ?? KeyStat()
        var spent: Double = 0
        if let last = stat.last, let lastAt = stat.lastAt {
            let gap = at.timeIntervalSince(lastAt)
            let delta = last - total
            if gap >= 0, gap <= Self.maximumSampleGap, delta > 0.0005 {
                spent = delta
                let day = Self.dayKey(at)
                stat.days[day] = ((stat.days[day] ?? 0) + delta).rounded(to: 4)
                let hour = Self.hourKey(at)
                stat.hours[hour] = ((stat.hours[hour] ?? 0) + delta).rounded(to: 4)
            }
        }
        stat.last = total
        stat.lastAt = at
        map[uid] = stat
        return spent
    }

    func stat(for uid: String) -> KeyStat {
        map[uid] ?? KeyStat()
    }

    /// 仅供演示/测试注入。
    func seed(_ map: [String: KeyStat]) {
        self.map = map
    }

    // MARK: - 查询

    func today(_ uid: String) -> Double {
        (map[uid]?.days[Self.dayKey(Date())] ?? 0).rounded(to: 2)
    }

    /// 最近 7 天，第 0 个是 6 天前，最后一个是今天。
    func week(_ uid: String) -> [Double] {
        let stat = map[uid] ?? KeyStat()
        return (0..<7).map { offset in
            let date = calendar.date(byAdding: .day, value: offset - 6, to: Date()) ?? Date()
            return (stat.days[Self.dayKey(date)] ?? 0).rounded(to: 2)
        }
    }

    func weekDates() -> [Date] {
        (0..<7).map { offset in
            calendar.date(byAdding: .day, value: offset - 6, to: Date()) ?? Date()
        }
    }

    /// 7 行 × 24 列，第 0 行是 6 天前。
    func heat(_ uid: String) -> [[Double]] {
        let stat = map[uid] ?? KeyStat()
        var rows: [[Double]] = []
        for offset in 0..<7 {
            let date = calendar.date(byAdding: .day, value: offset - 6, to: Date()) ?? Date()
            let day = Self.dayKey(date)
            var row = [Double](repeating: 0, count: 24)
            for hour in 0..<24 {
                row[hour] = stat.hours["\(day) \(String(format: "%02d", hour))"] ?? 0
            }
            rows.append(row)
        }
        return rows
    }

    func heatTotal(_ uid: String) -> Double {
        heat(uid).flatMap { $0 }.reduce(0, +).rounded(to: 2)
    }

    /// 凌晨 / 上午 / 下午 / 晚间 四段汇总。
    func buckets(_ uid: String) -> [Double] {
        let rows = heat(uid)
        var result = [Double](repeating: 0, count: 4)
        for row in rows {
            for (hour, value) in row.enumerated() {
                switch hour {
                case 0..<6: result[0] += value
                case 6..<12: result[1] += value
                case 12..<18: result[2] += value
                default: result[3] += value
                }
            }
        }
        return result.map { $0.rounded(to: 2) }
    }

    func clear() {
        map = [:]
        save()
    }

    func hasAnyData(_ uid: String) -> Bool {
        let stat = map[uid] ?? KeyStat()
        return stat.days.values.contains { $0 > 0 } || stat.hours.values.contains { $0 > 0 }
    }

    // MARK: - 持久化

    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else { return }
        if let decoded = try? JSONDecoder().decode([String: KeyStat].self, from: data) {
            map = decoded
        }
    }

    func save() {
        prune()
        guard let data = try? JSONEncoder().encode(map) else { return }
        do {
            try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            try data.write(to: fileURL, options: .atomic)
        } catch {
            // 统计失败不应影响余额展示。
            NSLog("DeepSeekBalance: 保存统计失败 \(error.localizedDescription)")
        }
    }

    private func prune() {
        let cutoff = Self.dayKey(calendar.date(byAdding: .day, value: -90, to: Date()) ?? Date())
        let hourCutoff = Self.dayKey(calendar.date(byAdding: .day, value: -7, to: Date()) ?? Date())
        for (uid, var stat) in map {
            stat.days = stat.days.filter { $0.key >= cutoff }
            stat.hours = stat.hours.filter { item in
                item.key.count >= 10 && String(item.key.prefix(10)) >= hourCutoff
            }
            map[uid] = stat
        }
    }

    // MARK: - 日期键

    static func dayKey(_ date: Date) -> String {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }

    static func hourKey(_ date: Date) -> String {
        let c = Calendar.current.dateComponents([.year, .month, .day, .hour], from: date)
        return String(format: "%04d-%02d-%02d %02d", c.year ?? 0, c.month ?? 0, c.day ?? 0, c.hour ?? 0)
    }

    static func shortDate(_ date: Date) -> String {
        let c = Calendar.current.dateComponents([.month, .day], from: date)
        return String(format: "%02d/%02d", c.month ?? 0, c.day ?? 0)
    }
}

extension Double {
    func rounded(to places: Int) -> Double {
        let factor = pow(10.0, Double(places))
        return (self * factor).rounded() / factor
    }
}
