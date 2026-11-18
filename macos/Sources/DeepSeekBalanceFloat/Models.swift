import Foundation
import CryptoKit

// MARK: - 余额

struct BalanceInfo: Sendable, Equatable {
    var currency: String = "CNY"
    var total: Double = 0
    var granted: Double = 0
    var topped: Double = 0
    var available: Bool = true

    var symbol: String { currency == "CNY" ? "¥" : (currency.isEmpty ? "¥" : currency + " ") }

    var subtitle: String {
        if !available { return "余额不足，API 调用会被拒绝" }
        if granted > 0.0001 { return "可用余额 · 赠送 \(symbol)\(Self.money(granted))" }
        return "可用余额"
    }

    static func money(_ value: Double) -> String {
        String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), value)
    }
}

// MARK: - Key

struct KeyItem: Codable, Identifiable, Equatable, Sendable {
    var key: String
    var show: Bool
    var name: String

    var id: String { KeyIdentity.uid(key) }

    enum CodingKeys: String, CodingKey {
        case key = "k"
        case show
        case name
    }

    init(key: String, show: Bool = true, name: String? = nil) {
        self.key = key
        self.show = show
        let trimmed = name?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        self.name = trimmed.isEmpty ? KeyIdentity.defaultName(key) : trimmed
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let raw = try c.decodeIfPresent(String.self, forKey: .key) ?? ""
        key = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        show = try c.decodeIfPresent(Bool.self, forKey: .show) ?? true
        let decodedName = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        let trimmed = decodedName.trimmingCharacters(in: .whitespacesAndNewlines)
        name = trimmed.isEmpty ? KeyIdentity.defaultName(key) : trimmed
    }
}

enum KeyIdentity {
    static func defaultName(_ key: String) -> String {
        guard !key.isEmpty else { return "未命名" }
        var body = key
        if body.hasPrefix("sk-") { body.removeFirst(3) }
        if body.count >= 6 { return String(body.prefix(6)) }
        return body.isEmpty ? "未命名" : body
    }

    /// 与 Windows 版一致的掩码规则：保留两端，隐藏中间六位。
    static func mask(_ key: String) -> String {
        let trimmed = key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count > 12 else { return trimmed }
        let cut = (trimmed.count - 6) / 2
        let head = trimmed.prefix(cut)
        let tail = trimmed.dropFirst(cut + 6)
        return "\(head)******\(tail)"
    }

    /// 6 字节 SHA-1 指纹，用作统计记录的主键（与 Windows 版算法一致）。
    static func uid(_ key: String) -> String {
        let digest = Insecure.SHA1.hash(data: Data(key.utf8))
        return digest.prefix(6).map { String(format: "%02x", $0) }.joined()
    }

    static func normalized(_ raw: String) -> String {
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        s = s.replacingOccurrences(of: "\n", with: "")
        s = s.replacingOccurrences(of: "\r", with: "")
        if s.lowercased().hasPrefix("bearer ") {
            s = String(s.dropFirst(7)).trimmingCharacters(in: .whitespaces)
        }
        return s
    }
}

// MARK: - Drop 动画

struct DropEvent: Equatable, Identifiable {
    let id = UUID()
    let uid: String
    let amount: Double
    let symbol: String
}

// MARK: - 展示辅助

enum BalanceTone: Equatable {
    case positive, warning, negative

    static func tone(total: Double, available: Bool) -> BalanceTone {
        if !available { return .negative }
        if total > 5 { return .positive }
        if total > 0.5 { return .warning }
        return .negative
    }
}
