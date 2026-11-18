import Foundation
import ServiceManagement

/// 开机自启。只使用 macOS 13+ 的 `SMAppService`（「系统设置 → 通用 → 登录项」），
/// 不再写 `~/Library/LaunchAgents`，避免绕过系统设置、卸载后残留。
enum LoginItem {
    /// 旧版本可能写入的 LaunchAgent 路径；仅用于清理，不再创建。
    private static var legacyAgentURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/com.k3huazhi.DeepSeekBalanceFloat.plist")
    }

    static var status: SMAppService.Status {
        SMAppService.mainApp.status
    }

    static var isEnabled: Bool {
        status == .enabled || status == .requiresApproval
    }

    static var requiresApproval: Bool {
        status == .requiresApproval
    }

    /// 返回 nil 表示成功，否则返回一条可展示给用户的说明。
    @discardableResult
    static func setEnabled(_ enabled: Bool) -> String? {
        cleanupLegacyAgent()
        if enabled {
            switch status {
            case .enabled:
                return nil
            case .requiresApproval:
                return "登录项已注册，正在等待系统批准：请到「系统设置 → 通用 → 登录项」中允许。"
            default:
                do {
                    try SMAppService.mainApp.register()
                } catch {
                    return "无法自动注册登录项：\(error.localizedDescription)\n请在「系统设置 → 通用 → 登录项」中手动添加本应用。"
                }
                if status == .requiresApproval {
                    return "登录项已注册，正在等待系统批准：请到「系统设置 → 通用 → 登录项」中允许。"
                }
                return nil
            }
        }

        if status != .notRegistered {
            try? SMAppService.mainApp.unregister()
        }
        return nil
    }

    /// 清理旧版可能留下的 LaunchAgent；新版本不会再创建。
    static func cleanupLegacyAgent() {
        try? FileManager.default.removeItem(at: legacyAgentURL)
    }
}
