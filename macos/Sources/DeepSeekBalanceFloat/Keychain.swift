import Foundation
import Security

enum KeychainError: LocalizedError {
    case status(OSStatus, String)
    case encoding(String)

    var errorDescription: String? {
        switch self {
        case let .status(code, context):
            let message = SecCopyErrorMessageString(code, nil) as String? ?? "错误 \(code)"
            return "钥匙串\(context)失败：\(message)"
        case let .encoding(message):
            return message
        }
    }
}

/// API Key 只写入 macOS 钥匙串（登录钥匙串，按用户隔离），不写入偏好设置或磁盘文件。
enum KeychainStore {
    static let service = "com.k3huazhi.DeepSeekBalanceFloat"
    static let account = "api_key"
    static let maxKeys = 10

    private static func baseQuery(service: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
    }

    static func load(service: String = KeychainStore.service) throws -> [KeyItem] {
        var query = baseQuery(service: service)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return [] }
        guard status == errSecSuccess else {
            throw KeychainError.status(status, "读取")
        }
        guard let data = result as? Data else { return [] }

        if let items = try? JSONDecoder().decode([KeyItem].self, from: data) {
            return Array(items.filter { !$0.key.isEmpty }.prefix(maxKeys))
        }

        // 兼容 Windows 版/早期版本写入的单个 Key（纯文本）。
        if let legacy = String(data: data, encoding: .utf8) {
            let key = legacy.trimmingCharacters(in: .whitespacesAndNewlines)
            if !key.isEmpty, !key.hasPrefix("[") {
                return [KeyItem(key: key)]
            }
        }
        return []
    }

    static func delete(service: String = KeychainStore.service) throws {
        let status = SecItemDelete(baseQuery(service: service) as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            throw KeychainError.status(status, "删除")
        }
    }

    static func save(_ keys: [KeyItem], service: String = KeychainStore.service) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let data: Data
        do {
            data = try encoder.encode(Array(keys.prefix(maxKeys)))
        } catch {
            throw KeychainError.encoding("无法序列化 API Key：\(error.localizedDescription)")
        }

        var query = baseQuery(service: service)
        let attributes: [String: Any] = [kSecValueData as String: data]
        let updateStatus = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if updateStatus == errSecSuccess { return }
        if updateStatus != errSecItemNotFound {
            throw KeychainError.status(updateStatus, "更新")
        }

        query[kSecValueData as String] = data
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        query[kSecAttrLabel as String] = "DeepSeek 余额悬浮窗 API Key"
        let addStatus = SecItemAdd(query as CFDictionary, nil)
        guard addStatus == errSecSuccess else {
            throw KeychainError.status(addStatus, "写入")
        }
    }
}
