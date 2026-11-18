import SwiftUI

/// 亮暗两套语义色。全部基于系统字体与语义层级，保证在 Liquid Glass 上清晰可读。
struct Palette {
    let scheme: ColorScheme

    var isDark: Bool { scheme == .dark }

    var text: Color {
        isDark ? Color(red: 0.953, green: 0.965, blue: 0.961) : Color(red: 0.075, green: 0.090, blue: 0.086)
    }

    var secondary: Color {
        isDark ? Color(white: 0.66) : Color(white: 0.38)
    }

    var tertiary: Color {
        isDark ? Color(white: 0.50) : Color(white: 0.55)
    }

    var hairline: Color {
        isDark ? Color.white.opacity(0.11) : Color.black.opacity(0.085)
    }

    var fill: Color {
        isDark ? Color.white.opacity(0.06) : Color.black.opacity(0.035)
    }

    var hover: Color {
        isDark ? Color.white.opacity(0.11) : Color.black.opacity(0.06)
    }

    var positive: Color {
        isDark ? Color(red: 0.39, green: 0.89, blue: 0.64) : Color(red: 0.02, green: 0.46, blue: 0.27)
    }

    var warning: Color {
        isDark ? Color(red: 1.0, green: 0.82, blue: 0.42) : Color(red: 0.63, green: 0.35, blue: 0.0)
    }

    var negative: Color {
        isDark ? Color(red: 1.0, green: 0.60, blue: 0.60) : Color(red: 0.74, green: 0.16, blue: 0.12)
    }

    var accent: Color { positive }

    var barTrack: Color {
        isDark ? Color.white.opacity(0.045) : Color.black.opacity(0.035)
    }

    var glassTint: Color {
        isDark ? Color(red: 0.04, green: 0.12, blue: 0.10).opacity(0.24) : Color.white.opacity(0.06)
    }

    func tone(_ total: Double, available: Bool) -> Color {
        switch BalanceTone.tone(total: total, available: available) {
        case .positive: return positive
        case .warning: return warning
        case .negative: return negative
        }
    }
}

private struct PaletteKey: EnvironmentKey {
    static let defaultValue = Palette(scheme: .dark)
}

extension EnvironmentValues {
    var palette: Palette {
        get { self[PaletteKey.self] }
        set { self[PaletteKey.self] = newValue }
    }
}
