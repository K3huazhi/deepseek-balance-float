import SwiftUI

struct AboutSettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                SettingsPageHeader(
                    title: "关于",
                    subtitle: "一个为 macOS Tahoe 26 重新实现的 DeepSeek 余额悬浮窗。"
                )

                HStack(alignment: .center, spacing: 18) {
                    AppIconView()
                        .frame(width: 88, height: 88)
                        .shadow(color: .black.opacity(0.18), radius: 10, y: 5)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("DeepSeek 余额")
                            .font(.system(size: 20, weight: .bold))
                        Text("macOS 原生版 \(AppInfo.version)")
                            .font(.system(size: 11.5))
                            .foregroundStyle(.secondary)
                        Text("SwiftUI · Liquid Glass · 无第三方依赖")
                            .font(.system(size: 10.5))
                            .foregroundStyle(.tertiary)
                    }
                    Spacer()
                }
                .padding(.top, 2)

                Text("从 K3huazhi/deepseek-balance-float 的 Windows 版提取功能与数据规则，使用 SwiftUI 与 AppKit 为 macOS 重新实现：Liquid Glass 悬浮面板、菜单栏余额、钥匙串保存 Key、原生设置窗口、7 × 24 消耗热力图，以及按日出日落自动切换外观。")
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .lineSpacing(3)

                SettingsCard {
                    featureRow("rectangle.on.rectangle", "悬浮窗", "拖动定位、边缘吸附、多显示器位置记忆")
                    SettingsDivider()
                    featureRow("arrow.triangle.2.circlepath", "实时刷新", "每 5 秒获取余额，右键或 ⌘R 立即刷新")
                    SettingsDivider()
                    featureRow("key.horizontal", "多 Key", "最多 10 个，同时显示 3 个，钥匙串加密保存")
                    SettingsDivider()
                    featureRow("square.grid.3x3", "消耗统计", "7 天 × 24 小时热力图与分时段汇总")
                    SettingsDivider()
                    featureRow("sun.horizon", "自动外观", "跟随系统，或按当地日出日落切换")
                }

                HStack(spacing: 10) {
                    Link(destination: AppInfo.repository) {
                        Label("上游 GitHub 项目", systemImage: "arrow.up.right.square")
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.regular)

                    Link(destination: AppInfo.platform) {
                        Label("DeepSeek 开放平台", systemImage: "safari")
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.regular)

                    Spacer()
                }

                VStack(alignment: .leading, spacing: 7) {
                    Text("数据存放位置")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.secondary)
                    pathRow("钥匙串", "登录钥匙串 · \(KeychainStore.service)")
                    pathRow("消耗统计", "~/Library/Application Support/DeepSeekBalanceFloat/stats.json")
                    pathRow("偏好设置", "~/Library/Preferences/com.k3huazhi.DeepSeekBalanceFloat.plist")
                }

                Text("本项目基于上游 MIT 许可证发布。DeepSeek 与鲸鱼标志是杭州深度求索人工智能基础技术研究有限公司的商标；本应用为非官方客户端。")
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(28)
            .frame(maxWidth: 640, alignment: .leading)
        }
    }

    private func featureRow(_ symbol: String, _ title: String, _ detail: String) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .font(.system(size: 13))
                .foregroundStyle(.secondary)
                .frame(width: 20)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.system(size: 12.5, weight: .medium))
                Text(detail).font(.system(size: 10.5)).foregroundStyle(.secondary)
            }
            Spacer()
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
    }

    private func pathRow(_ label: String, _ value: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(label)
                .font(.system(size: 10.5))
                .foregroundStyle(.secondary)
                .frame(width: 60, alignment: .leading)
            Text(value)
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(.tertiary)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

struct AppIconView: View {
    var body: some View {
        GeometryReader { geo in
            let size = min(geo.size.width, geo.size.height)
            ZStack {
                RoundedRectangle(cornerRadius: size * 0.225, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [Color(red: 0.10, green: 0.34, blue: 0.28),
                                     Color(red: 0.03, green: 0.16, blue: 0.13)],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                RoundedRectangle(cornerRadius: size * 0.225, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [Color.white.opacity(0.24), .clear],
                            startPoint: .top,
                            endPoint: .center
                        )
                    )
                WhaleShape()
                    .fill(Color.white.opacity(0.96))
                    .frame(width: size * 0.60, height: size * 0.44)
                    .offset(y: size * 0.01)
                    .shadow(color: .black.opacity(0.25), radius: size * 0.02, y: size * 0.015)
                RoundedRectangle(cornerRadius: size * 0.225, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.18), lineWidth: 0.8)
            }
        }
        .aspectRatio(1, contentMode: .fit)
    }
}
