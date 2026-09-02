import CryptoKit
import Foundation

public enum M3UParserError: LocalizedError, Equatable, Sendable {
    case missingSourceIdentity
    case invalidSafetyLimit
    case oversizedPlaylist(maximumBytes: Int)
    case tooManyChannels(maximum: Int)
    case noPlayableEntries

    public var errorDescription: String? {
        switch self {
        case .missingSourceIdentity:
            "A source ID and source name are required."
        case .invalidSafetyLimit:
            "Playlist safety limits must be positive."
        case .oversizedPlaylist(let maximumBytes):
            "The playlist is larger than the \(maximumBytes / 1_048_576) MB safety limit."
        case .tooManyChannels(let maximum):
            "The playlist exceeds the \(maximum.formatted()) channel safety limit."
        case .noPlayableEntries:
            "No playable entries were found. Choose an M3U or M3U8 playlist that contains stream URLs."
        }
    }
}

public enum M3UParser {
    public static let defaultMaximumChannels = 250_000
    public static let defaultMaximumBytes = 64 * 1_024 * 1_024

    private static let playableSchemes = Set(["http", "https", "rtsp", "rtmp", "udp", "file"])
    private static let guideSchemes = Set(["http", "https", "file"])

    public static func parse(
        _ text: String,
        sourceId: String,
        sourceName: String,
        maximumChannels: Int = defaultMaximumChannels,
        maximumBytes: Int = defaultMaximumBytes
    ) throws -> ParsedPlaylist {
        guard !sourceId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              !sourceName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw M3UParserError.missingSourceIdentity
        }
        guard maximumChannels > 0, maximumBytes > 0 else {
            throw M3UParserError.invalidSafetyLimit
        }
        guard text.utf8.count <= maximumBytes else {
            throw M3UParserError.oversizedPlaylist(maximumBytes: maximumBytes)
        }

        var channels: [CatalogChannel] = []
        channels.reserveCapacity(min(16_384, maximumChannels))
        var pending: PendingChannel?
        var guideSources: [String] = []

        text.enumerateLines { rawLine, stop in
            guard stop == false else { return }
            let line = cleanLine(rawLine)
            guard !line.isEmpty else { return }

            if line.hasPrefixIgnoringCase("#EXTM3U") {
                if guideSources.isEmpty {
                    guideSources = parseGuideSources(line)
                }
                return
            }
            if line.hasPrefixIgnoringCase("#EXTINF") {
                pending = parseMetadata(line)
                return
            }
            if pending != nil, line.hasPrefixIgnoringCase("#EXTVLCOPT:http-user-agent=") {
                pending?.userAgent = clean(String(line.dropFirst(throughFirst: "=")))
                return
            }
            if pending != nil && (
                line.hasPrefixIgnoringCase("#EXTVLCOPT:http-referrer=") ||
                line.hasPrefixIgnoringCase("#EXTHTTP:")
            ) {
                pending?.referrer = extractReferrer(line)
                return
            }
            if line.hasPrefix("#") || !hasAllowedScheme(line, allowed: playableSchemes) {
                return
            }
            if channels.count >= maximumChannels {
                stop = true
                return
            }

            let metadata = pending ?? PendingChannel(name: "Channel \(channels.count + 1)")
            let name = clean(metadata.name) ?? "Channel \(channels.count + 1)"
            let group = clean(metadata.group) ?? "Uncategorized"
            var requestHeaders: [String: String] = [:]
            if let userAgent = clean(metadata.userAgent) { requestHeaders["User-Agent"] = userAgent }
            if let referrer = clean(metadata.referrer) { requestHeaders["Referer"] = referrer }

            let guide = GuideMetadata(
                tvgId: clean(metadata.tvgId),
                tvgName: clean(metadata.tvgName),
                logoUri: clean(metadata.logoUri)
            )
            let catchup = clean(metadata.catchupSource).map { source in
                CatchupMetadata(
                    mode: clean(metadata.catchupMode) ?? "default",
                    source: source,
                    days: min(max(metadata.catchupDays, 0), 365),
                    correctionMinutes: min(max(metadata.catchupCorrectionMinutes, -1_440), 1_440)
                )
            }
            channels.append(
                CatalogChannel(
                    id: stableChannelID(
                        tvgId: metadata.tvgId,
                        name: name,
                        group: group,
                        streamURI: line
                    ),
                    number: channels.count + 1,
                    name: name,
                    group: group,
                    kind: inferKind(group: group, streamURI: line),
                    sourceId: sourceId,
                    stream: StreamDescriptor(uri: line, requestHeaders: requestHeaders),
                    guide: guide.isEmpty ? nil : guide,
                    catchup: catchup
                )
            )
            pending = nil
        }

