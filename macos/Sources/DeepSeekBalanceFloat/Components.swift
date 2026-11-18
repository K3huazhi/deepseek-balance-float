import SwiftUI
import AppKit

// MARK: - 品牌图形

struct WhaleMark: View {
    var body: some View {
        WhaleShape()
            .fill(.primary)
            .aspectRatio(26.634 / 19.6, contentMode: .fit)
            .accessibilityHidden(true)
    }
}

// MARK: - 玻璃

struct PanelBackground: View {
    @Environment(\.palette) private var p
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 26, style: .continuous)
        Group {
            if reduceTransparency {
                shape.fill(Color(nsColor: .windowBackgroundColor))
            } else {
                Color.clear
                    .glassEffect(.regular.tint(p.glassTint), in: shape)
            }
        }
        .overlay {
            shape
                .fill(p.isDark
                      ? Color(red: 0.035, green: 0.065, blue: 0.058).opacity(0.45)
                      : Color.white.opacity(0.58))
                .allowsHitTesting(false)
        }
        .overlay {
            shape.fill(
                LinearGradient(
                    colors: [Color.white.opacity(p.isDark ? 0.035 : 0.16), .clear],
                    startPoint: .top,
                    endPoint: .center
                )
            )
            .blendMode(.plusLighter)
            .allowsHitTesting(false)
        }
        .overlay {
            shape.strokeBorder(
                LinearGradient(
                    colors: [
                        Color.white.opacity(p.isDark ? 0.22 : 0.60),
                        p.hairline
                    ],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                ),
                lineWidth: 0.8
            )
            .allowsHitTesting(false)
        }
    }
}

// MARK: - 小型图标按钮

struct PanelIconButton: View {
    let systemName: String
    var help: String
    var rotation: Double = 0
    let action: () -> Void

    @Environment(\.palette) private var p
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 11.5, weight: .medium))
                .rotationEffect(.degrees(rotation))
                .frame(width: 24, height: 22)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(hovering ? p.text : p.secondary)
        .background {
            RoundedRectangle(cornerRadius: 6, style: .continuous)
                .fill(hovering ? p.hover : Color.clear)
        }
        .onHover { hovering = $0 }
        .help(help)
        .accessibilityLabel(help)
    }
}

struct AccentButton: View {
    let title: String
    let action: () -> Void
    @Environment(\.palette) private var p
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(p.isDark ? Color(red: 0.02, green: 0.12, blue: 0.09) : Color.white)
                .padding(.horizontal, 15)
                .padding(.vertical, 6)
                .background {
                    Capsule().fill(p.accent.opacity(hovering ? 0.86 : 1))
                }
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

// MARK: - 细分隔线

struct Hairline: View {
    @Environment(\.palette) private var p

    var body: some View {
        Rectangle()
            .fill(p.hairline)
            .frame(height: 0.7)
    }
}

// MARK: - 数字

struct AnimatedMoney: View {
    let symbol: String
    let value: Double
    let font: Font
    let color: Color

    var body: some View {
        Text(symbol + BalanceInfo.money(value))
            .font(font)
            .monospacedDigit()
            .foregroundStyle(color)
            .contentTransition(.numericText(value: value))
            .animation(.snappy(duration: 0.45), value: value)
            .lineLimit(1)
            .minimumScaleFactor(0.7)
    }
}

// MARK: - 窗口访问

struct WindowAccessor: NSViewRepresentable {
    let onWindow: (NSWindow?) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView(frame: .zero)
        DispatchQueue.main.async {
            context.coordinator.window = view.window
            onWindow(view.window)
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        if nsView.window !== context.coordinator.window {
            context.coordinator.window = nsView.window
            DispatchQueue.main.async { onWindow(nsView.window) }
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator {
        weak var window: NSWindow?
    }
}
