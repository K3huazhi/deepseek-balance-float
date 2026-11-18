import Foundation

struct APIError: LocalizedError {
    let message: String
    var status: Int?
    var retryAfter: TimeInterval?

    init(message: String, status: Int? = nil, retryAfter: TimeInterval? = nil) {
        self.message = message
        self.status = status
        self.retryAfter = retryAfter
    }

    var errorDescription: String? { message }
}

/// 拒绝一切 HTTP 重定向，避免把 Authorization 带到其他主机。
private final class RedirectBlocker: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession,
                    task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

private struct BalanceEnvelope: Decodable {
    let isAvailable: Bool?
    let balanceInfos: [BalanceEntry]

    enum CodingKeys: String, CodingKey {
        case isAvailable = "is_available"
        case balanceInfos = "balance_infos"
    }
}

private struct BalanceEntry: Decodable {
    let currency: String
    let total: Double
    let granted: Double
    let topped: Double

    enum CodingKeys: String, CodingKey {
        case currency
        case total = "total_balance"
        case granted = "granted_balance"
        case topped = "topped_up_balance"
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        currency = (try? c.decode(String.self, forKey: .currency)) ?? "CNY"
        total = Self.number(c, .total)
        granted = Self.number(c, .granted)
        topped = Self.number(c, .topped)
    }

    private static func number(_ c: KeyedDecodingContainer<CodingKeys>, _ key: CodingKeys) -> Double {
        if let string = try? c.decode(String.self, forKey: key), let value = Double(string) {
            return value
        }
        if let value = try? c.decode(Double.self, forKey: key) {
            return value
        }
        return 0
    }
}

enum ApiClient {
    private static let redirectBlocker = RedirectBlocker()

    /// 共享 session：复用连接、统一超时与重定向策略。
    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 15
        configuration.timeoutIntervalForResource = 20
        configuration.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        configuration.waitsForConnectivity = false
        return URLSession(configuration: configuration, delegate: redirectBlocker, delegateQueue: nil)
    }()

    /// 校验接口地址：非 loopback 一律要求 HTTPS。
    static func validatedURL(_ urlString: String) throws -> URL {
        let trimmed = urlString.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let url = URL(string: trimmed.isEmpty ? AppSettings.defaultURL : trimmed),
              let scheme = url.scheme?.lowercased(),
              scheme == "https" || scheme == "http",
              let host = url.host, !host.isEmpty else {
            throw APIError(message: "接口地址无效")
        }
        if scheme == "http", !isLoopback(host) {
            throw APIError(message: "接口地址必须使用 HTTPS（localhost 除外）")
        }
        return url
    }

    private static func isLoopback(_ host: String) -> Bool {
        let h = host.lowercased()
        if h == "localhost" || h == "::1" || h.hasSuffix(".localhost") {
            return true
        }
        // 只接受 127.0.0.0/8 的数字地址，避免 "127.example.com" 被当成 loopback。
        let parts = h.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4, parts[0] == "127" else { return false }
        return parts.dropFirst().allSatisfy { part in
            guard let value = Int(part) else { return false }
            return (0...255).contains(value)
        }
    }

    static func fetch(key: String, urlString: String) async throws -> BalanceInfo {
        let url = try validatedURL(urlString)

        var request = URLRequest(url: url)
        request.timeoutInterval = 15
        request.httpMethod = "GET"
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("DeepSeekBalanceFloat/1.0 (macOS)", forHTTPHeaderField: "User-Agent")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch let error as URLError {
            switch error.code {
            case .timedOut: throw APIError(message: "请求超时，请检查网络")
            case .notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost:
                throw APIError(message: "网络不可用")
            default: throw APIError(message: "网络错误：\(error.localizedDescription)")
            }
        }

        guard let http = response as? HTTPURLResponse else {
            throw APIError(message: "接口返回异常")
        }

        switch http.statusCode {
        case 200...299:
            break
        case 401:
            throw APIError(message: "Key 无效或已被删除", status: 401)
        case 402:
            throw APIError(message: "账户余额不足", status: 402)
        case 429:
            let retryAfter = http.value(forHTTPHeaderField: "Retry-After").flatMap(Double.init)
            throw APIError(message: "请求过于频繁", status: 429, retryAfter: retryAfter)
        default:
            throw APIError(message: "接口返回 \(http.statusCode)", status: http.statusCode)
        }

        return try decode(data)
    }

    /// 解析 DeepSeek 余额响应；单独暴露以便自检与调试。
    static func decode(_ data: Data) throws -> BalanceInfo {
        let envelope: BalanceEnvelope
        do {
            envelope = try JSONDecoder().decode(BalanceEnvelope.self, from: data)
        } catch {
            throw APIError(message: "无法解析接口返回")
        }
        guard let entry = envelope.balanceInfos.first else {
            throw APIError(message: "没有余额信息")
        }

        var info = BalanceInfo()
        info.currency = entry.currency.isEmpty ? "CNY" : entry.currency
        info.total = entry.total
        info.granted = entry.granted
        info.topped = entry.topped
        if let available = envelope.isAvailable { info.available = available }
        return info
    }
}
