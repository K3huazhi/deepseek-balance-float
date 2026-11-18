import AppKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuItemValidation {
    private let model = AppModel.shared
    private var panel: PanelController!
    private var settingsWindow: SettingsWindowController!
    private var statusBar: StatusBarController!
    private var cancellables = Set<AnyCancellable>()
    private var panelWasVisibleBeforeSettings = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard !terminateIfAlreadyRunning() else { return }

        buildMainMenu()
        applyActivationPolicy()

        panel = PanelController(model: model)
        settingsWindow = SettingsWindowController(model: model)
        statusBar = StatusBarController(model: model)

        model.openSettingsAction = { [weak self] section in
            self?.settingsWindow.show(section: section)
        }
        model.showPanelAction = { [weak self] in self?.panel.show() }
        model.hidePanelAction = { [weak self] in self?.panel.hide() }
        model.togglePanelAction = { [weak self] in self?.panel.toggle() }
        model.refreshMenuBarAction = { [weak self] in self?.statusBar.setVisible(AppModel.shared.settings.showMenuBar) }

        settingsWindow.onShow = { [weak self] in
            guard let self else { return }
            if self.panel.isVisible || self.model.panelVisible {
                self.panelWasVisibleBeforeSettings = true
            }
            self.panel.hide()
        }
        settingsWindow.onClose = { [weak self] in
            guard let self else { return }
            if self.panelWasVisibleBeforeSettings {
                self.panel.show()
            }
            self.panelWasVisibleBeforeSettings = false
        }

        model.settings.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.applySettings() }
            }
            .store(in: &cancellables)

        applySettings()
        panel.show()
        model.start()

        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.model.refreshAll() }
        }

        if model.keys.isEmpty {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.55) { [weak self] in
                guard let self, self.model.keys.isEmpty else { return }
                self.settingsWindow.show(section: .keys)
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        model.stop()
        panel?.savePosition()
        model.stats.save()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
        true
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        panel?.show()
        return true
    }

    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        let menu = NSMenu()
        let balance = NSMenuItem(title: model.panelVisible ? "隐藏悬浮窗" : "显示悬浮窗",
                                 action: #selector(togglePanel), keyEquivalent: "")
        balance.target = self
        menu.addItem(balance)
        let refresh = NSMenuItem(title: "刷新余额", action: #selector(refreshBalance), keyEquivalent: "")
        refresh.target = self
        menu.addItem(refresh)
        menu.addItem(.separator())
        let settings = NSMenuItem(title: "设置…", action: #selector(openSettingsFromMenu), keyEquivalent: "")
        settings.target = self
        menu.addItem(settings)
        return menu
    }

    // MARK: - 设置应用

    private func applySettings() {
        let settings = model.settings
        if !settings.showMenuBar && !settings.showDockIcon {
            settings.showMenuBar = true
        }
        applyActivationPolicy()
        statusBar?.setVisible(settings.showMenuBar)
        if !settings.showDockIcon && !settings.showMenuBar {
            settings.showMenuBar = true
        }
    }

    private func applyActivationPolicy() {
        let policy: NSApplication.ActivationPolicy = model.settings.showDockIcon ? .regular : .accessory
        if NSApp.activationPolicy() != policy {
            NSApp.setActivationPolicy(policy)
        }
    }

    private func terminateIfAlreadyRunning() -> Bool {
        guard let bundleID = Bundle.main.bundleIdentifier,
              !CommandLine.arguments.contains("--allow-multi") else { return false }
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
            .filter { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }
        if let other = others.first {
            other.activate()
            NSApp.terminate(nil)
            return true
        }
        return false
    }

    // MARK: - 主菜单

    private func buildMainMenu() {
        let mainMenu = NSMenu()

        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)
        let appMenu = NSMenu(title: "DeepSeek 余额")
        appItem.submenu = appMenu

        let about = NSMenuItem(title: "关于 DeepSeek 余额", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        appMenu.addItem(about)

        let settings = NSMenuItem(title: "设置…", action: #selector(openSettingsFromMenu), keyEquivalent: ",")
        settings.target = self
        appMenu.addItem(settings)
        appMenu.addItem(.separator())

        let servicesMenu = NSMenu(title: "服务")
        let servicesItem = NSMenuItem(title: "服务", action: nil, keyEquivalent: "")
        servicesItem.submenu = servicesMenu
        appMenu.addItem(servicesItem)
        NSApp.servicesMenu = servicesMenu
        appMenu.addItem(.separator())

        appMenu.addItem(withTitle: "隐藏 DeepSeek 余额",
                        action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let hideOthers = NSMenuItem(title: "隐藏其他",
                                    action: #selector(NSApplication.hideOtherApplications(_:)),
                                    keyEquivalent: "h")
        hideOthers.keyEquivalentModifierMask = [.command, .option]
        appMenu.addItem(hideOthers)
        appMenu.addItem(withTitle: "显示全部",
                        action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "退出 DeepSeek 余额",
                        action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        let editItem = NSMenuItem()
        mainMenu.addItem(editItem)
        let editMenu = NSMenu(title: "编辑")
        editItem.submenu = editMenu
        editMenu.addItem(withTitle: "撤销", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = NSMenuItem(title: "重做", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        editMenu.addItem(redo)
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: "剪切", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "拷贝", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "粘贴", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "删除", action: #selector(NSText.delete(_:)), keyEquivalent: "")
        editMenu.addItem(withTitle: "全选", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")

        let viewItem = NSMenuItem()
        mainMenu.addItem(viewItem)
        let viewMenu = NSMenu(title: "显示")
        viewItem.submenu = viewMenu
        let refresh = NSMenuItem(title: "刷新余额", action: #selector(refreshBalance), keyEquivalent: "r")
        refresh.target = self
        viewMenu.addItem(refresh)
        let expand = NSMenuItem(title: "展开/收起详情", action: #selector(toggleExpanded), keyEquivalent: "e")
        expand.target = self
        viewMenu.addItem(expand)
        viewMenu.addItem(.separator())
        let togglePanel = NSMenuItem(title: "显示/隐藏悬浮窗", action: #selector(togglePanel), keyEquivalent: "d")
        togglePanel.keyEquivalentModifierMask = [.command, .shift]
        togglePanel.target = self
        viewMenu.addItem(togglePanel)

        let windowItem = NSMenuItem()
        mainMenu.addItem(windowItem)
        let windowMenu = NSMenu(title: "窗口")
        windowItem.submenu = windowMenu
        windowMenu.addItem(withTitle: "最小化", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "缩放", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        windowMenu.addItem(.separator())
        windowMenu.addItem(withTitle: "前置全部窗口",
                           action: #selector(NSApplication.arrangeInFront(_:)), keyEquivalent: "")
        NSApp.windowsMenu = windowMenu

        NSApp.mainMenu = mainMenu
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        if menuItem.action == #selector(toggleExpanded) {
            menuItem.title = model.settings.expanded ? "收起详情" : "展开详情"
            return !model.isMulti
        }
        if menuItem.action == #selector(togglePanel) {
            menuItem.title = model.panelVisible ? "隐藏悬浮窗" : "显示悬浮窗"
        }
        return true
    }

    // MARK: - 菜单动作

    @objc private func showAbout() {
        settingsWindow.show(section: .about)
    }

    @objc private func openSettingsFromMenu() {
        settingsWindow.show(section: .general)
    }

    @objc private func refreshBalance() {
        model.refreshAll()
    }

    @objc private func toggleExpanded() {
        guard !model.isMulti else { return }
        model.settings.expanded.toggle()
    }

    @objc private func togglePanel() {
        panel.toggle()
    }
}
