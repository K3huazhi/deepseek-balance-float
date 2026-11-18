import AppKit
import SwiftUI
import Combine

@MainActor
final class PanelController {
    let panel: NSPanel
    private let model: AppModel
    private var hosting: NSHostingView<PanelView>
    private var cancellables = Set<AnyCancellable>()
    private let defaults = UserDefaults.standard
    private var isSizingProgrammatically = false
    private var saveWorkItem: DispatchWorkItem?
    private var observers: [NSObjectProtocol] = []
    private var rightClickMonitor: Any?

    private static let positionsKey = "panelPositions"
    private static let lastScreenKey = "panelLastScreen"

    init(model: AppModel) {
        self.model = model

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 300, height: model.panelHeight),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .utilityWindow
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.level = model.settings.pinned ? .floating : .normal
        panel.becomesKeyOnlyIfNeeded = true
        panel.isMovable = false
        panel.tabbingMode = .disallowed
        panel.title = "DeepSeek 余额"

        hosting = NSHostingView(rootView: PanelView(model: model))
        hosting.autoresizingMask = [.width, .height]
        panel.contentView = hosting
        self.panel = panel

        restorePosition()
        panel.setFrame(NSRect(origin: panel.frame.origin, size: NSSize(width: 300, height: model.panelHeight)),
                       display: false)

        model.objectWillChange
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.updateSizeAndLevel() }
            }
            .store(in: &cancellables)

        observers.append(NotificationCenter.default.addObserver(forName: .panelDidMove, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.scheduleSave() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: .resetPanelPosition, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.resetPosition() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                WindowSnapper.clamp(self.panel)
                self.scheduleSave()
            }
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: panel, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.scheduleSave() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: panel, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.scheduleSave() }
        })
        rightClickMonitor = NSEvent.addLocalMonitorForEvents(matching: .rightMouseDown) { [weak self] event in
            if let self, event.window === self.panel {
                self.model.refreshAll()
            }
            return event
        }
    }

    deinit {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
        if let rightClickMonitor { NSEvent.removeMonitor(rightClickMonitor) }
    }

    var isVisible: Bool { panel.isVisible }

    func show(makeKey: Bool = false) {
        updateSize(animated: false)
        panel.orderFrontRegardless()
        if makeKey { panel.makeKeyAndOrderFront(nil) }
        model.setPanelVisible(true)
    }

    func hide() {
        panel.orderOut(nil)
        model.setPanelVisible(false)
    }

    func toggle() {
        if panel.isVisible { hide() } else { show() }
    }

    func updateSizeAndLevel() {
        panel.level = model.settings.pinned ? .floating : .normal
        updateSize(animated: true)
    }

    private func updateSize(animated: Bool) {
        let target = ceil(model.panelHeight)
        guard abs(panel.frame.height - target) > 0.5, !isSizingProgrammatically else { return }
        var frame = panel.frame
        let top = frame.maxY
        frame.size = NSSize(width: 300, height: target)
        frame.origin.y = top - target
        if let screen = panel.screen ?? NSScreen.main {
            let visible = screen.visibleFrame
            if frame.minY < visible.minY { frame.origin.y = visible.minY }
            if frame.maxY > visible.maxY { frame.origin.y = visible.maxY - frame.height }
        }
        isSizingProgrammatically = true
        panel.setFrame(frame, display: true, animate: animated)
        isSizingProgrammatically = false
        scheduleSave()
    }

    // MARK: - 位置

    private func screenKey(_ screen: NSScreen) -> String {
        let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber
        return "screen-\(number?.intValue ?? 0)"
    }

    private func restorePosition() {
        let screens = NSScreen.screens
        let lastKey = defaults.string(forKey: Self.lastScreenKey)
        let screen = screens.first(where: { screenKey($0) == lastKey }) ?? NSScreen.main ?? screens.first
        guard let screen else { return }
        let visible = screen.visibleFrame
        let positions = defaults.dictionary(forKey: Self.positionsKey) as? [String: String] ?? [:]
        var origin: NSPoint?
        if let lastKey, let text = positions[lastKey] {
            let parts = text.split(separator: ",").compactMap { Double($0) }
            if parts.count == 2 { origin = NSPoint(x: parts[0], y: parts[1]) }
        }
        if origin == nil {
            origin = NSPoint(x: visible.maxX - 300 - 28,
                             y: visible.maxY - model.panelHeight - 28)
        }
        var frame = NSRect(origin: origin!, size: NSSize(width: 300, height: model.panelHeight))
        frame.origin.x = min(max(frame.origin.x, visible.minX), visible.maxX - frame.width)
        frame.origin.y = min(max(frame.origin.y, visible.minY), visible.maxY - frame.height)
        panel.setFrame(frame, display: false)
    }

    private func scheduleSave() {
        saveWorkItem?.cancel()
        let item = DispatchWorkItem { [weak self] in self?.savePosition() }
        saveWorkItem = item
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35, execute: item)
    }

    func savePosition() {
        guard let screen = panel.screen ?? NSScreen.main else { return }
        var positions = defaults.dictionary(forKey: Self.positionsKey) as? [String: String] ?? [:]
        positions[screenKey(screen)] = "\(panel.frame.origin.x),\(panel.frame.origin.y)"
        defaults.set(positions, forKey: Self.positionsKey)
        defaults.set(screenKey(screen), forKey: Self.lastScreenKey)
    }

    func resetPosition() {
        guard let screen = panel.screen ?? NSScreen.main else { return }
        let visible = screen.visibleFrame
        var frame = panel.frame
        frame.origin = NSPoint(x: visible.maxX - frame.width - 28,
                               y: visible.maxY - frame.height - 28)
        panel.setFrame(frame, display: true, animate: true)
        savePosition()
    }
}
