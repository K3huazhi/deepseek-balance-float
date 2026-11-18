import Foundation
import AppKit
import Combine
import SwiftUI

@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    // MARK: - 发布状态

    @Published private(set) var keys: [KeyItem] = []
    @Published private(set) var balances: [String: BalanceInfo] = [:]
    @Published private(set) var errors: [String: String] = [:]
    @Published private(set) var lastUpdate: Date?
    @Published private(set) var isRefreshing = false
    @Published private(set) var storageError: String?
    @Published private(set) var statsRevision = 0
    @Published var dropEvent: DropEvent?
    @Published var pendingSection: SettingsSection = .general
    @Published private(set) var panelVisible = true

    let settings: AppSettings
    let stats: StatsStore
    let isDemo: Bool

    // MARK: - 窗口动作（由 AppDelegate 注入）

    var openSettingsAction: ((SettingsSection) -> Void)?
    var showPanelAction: (() -> Void)?
    var hidePanelAction: (() -> Void)?
    var togglePanelAction: (() -> Void)?
    var refreshMenuBarAction: (() -> Void)?

    private var refreshLoop: Task<Void, Never>?
    private var cancellables = Set<AnyCancellable>()
    private var lastThemeTick = Date.distantPast

    private static let timeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "zh_Hans_CN")
        f.dateFormat = "HH:mm:ss"
        return f
    }()

    // MARK: - 生命周期

    init(demo: Bool? = nil) {
        let env = ProcessInfo.processInfo.environment["DSB_DEMO"] == "1"
        let flag = CommandLine.arguments.contains("--demo")
        let demoMode = demo ?? (env || flag)
        isDemo = demoMode
        settings = AppSettings()
        if demoMode {
            let url = FileManager.default.temporaryDirectory.appendingPathComponent("dsb-demo-stats.json")
            stats = StatsStore(fileURL: url)
            keys = [
                KeyItem(key: "sk-demo3b990577a3d4c1e9f2a7b8c6d5e4f3a2b1", show: true, name: "3b9905"),
                KeyItem(key: "sk-demof5bc6784ec9a1b2c3d4e5f6a7b8c9d0e1f2", show: true, name: "f5bc67"),
                KeyItem(key: "sk-demo9147c4eae3d2c1b0a9f8e7d6c5b4a3f2e1", show: true, name: "9147c4")
            ]
            seedDemoData()
            lastUpdate = Date()
        } else {
            stats = StatsStore()
            do {
                keys = try KeychainStore.load()
            } catch {
                storageError = error.localizedDescription
                NSLog("DeepSeekBalance: \(error.localizedDescription)")
            }
        }

        settings.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                guard let self else { return }
                self.objectWillChange.send()
                DispatchQueue.main.async { self.applyAppearance() }
            }
            .store(in: &cancellables)

        applyAppearance()
    }

    // MARK: - 派生状态

    var visibleKeys: [KeyItem] {
        Array(keys.filter { $0.show }.prefix(3))
    }

    var primaryKey: KeyItem? {
        visibleKeys.first ?? keys.first
    }

    var isMulti: Bool { visibleKeys.count >= 2 }

    var panelHeight: CGFloat {
        if keys.isEmpty { return 184 }
        if isMulti { return 48 + CGFloat(visibleKeys.count) * 58 + 38 }
        return settings.expanded ? 368 : 138
    }

    var lastUpdateText: String {
        guard let lastUpdate else { return "尚未更新" }
        return Self.timeFormatter.string(from: lastUpdate)
    }

    func balance(for key: KeyItem?) -> BalanceInfo? {
        guard let key else { return nil }
        return balances[key.id]
    }

    func error(for key: KeyItem?) -> String? {
        guard let key else { return nil }
        return errors[key.id]
    }

    func todaySpend(for key: KeyItem?) -> Double {
        guard let key else { return 0 }
        return stats.today(key.id)
    }

    var hasAnyBalance: Bool { !balances.isEmpty }

    // MARK: - 刷新

    func start() {
        Task { [weak self] in
            let enabled = await Task.detached { LoginItem.isEnabled }.value
            self?.settings.launchAtLogin = enabled
        }
        applyAppearance()
        refreshLoop?.cancel()
        refreshLoop = Task { [weak self] in
            while !Task.isCancelled {
                self?.refreshAll()
                try? await Task.sleep(for: .seconds(5))
                self?.themeTick()
            }
        }
        refreshMenuBarAction?()
    }

    func stop() {
        refreshLoop?.cancel()
        refreshLoop = nil
    }

    func refreshAll() {
        guard !isRefreshing else { return }
        if isDemo {
            isRefreshing = true
            Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(280))
                self?.applyDemoBalances()
                self?.isRefreshing = false
            }
            return
        }

        let targets = visibleKeys.map { (uid: $0.id, key: $0.key) }
        guard !targets.isEmpty else {
            isRefreshing = false
            lastUpdate = Date()
            return
        }
        isRefreshing = true
        let url = settings.apiURL

        Task { [weak self] in
            var results: [(String, Result<BalanceInfo, Error>)] = []
            await withTaskGroup(of: (String, Result<BalanceInfo, Error>).self) { group in
                for target in targets {
                    group.addTask {
                        let result = await Self.fetch(target.key, url: url)
                        return (target.uid, result)
                    }
                }
                for await item in group { results.append(item) }
            }
            guard let self else { return }
            self.apply(results)
        }
    }

    private nonisolated static func fetch(_ key: String, url: String) async -> Result<BalanceInfo, Error> {
        do {
            let info = try await ApiClient.fetch(key: key, urlString: url)
            return .success(info)
        } catch {
            return .failure(error)
        }
    }

    private func apply(_ results: [(String, Result<BalanceInfo, Error>)]) {
        var statsChanged = false
        var newDrop: (uid: String, amount: Double, symbol: String)?
        for (uid, result) in results {
            switch result {
            case let .success(info):
                balances[uid] = info
                errors[uid] = nil
                let spent = stats.record(uid: uid, total: info.total)
                if spent > 0 {
                    statsChanged = true
                    if uid == primaryKey?.id {
                        newDrop = (uid, spent, info.symbol)
                    }
                }
            case let .failure(error):
                errors[uid] = error.localizedDescription
            }
        }
        if statsChanged {
            stats.save()
            statsRevision += 1
        }
        lastUpdate = Date()
        isRefreshing = false
        if let newDrop {
            let event = DropEvent(uid: newDrop.uid, amount: newDrop.amount, symbol: newDrop.symbol)
            dropEvent = event
            Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(1500))
                if self?.dropEvent?.id == event.id { self?.dropEvent = nil }
            }
        }
    }

    // MARK: - 外观

    func applyAppearance() {
        let dark: Bool?
        switch settings.theme {
        case .light:
            dark = false
        case .dark:
            dark = true
        case .system:
            if settings.solarAuto {
                dark = SunTimes.isNight(Date(), latitude: settings.latitude, longitude: settings.longitude)
            } else {
                dark = nil
            }
        }
        if let dark {
            NSApp.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        } else {
            NSApp.appearance = nil
        }
    }

    private func themeTick() {
        guard settings.theme == .system, settings.solarAuto else { return }
        guard Date().timeIntervalSince(lastThemeTick) > 30 else { return }
        lastThemeTick = Date()
        applyAppearance()
    }

    func cycleTheme() {
        let order: [ThemeMode] = [.light, .dark, .system]
        let index = order.firstIndex(of: settings.theme) ?? 2
        settings.theme = order[(index + 1) % order.count]
        applyAppearance()
    }

    // MARK: - Key 管理

    @discardableResult
    func addKey(_ raw: String) -> String? {
        let normalized = KeyIdentity.normalized(raw)
        guard !normalized.isEmpty else { return "请输入 API Key" }
        guard normalized.count >= 8 else { return "Key 长度不正确" }
        guard !keys.contains(where: { $0.key == normalized }) else { return "该 Key 已存在" }
        guard keys.count < KeychainStore.maxKeys else { return "最多保存 \(KeychainStore.maxKeys) 个 Key" }
        var next = keys
        next.append(KeyItem(key: normalized, show: visibleKeys.count < 3))
        if let error = commit(next) { return error }
        refreshAll()
        return nil
    }

    @discardableResult
    func removeKey(_ id: String) -> String? {
        var next = keys.filter { $0.id != id }
        if !next.isEmpty, !next.contains(where: { $0.show }) {
            next[0].show = true
        }
        return commit(next)
    }

    @discardableResult
    func renameKey(_ id: String, to name: String) -> String? {
        guard let index = keys.firstIndex(where: { $0.id == id }) else { return nil }
        var next = keys
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        next[index].name = trimmed.isEmpty ? KeyIdentity.defaultName(next[index].key) : String(trimmed.prefix(24))
        return commit(next)
    }

    @discardableResult
    func toggleShow(_ id: String) -> String? {
        guard let index = keys.firstIndex(where: { $0.id == id }) else { return nil }
        var next = keys
        if next[index].show {
            guard next.filter({ $0.show }).count > 1 else { return "至少保留 1 个显示的 Key" }
            next[index].show = false
        } else {
            guard next.filter({ $0.show }).count < 3 else { return "最多同时显示 3 个 Key" }
            next[index].show = true
        }
        if let error = commit(next) { return error }
        refreshAll()
        return nil
    }

    func reloadKeys() {
        guard !isDemo else { return }
        do {
            keys = try KeychainStore.load()
            storageError = nil
            refreshAll()
        } catch {
            storageError = error.localizedDescription
        }
    }

    func copyKey(_ id: String) {
        guard let key = keys.first(where: { $0.id == id })?.key else { return }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(key, forType: .string)
    }

    func clearStats() {
        stats.clear()
        statsRevision += 1
    }

    func copyBalance() {
        guard let info = balance(for: primaryKey) else { return }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString("\(info.symbol)\(BalanceInfo.money(info.total))", forType: .string)
    }

    private func commit(_ next: [KeyItem]) -> String? {
        if !isDemo {
            do {
                try KeychainStore.save(next)
            } catch {
                storageError = error.localizedDescription
                return error.localizedDescription
            }
        }
        keys = next
        storageError = nil
        return nil
    }

    // MARK: - 窗口动作

    func openSettings(_ section: SettingsSection = .general) {
        pendingSection = section
        openSettingsAction?(section)
    }

    func togglePanel() {
        togglePanelAction?()
    }

    func setPanelVisible(_ visible: Bool) {
        if panelVisible != visible { panelVisible = visible }
    }

    // MARK: - 演示数据

    private func seedDemoData() {
        let now = Date()
        let day = StatsStore.dayKey(now)
        var first = KeyStat()
        first.days = [
            StatsStore.dayKey(now.addingTimeInterval(-6 * 86400)): 1.24,
            StatsStore.dayKey(now.addingTimeInterval(-5 * 86400)): 0.42,
            StatsStore.dayKey(now.addingTimeInterval(-4 * 86400)): 0,
            StatsStore.dayKey(now.addingTimeInterval(-3 * 86400)): 4.61,
            StatsStore.dayKey(now.addingTimeInterval(-2 * 86400)): 2.18,
            StatsStore.dayKey(now.addingTimeInterval(-86400)): 3.06,
            day: 0.86
        ]
        let dayTotals: [Double] = [1.24, 0.42, 0, 4.61, 2.18, 3.06, 0.86]
        for offset in 0..<7 {
            let total = dayTotals[offset]
            guard total > 0 else { continue }
            let date = now.addingTimeInterval(Double(offset - 6) * 86400)
            let key = StatsStore.dayKey(date)
            var weights: [Double] = []
            for hourOfDay in 0..<24 {
                let wave = sin(Double(hourOfDay + offset * 3) * 0.55)
                weights.append(0.12 + max(0, wave) * max(0, wave))
            }
            let sum = weights.reduce(0, +)
            for hourOfDay in 0..<24 {
                let value = (total * weights[hourOfDay] / sum).rounded(to: 3)
                if value > 0 {
                    first.hours["\(key) \(String(format: "%02d", hourOfDay))"] = value
                }
            }
        }
        first.last = 9.74
        var second = KeyStat()
        second.days = [day: 1.02]
        second.last = 23.47
        var third = KeyStat()
        third.days = [day: 0.12]
        third.last = 3.82
        stats.seed([keys[0].id: first, keys[1].id: second, keys[2].id: third])
    }

    private func applyDemoBalances() {
        guard keys.count >= 3 else { return }
        let infos: [BalanceInfo] = [
            BalanceInfo(currency: "CNY", total: 9.31, granted: 0, topped: 9.31, available: true),
            BalanceInfo(currency: "CNY", total: 23.47, granted: 5.0, topped: 18.47, available: true),
            BalanceInfo(currency: "CNY", total: 3.82, granted: 0, topped: 3.82, available: true)
        ]
        for (index, key) in keys.enumerated() where index < infos.count {
            balances[key.id] = infos[index]
        }
        statsRevision += 1
        lastUpdate = Date()
    }

}
