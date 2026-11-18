import SwiftUI
import AppKit

struct KeysSettingsView: View {
    @ObservedObject var model: AppModel

    @State private var draft = ""
    @State private var inlineError: String?
    @State private var actionError: String?
    @State private var deleteTarget: KeyItem?
    @FocusState private var addFocused: Bool

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                SettingsPageHeader(
                    title: "API Key",
                    subtitle: "Key 保存在 macOS 钥匙串中。最多保存 \(KeychainStore.maxKeys) 个，最多同时显示 3 个。"
                )

                if let storageError = model.storageError {
                    HStack(alignment: .top, spacing: 8) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 11))
                            .foregroundStyle(.red)
                        Text(storageError)
                            .font(.system(size: 11))
                            .fixedSize(horizontal: false, vertical: true)
                        Spacer(minLength: 8)
                        Button("重试") { model.reloadKeys() }
                            .controlSize(.small)
                    }
                    .padding(12)
                    .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.red.opacity(0.08)))
                }

                SettingsCard {
                    HStack(spacing: 8) {
                        TextField("粘贴 API Key（sk-…）", text: $draft)
                            .textFieldStyle(.roundedBorder)
                            .font(.system(size: 12, design: .monospaced))
                            .focused($addFocused)
                            .onSubmit(addKey)
                            .disabled(model.keys.count >= KeychainStore.maxKeys)
                        Button("添加") { addKey() }
                            .buttonStyle(.borderedProminent)
                            .controlSize(.regular)
                            .disabled(model.keys.count >= KeychainStore.maxKeys)
                    }
                    .padding(12)

                    if let inlineError {
                        HStack(spacing: 5) {
                            Image(systemName: "exclamationmark.circle.fill")
                            Text(inlineError)
                            Spacer()
                        }
                        .font(.system(size: 10.5))
                        .foregroundStyle(.red)
                        .padding(.horizontal, 14)
                        .padding(.bottom, 10)
                    }
                }

                HStack(spacing: 6) {
                    Image(systemName: "key.horizontal")
                    Text("已保存 \(model.keys.count) / \(KeychainStore.maxKeys) 个 Key")
                    Spacer()
                    Text("双击名称可重命名")
                }
                .font(.system(size: 10.5))
                .foregroundStyle(.secondary)
                .padding(.horizontal, 2)

                SettingsCard {
                    if model.keys.isEmpty {
                        VStack(spacing: 6) {
                            Image(systemName: "key.slash")
                                .font(.system(size: 20))
                                .foregroundStyle(.tertiary)
                            Text("还没有保存任何 API Key")
                                .font(.system(size: 12))
                                .foregroundStyle(.secondary)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 28)
                    } else {
                        ForEach(Array(model.keys.enumerated()), id: \.element.id) { index, key in
                            KeyRow(
                                model: model,
                                key: key,
                                onDelete: { deleteTarget = key },
                                onError: { actionError = $0 }
                            )
                            if index < model.keys.count - 1 {
                                SettingsDivider()
                            }
                        }
                    }
                }

                HStack(alignment: .top, spacing: 7) {
                    Image(systemName: "lock.fill")
                        .font(.system(size: 9))
                    Text("Key 只写入本机登录钥匙串，不写入偏好设置或任何文件，也不会发送到 DeepSeek 以外的服务器。界面上仅显示掩码。")
                        .fixedSize(horizontal: false, vertical: true)
                }
                .font(.system(size: 10.5))
                .foregroundStyle(.tertiary)
                .padding(.horizontal, 2)
            }
            .padding(28)
            .frame(maxWidth: 640, alignment: .leading)
        }
        .alert("无法切换显示状态", isPresented: Binding(
            get: { actionError != nil },
            set: { if !$0 { actionError = nil } }
        )) {
            Button("好", role: .cancel) { actionError = nil }
        } message: {
            Text(actionError ?? "")
        }
        .alert("删除这个 API Key？", isPresented: Binding(
            get: { deleteTarget != nil },
            set: { if !$0 { deleteTarget = nil } }
        ), presenting: deleteTarget) { key in
            Button("取消", role: .cancel) { deleteTarget = nil }
            Button("删除", role: .destructive) {
                if let error = model.removeKey(key.id) {
                    actionError = error
                }
                deleteTarget = nil
            }
        } message: { key in
            Text("「\(key.name)」将从钥匙串中移除，该 Key 的余额停止刷新。历史消耗统计会保留。")
        }
    }

    private func addKey() {
        let result = model.addKey(draft)
        if let result {
            inlineError = result
        } else {
            draft = ""
            inlineError = nil
            addFocused = false
        }
    }
}

private struct KeyRow: View {
    @ObservedObject var model: AppModel
    let key: KeyItem
    let onDelete: () -> Void
    let onError: (String) -> Void

    @State private var nameDraft: String
    @State private var copied = false
    @FocusState private var nameFocused: Bool

    init(model: AppModel, key: KeyItem, onDelete: @escaping () -> Void, onError: @escaping (String) -> Void) {
        self.model = model
        self.key = key
        self.onDelete = onDelete
        self.onError = onError
        _nameDraft = State(initialValue: key.name)
    }

    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                TextField("名称", text: $nameDraft)
                    .textFieldStyle(.plain)
                    .font(.system(size: 13, weight: .medium))
                    .frame(width: 170, alignment: .leading)
                    .focused($nameFocused)
                    .onSubmit(commitName)
                    .onChange(of: nameFocused) { _, focused in
                        if !focused { commitName() }
                    }
                    .onChange(of: key.name) { _, newValue in
                        if !nameFocused { nameDraft = newValue }
                    }

                HStack(spacing: 5) {
                    Text(KeyIdentity.mask(key.key))
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    if key.show {
                        Text("显示中")
                            .font(.system(size: 9))
                            .foregroundStyle(.secondary)
                            .padding(.horizontal, 4)
                            .padding(.vertical, 1)
                            .background(Capsule().fill(Color.primary.opacity(0.06)))
                    }
                }
            }

            Spacer(minLength: 8)

            Toggle("显示", isOn: Binding(
                get: { key.show },
                set: { _ in
                    if let error = model.toggleShow(key.id) { onError(error) }
                }
            ))
            .labelsHidden()
            .toggleStyle(.switch)
            .controlSize(.small)
            .help("在悬浮窗中显示")

            Button {
                model.copyKey(key.id)
                copied = true
                Task { @MainActor in
                    try? await Task.sleep(for: .milliseconds(1200))
                    copied = false
                }
            } label: {
                Image(systemName: copied ? "checkmark" : "doc.on.doc")
                    .font(.system(size: 11))
                    .frame(width: 24, height: 22)
                    .foregroundStyle(copied ? .green : .secondary)
            }
            .buttonStyle(.plain)
            .help("复制完整 Key")

            Button(action: onDelete) {
                Image(systemName: "trash")
                    .font(.system(size: 11))
                    .frame(width: 24, height: 22)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("删除")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    private func commitName() {
        guard nameDraft != key.name else { return }
        if let error = model.renameKey(key.id, to: nameDraft) {
            onError(error)
            nameDraft = key.name
        } else if let updated = model.keys.first(where: { $0.id == key.id }) {
            nameDraft = updated.name
        }
    }
}
