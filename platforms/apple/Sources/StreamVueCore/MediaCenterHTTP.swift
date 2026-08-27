import Foundation

public enum MediaCenterHTTPMethod: String, Equatable, Sendable {
    case get = "GET"
    case post = "POST"
}

public struct MediaCenterHTTPRequest: Equatable, Sendable {
    public static let defaultMaximumResponseBytes = 16 * 1_024 * 1_024

    public let method: MediaCenterHTTPMethod
    public let url: URL
    public let headers: [String: String]
    public let body: Data?
    public let maximumResponseBytes: Int

    public init(
        method: MediaCenterHTTPMethod,
        url: URL,
        headers: [String: String] = [:],
        body: Data? = nil,
        maximumResponseBytes: Int = defaultMaximumResponseBytes
    ) {
        self.method = method
        self.url = url
        self.headers = headers
        self.body = body
        self.maximumResponseBytes = maximumResponseBytes
    }
}

public struct MediaCenterHTTPResponse: Equatable, Sendable {
    public let statusCode: Int
    public let headers: [String: String]
    public let body: Data

    public init(statusCode: Int, headers: [String: String] = [:], body: Data) {
        self.statusCode = statusCode
        self.headers = headers
        self.body = body
    }
}

public protocol MediaCenterHTTPClient: Sendable {
    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse
}

/// Ephemeral, bounded networking for media-center metadata. Redirects remain on
/// the same host and HTTPS is never downgraded to cleartext.
public final class URLSessionMediaCenterHTTPClient: NSObject, MediaCenterHTTPClient,
    URLSessionTaskDelegate, @unchecked Sendable {
    private let maximumRedirects: Int
    private let lock = NSLock()
    private var redirectCounts: [Int: Int] = [:]
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 20
        configuration.timeoutIntervalForResource = 60
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.waitsForConnectivity = true
        configuration.httpMaximumConnectionsPerHost = 4
        configuration.httpCookieStorage = nil
        configuration.urlCredentialStorage = nil
        return URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
    }()

    public init(maximumRedirects: Int = 5) {
        self.maximumRedirects = max(0, maximumRedirects)
    }

    public func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        guard request.maximumResponseBytes > 0,
              let scheme = request.url.scheme?.lowercased(),
              ["http", "https"].contains(scheme),
              request.url.host?.isEmpty == false else {
            throw MediaCenterError.invalidBaseURL
        }
        var urlRequest = URLRequest(url: request.url)
        urlRequest.httpMethod = request.method.rawValue
        urlRequest.httpBody = request.body
        for (name, value) in request.headers where MediaCenterHeaderPolicy.isHeaderName(name) {
            guard !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
                throw MediaCenterError.invalidResponse
            }
            urlRequest.setValue(value, forHTTPHeaderField: name)
        }

        let (bytes, response) = try await session.bytes(for: urlRequest)
        guard let response = response as? HTTPURLResponse else {
            throw MediaCenterError.invalidResponse
        }
        if response.expectedContentLength > Int64(request.maximumResponseBytes) {
            throw MediaCenterError.responseTooLarge(maximumBytes: request.maximumResponseBytes)
        }
        var body = Data()
        if response.expectedContentLength > 0 {
            body.reserveCapacity(min(Int(response.expectedContentLength), request.maximumResponseBytes))
        }
        for try await byte in bytes {
            guard body.count < request.maximumResponseBytes else {
                throw MediaCenterError.responseTooLarge(maximumBytes: request.maximumResponseBytes)
            }
            body.append(byte)
        }
        var responseHeaders: [String: String] = [:]
        for (name, value) in response.allHeaderFields {
            guard let name = name as? String else { continue }
            responseHeaders[name] = String(describing: value)
        }
        return MediaCenterHTTPResponse(
            statusCode: response.statusCode,
            headers: responseHeaders,
            body: body
        )
    }

    public func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        guard let current = response.url,
              let next = request.url,
              current.host?.caseInsensitiveCompare(next.host ?? "") == .orderedSame,
              effectivePort(current) == effectivePort(next),
              isAllowedSchemeTransition(from: current, to: next) else {
            completionHandler(nil)
            return
        }
        lock.lock()
        let count = (redirectCounts[task.taskIdentifier] ?? 0) + 1
        redirectCounts[task.taskIdentifier] = count
        lock.unlock()
        completionHandler(count <= maximumRedirects ? request : nil)
    }

    public func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didCompleteWithError error: (any Error)?
    ) {
        lock.lock()
        redirectCounts.removeValue(forKey: task.taskIdentifier)
        lock.unlock()
    }

    private func isAllowedSchemeTransition(from current: URL, to next: URL) -> Bool {
        switch (current.scheme?.lowercased(), next.scheme?.lowercased()) {
        case ("http", "http"), ("http", "https"), ("https", "https"): true
        default: false
        }
    }

    private func effectivePort(_ url: URL) -> Int? {
        if let port = url.port { return port }
        return switch url.scheme?.lowercased() {
        case "http": 80
        case "https": 443
        default: nil
        }
    }
}