        if channels.count >= maximumChannels,
           containsAnotherPlayableEntry(after: channels.count, in: text) {
            throw M3UParserError.tooManyChannels(maximum: maximumChannels)
        }
        guard !channels.isEmpty else { throw M3UParserError.noPlayableEntries }
        return ParsedPlaylist(channels: channels, guideSources: guideSources)
    }

    public static func stableChannelID(
        tvgId: String?,
        name: String,
        group: String,
        streamURI: String
    ) -> String {
        let trimmedURI = streamURI.trimmingCharacters(in: .whitespacesAndNewlines)
        let cutIndex = [trimmedURI.firstIndex(of: "?"), trimmedURI.firstIndex(of: "#")]
            .compactMap { $0 }
            .min()
        let endpoint = cutIndex.map { String(trimmedURI[..<$0]) } ?? trimmedURI
        let upperName = name.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        let upperGroup = group.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        let identity: String
        if let tvgId = clean(tvgId) {
            identity = "tvg:\(tvgId.uppercased())|name:\(upperName)|group:\(upperGroup)|endpoint:\(endpoint)"
        } else {
            identity = "name:\(upperName)|group:\(upperGroup)|endpoint:\(endpoint)"
        }
        return SHA256.hash(data: Data(identity.utf8))
            .map { String(format: "%02X", $0) }
            .joined()
    }

    private static func containsAnotherPlayableEntry(after maximum: Int, in text: String) -> Bool {
        var playableCount = 0
        for rawLine in text.components(separatedBy: .newlines) {
            let line = cleanLine(rawLine)
            guard !line.hasPrefix("#"), hasAllowedScheme(line, allowed: playableSchemes) else { continue }
            playableCount += 1
            if playableCount > maximum { return true }
        }
        return false
    }

    private static func cleanLine(_ rawLine: String) -> String {
        rawLine.trimmingCharacters(in: .whitespacesAndNewlines).removingLeadingBOM()
    }

    private static func parseMetadata(_ line: String) -> PendingChannel {
        let separator = findNameSeparator(line)
        let metadata = separator.map { String(line[..<$0]) } ?? line
        let listedName = separator.map { String(line[line.index(after: $0)...]).trimmingCharacters(in: .whitespaces) } ?? ""
        let attributes = parseAttributes(metadata)
        let tvgName = attributes["tvg-name"]
        let days = Int(attributes["catchup-days"] ?? attributes["timeshift"] ?? "0") ?? 0
        let correction = Double(attributes["catchup-correction"] ?? "0") ?? 0
        return PendingChannel(
            name: listedName.isEmpty ? (tvgName ?? "") : listedName,
            group: attributes["group-title"],
            logoUri: attributes["tvg-logo"],
            tvgId: attributes["tvg-id"],
            tvgName: tvgName,
            userAgent: attributes["http-user-agent"],
            referrer: attributes["http-referrer"],
            catchupMode: attributes["catchup"],
            catchupSource: attributes["catchup-source"],
            catchupDays: max(0, days),
            catchupCorrectionMinutes: Int((correction * 60).rounded(.towardZero))
        )
    }

    private static func parseGuideSources(_ line: String) -> [String] {
        let attributes = parseAttributes(line)
        for key in ["url-tvg", "x-tvg-url", "tvg-url"] {
            guard let value = attributes[key] else { continue }
            let sources = value.split(separator: ",")
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { hasAllowedScheme($0, allowed: guideSchemes) }
            if !sources.isEmpty { return sources }
        }
        return []
    }

    private static func parseAttributes(_ value: String) -> [String: String] {
        var attributes: [String: String] = [:]
        var index = value.startIndex

        while index < value.endIndex {
            while index < value.endIndex, !isAttributeKeyCharacter(value[index]) {
                index = value.index(after: index)
            }
            guard index < value.endIndex else { break }
            let keyStart = index
            while index < value.endIndex, isAttributeKeyCharacter(value[index]) {
                index = value.index(after: index)
            }
            let key = String(value[keyStart..<index]).lowercased()
            guard index < value.endIndex, value[index] == "=" else { continue }
            index = value.index(after: index)
            guard index < value.endIndex else {
                attributes[key] = ""
                break
            }

            let quote: Character? = value[index] == "\"" || value[index] == "'" ? value[index] : nil
            if quote != nil { index = value.index(after: index) }
            let valueStart = index
            if let quote {
                while index < value.endIndex, value[index] != quote {
                    index = value.index(after: index)
                }
                attributes[key] = String(value[valueStart..<index])
                if index < value.endIndex { index = value.index(after: index) }
            } else {
                while index < value.endIndex, !value[index].isWhitespace, value[index] != "," {
                    index = value.index(after: index)
                }
                attributes[key] = String(value[valueStart..<index])
            }
        }
        return attributes
    }

    private static func findNameSeparator(_ line: String) -> String.Index? {
        var quote: Character?
        var index = line.startIndex
        while index < line.endIndex {
            let character = line[index]
            if character == "\"" || character == "'" {
                quote = quote == nil ? character : quote == character ? nil : quote
            } else if character == ",", quote == nil {
                return index
            }
            index = line.index(after: index)
        }
        return nil
    }

    private static func extractReferrer(_ line: String) -> String? {
        if let equals = line.firstIndex(of: "=") {
            return clean(String(line[line.index(after: equals)...]))
        }
        guard let key = line.range(of: "referer", options: [.caseInsensitive]) else { return nil }
        let remainder = line[key.upperBound...]
        guard let colon = remainder.firstIndex(of: ":") else { return nil }
        let candidate = remainder[remainder.index(after: colon)...]
            .drop(while: { $0.isWhitespace || $0 == "\"" || $0 == "'" })
            .prefix(while: { $0 != "\"" && $0 != "'" && $0 != "}" && $0 != "," })
        return clean(String(candidate))
    }

    private static func hasAllowedScheme(_ value: String, allowed: Set<String>) -> Bool {
        guard let components = URLComponents(string: value),
              let scheme = components.scheme?.lowercased(),
              allowed.contains(scheme) else { return false }
        if scheme == "http" || scheme == "https" {
            return !(components.host ?? "").isEmpty
        }
        return true
    }

    private static func inferKind(group: String, streamURI: String) -> ChannelKind {
        let value = "\(group) \(streamURI)".lowercased()
        if value.contains("/series/") || value.contains("series") || value.contains("shows") {
            return .series
        }
        if value.contains("/movie/") || value.contains("movie") || value.contains("vod") || value.contains("cinema") {
            return .movie
        }
        return .live
    }

    private static func clean(_ value: String?) -> String? {
        value?.trimmingCharacters(in: .whitespacesAndNewlines).nonEmpty
    }

    private static func isAttributeKeyCharacter(_ character: Character) -> Bool {
        character.isLetter || character.isNumber || character == "_" || character == "-"
    }
}

