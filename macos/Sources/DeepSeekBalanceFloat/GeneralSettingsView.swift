import SwiftUI
import AppKit

struct GeneralSettingsView: View {
    @ObservedObject var model: AppModel
    @ObservedObject private var settings: AppSettings

    @State private var loginNote: String?
    @State private var showClearConfirm = false
    @State private var urlDraft: String

    init(model: AppModel) {
        self.model = model
        _settings = ObservedObject(wrappedValue: model.settings)
        _urlDraft = State(initialValue: model.settings.apiURL)
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                SettingsPageHeader(
                    title: "通用",
                    subtitle: "外观、启动方式与悬浮窗行为。更改会立即生效。"
                )

                group("外观") {
                    SettingsCard {
                        SettingsRow("显示模式", subtitle: "「自动」默认跟随系统外观") {
                            Picker("", selection: $settings.theme) {
                                ForEach(ThemeMode.allCases) { mode in
                                    Text(mode.title).tag(mode)
                                }
                            }
                            .labelsHidden()
                            .pickerStyle(.segmented)
                            .frame(width: 212)
                        }
                        SettingsDivider()
                        SettingsRow("自动模式按日出日落", subtitle: "仅在显示模式为「自动」时生效") {
                            Toggle("", isOn: $settings.solarAuto)
                                .labelsHidden()
                                .toggleStyle(.switch)
                                .controlSize(.small)
                        }
                    }
                }

                group("启动与菜单栏") {
                    SettingsCard {
                        SettingsRow("开机自启", subtitle: "登录后自动显示余额悬浮窗") {
                            Toggle("", isOn: Binding(
                                get: { settings.launchAtLogin },
                                set: { newValue in
                                    let note = LoginItem.setEnabled(newValue)
                                    settings.launchAtLogin = LoginItem.isEnabled
                                    loginNote = note
                                }
                            ))
                            .labelsHidden()
                            .toggleStyle(.switch)
                            .controlSize(.small)
                        }
                        SettingsDivider()
                        SettingsRow("在菜单栏显示余额", subtitle: "菜单栏图标可快速刷新和打开设置") {
                            Toggle("", isOn: Binding(
                                get: { settings.showMenuBar },
                                set: { newValue in
                                    if !newValue && !settings.showDockIcon { settings.showDockIcon = true }
                                    settings.showMenuBar = newValue
                                }
                            ))
                            .labelsHidden()
                            .toggleStyle(.switch)
                            .controlSize(.small)
                        }
                        SettingsDivider()
                        SettingsRow("在 Dock 中显示", subtitle: "关闭后可通过菜单栏重新打开") {
                            Toggle("", isOn: Binding(
                                get: { settings.showDockIcon },
                                set: { newValue in
                                    if !newValue && !settings.showMenuBar { settings.showMenuBar = true }
                                    settings.showDockIcon = newValue
                                }
                            ))
                            .labelsHidden()
                            .toggleStyle(.switch)
                            .controlSize(.small)
                        }
                    }
                    if let loginNote {
                        Text(loginNote)
                            .font(.system(size: 10.5))
                            .foregroundStyle(.secondary)
                            .padding(.leading, 2)
                    }
                }

                group("悬浮窗") {
                    SettingsCard {
                        SettingsRow("窗口置顶", subtitle: "让余额始终显示在其他窗口之上") {
                            Toggle("", isOn: $settings.pinned)
                                .labelsHidden()
                                .toggleStyle(.switch)
                                .controlSize(.small)
                        }
                        SettingsDivider()
                        SettingsRow("位置", subtitle: "拖动窗口时靠近屏幕边缘会自动吸附") {
                            Button("重置位置") {
                                NotificationCenter.default.post(name: .resetPanelPosition, object: nil)
                            }
                            .controlSize(.small)
                        }
                        SettingsDivider()
                        SettingsRow("当前状态", subtitle: "已显示 \(model.visibleKeys.count) 个 Key · 每 5 秒刷新") {
                            Button("立即刷新") { model.refreshAll() }
                                .controlSize(.small)
                                .disabled(model.isRefreshing)
                        }
                    }
                }

                group("高级") {
                    SettingsCard {
                        VStack(alignment: .leading, spacing: 8) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("接口地址")
                                    .font(.system(size: 13))
                                Text("默认使用 DeepSeek 官方余额接口，可改为自建代理")
                                    .font(.system(size: 10.5))
                                    .foregroundStyle(.secondary)
                            }
                            HStack(spacing: 8) {
                                TextField("https://api.deepseek.com/user/balance", text: $urlDraft)
                                    .textFieldStyle(.roundedBorder)
                                    .font(.system(size: 11, design: .monospaced))
                                    .onSubmit { applyURL() }
                                Button("应用") { applyURL() }
                                    .controlSize(.small)
                                    .disabled(urlDraft == settings.apiURL)
                                Button("恢复默认") {
                                    urlDraft = AppSettings.defaultURL
                                    applyURL()
                                }
                                .controlSize(.small)
                            }
                        }
                        .padding(.horizontal, 14)
                        .padding(.vertical, 11)
                        SettingsDivider()
                        SettingsRow("数据", subtitle: "统计保存在 Application Support，Key 保存在钥匙串") {
                            HStack(spacing: 8) {
                                Button("打开数据文件夹") {
                                    let folder = model.stats.fileURLForDisplay.deletingLastPathComponent()
                                    NSWorkspace.shared.open(folder)
                                }
                                .controlSize(.small)
                                Button("清空统计", role: .destructive) {
                                    showClearConfirm = true
                                }
                                .controlSize(.small)
                            }
                        }
                    }
                }

                HStack {
                    Text("DeepSeek 余额 macOS 版 \(AppInfo.version)（\(AppInfo.build)）")
                        .font(.system(size: 10))
                        .foregroundStyle(.tertiary)
                    Spacer()
                    Button("退出应用") { NSApp.terminate(nil) }
                        .controlSize(.small)
                }
                .padding(.top, 2)
            }
            .padding(28)
            .frame(maxWidth: 620, alignment: .leading)
        }
        .scrollIndicators(.automatic)
        .onAppear {
            urlDraft = settings.apiURL
        }
        .alert("清空消耗统计？", isPresented: $showClearConfirm) {
            Button("取消", role: .cancel) {}
            Button("清空", role: .destructive) { model.clearStats() }
        } message: {
            Text("将删除所有 Key 的 7 天小时明细与历史日汇总，且无法恢复。余额与 API Key 不受影响。")
        }
    }

    private func applyURL() {
        let trimmed = urlDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        settings.apiURL = trimmed.isEmpty ? AppSettings.defaultURL : trimmed
        urlDraft = settings.apiURL
        model.refreshAll()
    }

    @ViewBuilder
    private func group<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            SettingsSectionLabel(text: title)
            content()
        }
    }
}
