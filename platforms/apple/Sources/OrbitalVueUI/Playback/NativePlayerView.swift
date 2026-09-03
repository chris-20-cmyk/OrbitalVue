#if os(iOS) || os(tvOS)
import AVFoundation
import AVKit
import SwiftUI

public struct NativePlayerSurface: View {
    public let player: AVPlayer
    public let aspectMode: VideoAspectMode
    public let allowsPictureInPicture: Bool

    public init(
        player: AVPlayer,
        aspectMode: VideoAspectMode,
        allowsPictureInPicture: Bool
    ) {
        self.player = player
        self.aspectMode = aspectMode
        self.allowsPictureInPicture = allowsPictureInPicture
    }

    public var body: some View {
        GeometryReader { proxy in
            let size = fittedSize(in: proxy.size)
            ZStack {
                Color.black
                NativePlayerControllerView(
                    player: player,
                    videoGravity: videoGravity,
                    allowsPictureInPicture: allowsPictureInPicture
                )
                .frame(width: size.width, height: size.height)
                .clipped()
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(Color.black)
    }

    private var videoGravity: AVLayerVideoGravity {
        switch aspectMode {
        case .fill: .resizeAspectFill
        case .stretch: .resize
        default: .resizeAspect
        }
    }

    private func fittedSize(in available: CGSize) -> CGSize {
        guard let ratio = aspectMode.forcedRatio, available.width > 0, available.height > 0 else {
            return available
        }
        let availableRatio = available.width / available.height
        if availableRatio > ratio {
            return CGSize(width: available.height * ratio, height: available.height)
        }
        return CGSize(width: available.width, height: available.width / ratio)
    }
}

private struct NativePlayerControllerView: UIViewControllerRepresentable {
    let player: AVPlayer
    let videoGravity: AVLayerVideoGravity
    let allowsPictureInPicture: Bool

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIViewController(context: Context) -> AVPlayerViewController {
        let controller = AVPlayerViewController()
        controller.delegate = context.coordinator
        controller.player = player
        controller.showsPlaybackControls = true
        controller.videoGravity = videoGravity
        #if os(iOS)
        controller.updatesNowPlayingInfoCenter = true
        controller.allowsPictureInPicturePlayback = allowsPictureInPicture
        controller.canStartPictureInPictureAutomaticallyFromInline = allowsPictureInPicture
        #elseif os(tvOS)
        controller.appliesPreferredDisplayCriteriaAutomatically = true
        #endif
        return controller
    }

    func updateUIViewController(_ controller: AVPlayerViewController, context: Context) {
        controller.player = player
        controller.videoGravity = videoGravity
        #if os(iOS)
        controller.allowsPictureInPicturePlayback = allowsPictureInPicture
        controller.canStartPictureInPictureAutomaticallyFromInline = allowsPictureInPicture
        #elseif os(tvOS)
        controller.appliesPreferredDisplayCriteriaAutomatically = true
        #endif
    }

    final class Coordinator: NSObject, AVPlayerViewControllerDelegate {
        func playerViewController(
            _ playerViewController: AVPlayerViewController,
            restoreUserInterfaceForPictureInPictureStopWithCompletionHandler completionHandler: @escaping (Bool) -> Void
        ) {
            _ = playerViewController
            completionHandler(true)
        }
    }
}

public struct OrbitalVueRoutePicker: UIViewRepresentable {
    public init() {}

    public func makeUIView(context: Context) -> AVRoutePickerView {
        let view = AVRoutePickerView()
        view.prioritizesVideoDevices = true
        view.tintColor = .systemTeal
        view.activeTintColor = .systemTeal
        return view
    }

    public func updateUIView(_ view: AVRoutePickerView, context: Context) {
        view.prioritizesVideoDevices = true
    }
}
#endif
