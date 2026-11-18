import AppKit
import SwiftUI

@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    private let model: AppModel
    var onShow: (() -> Void)?
    var onClose: (() -> Void)?

    init(model: AppModel) {
        self.model = model
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 900, height: 620),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "DeepSeek 余额设置"
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 860, height: 580)
        window.contentViewController = NSHostingController(rootView: SettingsRootView(model: model))
        window.setContentSize(NSSize(width: 900, height: 620))
        super.init(window: window)
        window.delegate = self
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func show(section: SettingsSection) {
        model.pendingSection = section
        onShow?()
        showWindow(nil)
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
        if let window {
            window.setContentSize(NSSize(width: 900, height: 620))
            if let screen = NSScreen.main ?? NSScreen.screens.first {
                let visible = screen.visibleFrame
                let size = window.frame.size
                window.setFrameOrigin(NSPoint(x: visible.midX - size.width / 2,
                                             y: visible.midY - size.height / 2))
            } else {
                window.center()
            }
        }
        if let window {
            window.collectionBehavior.insert(.moveToActiveSpace)
        }
    }

    func windowWillClose(_ notification: Notification) {
        onClose?()
    }
}
