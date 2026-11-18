import Foundation
import Combine

enum ThemeMode: String, CaseIterable, Codable, Identifiable {
    case system, light, dark

    var id: String { rawValue }

    var title: String {
        switch self {
        case .system: return "自动"
        case .light: return "亮色"
        case .dark: return "暗色"
        }
    }

    var symbol: String {
        switch self {
        case .system: return "circle.lefthalf.filled"
        case .light: return "sun.max.fill"
        case .dark: return "moon.fill"
        }
    }
}

enum SettingsSection: String, CaseIterable, Identifiable, Hashable {
    case general, keys, usage, about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: return "通用"
        case .keys: return "API Key"
        case .usage: return "消耗记录"
        case .about: return "关于"
        }
    }

    var symbol: String {
        switch self {
        case .general: return "gearshape"
        case .keys: return "key.horizontal"
        case .usage: return "chart.bar.xaxis"
        case .about: return "info.circle"
        }
    }
}

/// 设置存放在 UserDefaults（`com.k3huazhi.DeepSeekBalanceFloat` 域），
/// 不生成散落的配置文件；API Key 单独存钥匙串。
final class AppSettings: ObservableObject {
    static let defaultURL = "https://api.deepseek.com/user/balance"

    @Published var theme: ThemeMode {
        didSet { defaults.set(theme.rawValue, forKey: Keys.theme) }
    }

    /// 自动模式下是否按日出日落切换（默认跟随系统外观）。
    @Published var solarAuto: Bool {
        didSet { defaults.set(solarAuto, forKey: Keys.solarAuto) }
    }

    @Published var pinned: Bool {
        didSet { defaults.set(pinned, forKey: Keys.pinned) }
    }

    @Published var expanded: Bool {
        didSet { defaults.set(expanded, forKey: Keys.expanded) }
    }

    @Published var showMenuBar: Bool {
        didSet { defaults.set(showMenuBar, forKey: Keys.showMenuBar) }
    }

    @Published var showDockIcon: Bool {
        didSet { defaults.set(showDockIcon, forKey: Keys.showDockIcon) }
    }

    @Published var apiURL: String {
        didSet { defaults.set(apiURL, forKey: Keys.apiURL) }
    }

    /// 实际状态由 LoginItem 读取，这里仅用于界面绑定。
    @Published var launchAtLogin: Bool = false

    @Published var latitude: Double? {
        didSet {
            if let latitude { defaults.set(latitude, forKey: Keys.latitude) }
            else { defaults.removeObject(forKey: Keys.latitude) }
        }
    }

    @Published var longitude: Double? {
        didSet {
            if let longitude { defaults.set(longitude, forKey: Keys.longitude) }
            else { defaults.removeObject(forKey: Keys.longitude) }
        }
    }

    private enum Keys {
        static let theme = "theme"
        static let solarAuto = "solarAuto"
        static let pinned = "pinned"
        static let expanded = "expanded"
        static let showMenuBar = "showMenuBar"
        static let showDockIcon = "showDockIcon"
        static let apiURL = "apiURL"
        static let latitude = "latitude"
        static let longitude = "longitude"
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        theme = ThemeMode(rawValue: defaults.string(forKey: Keys.theme) ?? "") ?? .system
        solarAuto = defaults.object(forKey: Keys.solarAuto) as? Bool ?? false
        pinned = defaults.object(forKey: Keys.pinned) as? Bool ?? true
        expanded = defaults.object(forKey: Keys.expanded) as? Bool ?? false
        showMenuBar = defaults.object(forKey: Keys.showMenuBar) as? Bool ?? true
        showDockIcon = defaults.object(forKey: Keys.showDockIcon) as? Bool ?? true
        let storedURL = defaults.string(forKey: Keys.apiURL) ?? ""
        apiURL = storedURL.isEmpty ? Self.defaultURL : storedURL
        latitude = defaults.object(forKey: Keys.latitude) as? Double
        longitude = defaults.object(forKey: Keys.longitude) as? Double
    }
}
