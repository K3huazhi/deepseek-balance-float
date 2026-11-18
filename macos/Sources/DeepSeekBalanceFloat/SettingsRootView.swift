import SwiftUI

struct SettingsRootView: View {
    @ObservedObject var model: AppModel
    @Environment(\.colorScheme) private var scheme
    @State private var selection: SettingsSection

    init(model: AppModel) {
        _model = ObservedObject(wrappedValue: model)
        _selection = State(initialValue: model.pendingSection)
    }

    var body: some View {
        HStack(spacing: 0) {
            sidebar
                .frame(width: 188)
            Divider()
            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Color(nsColor: .windowBackgroundColor))
        }
        .frame(minWidth: 860, minHeight: 560)
        .background(Color(nsColor: .windowBackgroundColor))
        .environment(\.palette, Palette(scheme: scheme))
        .onReceive(model.$pendingSection) { section in
            selection = section
        }
    }

    private var sidebar: some View {
        VStack(spacing: 0) {
            List(selection: $selection) {
                ForEach(SettingsSection.allCases) { section in
                    Label(section.title, systemImage: section.symbol)
                        .tag(section)
                }
            }
            .listStyle(.sidebar)
            .scrollContentBackground(.hidden)
            .background(.regularMaterial)

            Divider()
            VStack(alignment: .leading, spacing: 3) {
                Text("DeepSeek 余额")
                    .font(.system(size: 10.5, weight: .medium))
                    .foregroundStyle(.secondary)
                Text("macOS 原生版 \(AppInfo.version)")
                    .font(.system(size: 9.5))
                    .foregroundStyle(.tertiary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 18)
            .padding(.vertical, 11)
        }
        .background(.regularMaterial)
    }

    @ViewBuilder
    private var detail: some View {
        switch selection {
        case .general:
            GeneralSettingsView(model: model)
        case .keys:
            KeysSettingsView(model: model)
        case .usage:
            UsageSettingsView(model: model)
        case .about:
            AboutSettingsView(model: model)
        }
    }
}

enum AppInfo {
    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0"
    }

    static var build: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1"
    }

    static let repository = URL(string: "https://github.com/K3huazhi/deepseek-balance-float")!
    static let platform = URL(string: "https://platform.deepseek.com")!
}
