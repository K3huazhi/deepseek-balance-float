import AppKit

extension Notification.Name {
    static let panelDidMove = Notification.Name("DeepSeekBalancePanelDidMove")
    static let resetPanelPosition = Notification.Name("DeepSeekBalanceResetPanelPosition")
}

enum WindowSnapper {
    static let edgeThreshold: CGFloat = 12

    /// 吸附到所在屏幕的可见边缘，并保证窗口完整落在屏幕内。
    @discardableResult
    static func snap(_ window: NSWindow) -> NSRect {
        guard let screen = window.screen ?? NSScreen.main else { return window.frame }
        let visible = screen.visibleFrame
        var frame = window.frame

        if abs(frame.minX - visible.minX) <= edgeThreshold { frame.origin.x = visible.minX }
        if abs(frame.maxX - visible.maxX) <= edgeThreshold { frame.origin.x = visible.maxX - frame.width }
        if abs(frame.minY - visible.minY) <= edgeThreshold { frame.origin.y = visible.minY }
        if abs(frame.maxY - visible.maxY) <= edgeThreshold { frame.origin.y = visible.maxY - frame.height }

        frame.origin.x = min(max(frame.origin.x, visible.minX), visible.maxX - frame.width)
        frame.origin.y = min(max(frame.origin.y, visible.minY), visible.maxY - frame.height)
        return frame
    }

    static func clamp(_ window: NSWindow) {
        guard let screen = NSScreen.screens.first(where: { $0.frame.intersects(window.frame) }) ?? NSScreen.main else {
            return
        }
        let visible = screen.visibleFrame
        var frame = window.frame
        frame.origin.x = min(max(frame.origin.x, visible.minX), visible.maxX - frame.width)
        frame.origin.y = min(max(frame.origin.y, visible.minY), visible.maxY - frame.height)
        if frame != window.frame {
            window.setFrame(frame, display: true)
        }
    }
}
