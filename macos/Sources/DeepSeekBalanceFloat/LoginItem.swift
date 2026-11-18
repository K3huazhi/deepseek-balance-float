import Foundation
import ServiceManagement

/// 开机自启。优先使用 macOS 13+ 的 `SMAppService`（会出现在「系统设置 → 通用 → 登录项」）；
/// 对于未签名的本地构建，若系统拒绝注册则自动回退为当前用户的 LaunchAgent。
enum LoginItem {
    static let agentLabel = "com.k3huazhi.DeepSeekBalanceFloat"

    static var agentURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(agentLabel).plist")
    }

    static var isEnabled: Bool {
        if SMAppService.mainApp.status == .enabled { return true }
        return FileManager.default.fileExists(atPath: agentURL.path)
    }

    /// 返回 nil 表示完全成功，否则返回一条可展示给用户的说明。
    @discardableResult
    static func setEnabled(_ enabled: Bool) -> String? {
        if enabled {
            do {
                if SMAppService.mainApp.status != .enabled {
                    try SMAppService.mainApp.register()
                }
                removeAgent()
                return nil
            } catch {
                do {
                    try installAgent()
                    return "系统登录项不可用，已改用 LaunchAgent 实现开机自启。"
                } catch {
                    return "无法开启开机自启：\(error.localizedDescription)"
                }
            }
        } else {
            if SMAppService.mainApp.status != .notRegistered {
                try? SMAppService.mainApp.unregister()
            }
            removeAgent()
            return nil
        }
    }

    private static func installAgent() throws {
        let bundle = Bundle.main.bundlePath
        let program: [String]
        if bundle.hasSuffix(".app") {
            program = ["/usr/bin/open", "-a", bundle]
        } else {
            program = [Bundle.main.executablePath ?? CommandLine.arguments[0]]
        }
        let plist: [String: Any] = [
            "Label": agentLabel,
            "ProgramArguments": program,
            "RunAtLoad": true,
            "KeepAlive": false,
            "ProcessType": "Interactive",
            "LimitLoadToSessionType": "Aqua"
        ]
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try FileManager.default.createDirectory(at: agentURL.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        try data.write(to: agentURL, options: .atomic)
    }

    private static func removeAgent() {
        try? FileManager.default.removeItem(at: agentURL)
    }
}
