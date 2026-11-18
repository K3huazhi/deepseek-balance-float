// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DeepSeekBalanceFloat",
    platforms: [.macOS("26.0")],
    products: [
        .executable(name: "DeepSeekBalanceFloat", targets: ["DeepSeekBalanceFloat"])
    ],
    targets: [
        .executableTarget(
            name: "DeepSeekBalanceFloat",
            path: "Sources/DeepSeekBalanceFloat",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
