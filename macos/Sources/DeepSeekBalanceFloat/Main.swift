import AppKit

@main
enum DeepSeekBalanceFloatMain {
    static func main() {
        if CommandLine.arguments.contains("--self-test") {
            exit(SelfTest.run() ? 0 : 1)
        }
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.regular)
        withExtendedLifetime(delegate) {
            app.run()
        }
    }
}
