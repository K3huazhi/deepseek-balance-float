import AppKit
import Combine
import SwiftUI

enum BrandIcon {
    static func whale(pointSize: NSSize = NSSize(width: 17, height: 12.5)) -> NSImage {
        let image = NSImage(size: pointSize, flipped: true) { rect in
            let path = WhaleShape().path(in: rect)
            NSColor.black.setFill()
            NSBezierPath(cgPath: path.cgPath).fill()
            return true
        }
        image.isTemplate = true
        return image
    }
}

@MainActor
final class StatusBarController: NSObject, NSMenuDelegate {
    private let item: NSStatusItem
    private let model: AppModel
    private var cancellables = Set<AnyCancellable>()

    init(model: AppModel) {
        self.model = model
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()

        if let button = item.button {
            button.image = BrandIcon.whale()
            button.imagePosition = .imageLeading
            button.imageScaling = .scaleProportionallyDown
            button.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
            button.toolTip = "DeepSeek 余额"
        }
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        item.isVisible = model.settings.showMenuBar

        model.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.refreshButton() }
            }
            .store(in: &cancellables)

        refreshButton()
    }

    func setVisible(_ visible: Bool) {
        item.isVisible = visible
    }

    private func refreshButton() {
        guard let button = item.button else { return }
        let visible = model.visibleKeys
        if model.isMulti {
            let sum = visible.reduce(0.0) { $0 + (model.balances[$1.id]?.total ?? 0) }
            button.title = " ¥" + BalanceInfo.money(sum)
            button.toolTip = "DeepSeek 余额 · \(visible.count) 个 Key · 合计 ¥\(BalanceInfo.money(sum))"
        } else if let key = model.primaryKey, let info = model.balances[key.id] {
            button.title = " \(info.symbol)\(BalanceInfo.money(info.total))"
            var tip = "DeepSeek 余额 \(info.symbol)\(BalanceInfo.money(info.total))"
            if let error = model.errors[key.id] { tip += "\n\(error)" }
            if model.lastUpdate != nil { tip += "\n更新于 \(model.lastUpdateText)" }
            button.toolTip = tip
        } else {
            button.title = " —"
            let error = model.primaryKey.flatMap { model.errors[$0.id] }
            button.toolTip = error ?? "DeepSeek 余额 · 正在获取"
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let visible = model.visibleKeys

        let header: String
        if model.isMulti {
            let sum = visible.reduce(0.0) { $0 + (model.balances[$1.id]?.total ?? 0) }
            header = "\(visible.count) 个 Key · 合计 ¥\(BalanceInfo.money(sum))"
        } else if let key = model.primaryKey, let info = model.balances[key.id] {
            header = "可用余额 \(info.symbol)\(BalanceInfo.money(info.total))"
        } else {
            header = "正在获取余额…"
        }
        let headerItem = NSMenuItem(title: header, action: nil, keyEquivalent: "")
        headerItem.isEnabled = false
        menu.addItem(headerItem)

        if let key = model.primaryKey, let error = model.errors[key.id] {
            let errorItem = NSMenuItem(title: error, action: nil, keyEquivalent: "")
            errorItem.isEnabled = false
            menu.addItem(errorItem)
        }
        if model.lastUpdate != nil {
            let updated = NSMenuItem(title: "更新于 \(model.lastUpdateText)", action: nil, keyEquivalent: "")
            updated.isEnabled = false
            menu.addItem(updated)
        }

        menu.addItem(.separator())
        addItem(menu, "刷新余额", #selector(refresh), "r")
        addItem(menu, model.panelVisible ? "隐藏悬浮窗" : "显示悬浮窗", #selector(togglePanel), "")
        if !model.isMulti {
            addItem(menu, model.settings.expanded ? "收起详情" : "展开详情", #selector(toggleExpanded), "")
        }
        menu.addItem(.separator())
        addItem(menu, "设置…", #selector(openGeneral), ",")
        addItem(menu, "API Key 管理…", #selector(openKeys), "")
        addItem(menu, "消耗记录…", #selector(openUsage), "")
        menu.addItem(.separator())
        addItem(menu, "退出 DeepSeek 余额", #selector(quit), "q")
    }

    private func addItem(_ menu: NSMenu, _ title: String, _ action: Selector, _ key: String) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        menu.addItem(item)
    }

    @objc private func refresh() { model.refreshAll() }
    @objc private func togglePanel() { model.togglePanel() }
    @objc private func toggleExpanded() {
        guard !model.isMulti else { return }
        model.settings.expanded.toggle()
    }
    @objc private func openGeneral() { model.openSettings(.general) }
    @objc private func openKeys() { model.openSettings(.keys) }
    @objc private func openUsage() { model.openSettings(.usage) }
    @objc private func quit() { NSApp.terminate(nil) }
}