public enum StreamVueCatalogFactory {
    public static func create(
        fromM3U text: String,
        catalogId: String,
        displayName: String,
        source: CatalogSource,
        loadedAt: Date = Date(),
        maximumChannels: Int = M3UParser.defaultMaximumChannels
    ) throws -> StreamVueCatalog {
        let parsed = try M3UParser.parse(
            text,
            sourceId: source.id,
            sourceName: source.name,
            maximumChannels: maximumChannels
        )
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return StreamVueCatalog(
            catalogId: catalogId,
            displayName: displayName,
            loadedAt: formatter.string(from: loadedAt),
            sources: [source],
            guideSources: parsed.guideSources,
            channels: parsed.channels
        )
    }
}

private struct PendingChannel {
    var name: String
    var group: String?
    var logoUri: String?
    var tvgId: String?
    var tvgName: String?
    var userAgent: String?
    var referrer: String?
    var catchupMode: String?
    var catchupSource: String?
    var catchupDays = 0
    var catchupCorrectionMinutes = 0
}

private extension String {
    var nonEmpty: String? { isEmpty ? nil : self }

    func hasPrefixIgnoringCase(_ prefix: String) -> Bool {
        range(of: prefix, options: [.anchored, .caseInsensitive]) != nil
    }

    func dropFirst(throughFirst character: Character) -> Substring {
        guard let index = firstIndex(of: character) else { return self[...] }
        return self[self.index(after: index)...]
    }

    func removingLeadingBOM() -> String {
        first == "\u{FEFF}" ? String(dropFirst()) : self
    }
}
