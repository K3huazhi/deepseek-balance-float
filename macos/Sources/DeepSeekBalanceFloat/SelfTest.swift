import Foundation

enum SelfTest {
    static func run() -> Bool {
        var passed = true

        func check(_ condition: Bool, _ name: String) {
            print(condition ? "PASS  \(name)" : "FAIL  \(name)")
            if !condition { passed = false }
        }

        check(KeyIdentity.mask("sk-a81117a2c5d4e3f2c97b01ceb0921c8").contains("******"),
              "Key 掩码隐藏中间六位")
        check(KeyIdentity.mask("sk-a81117a2c5d4e3f2c97b01ceb0921c8").hasPrefix("sk-a81117"),
              "Key 掩码保留头部")
        check(KeyIdentity.defaultName("sk-1234567890") == "123456", "默认名称取 sk- 后六位")
        check(KeyIdentity.uid("").count == 12, "Key 指纹为 12 位十六进制")
        check(KeyIdentity.normalized(" Bearer sk-abc \n") == "sk-abc", "规范化去掉 Bearer 前缀")

        let june = Calendar.current.date(from: DateComponents(year: 2026, month: 6, day: 21))!
        let times = SunTimes.minutes(date: june, latitude: 35, longitude: 120)
        check(times != nil, "日出日落可计算")
        if let times {
            check(times.sunrise > 240 && times.sunrise < 330, "夏季日出时间合理")
            check(times.sunset > 1120 && times.sunset < 1215, "夏季日落时间合理")
        }
        let december = Calendar.current.date(from: DateComponents(year: 2026, month: 12, day: 21))!
        let winter = SunTimes.minutes(date: december, latitude: 35, longitude: 120)
        if let winter {
            check(winter.sunrise > 400 && winter.sunset < 1050, "冬季日照更短")
        }

        let statsURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("dsb-selftest-\(UUID().uuidString).json")
        let stats = StatsStore(fileURL: statsURL)
        stats.record(uid: "abc", total: 10)
        check(stats.today("abc") == 0, "首次记录不产生消费")
        stats.record(uid: "abc", total: 9)
        check(abs(stats.today("abc") - 1.0) < 0.001, "余额差记录为今日消费")
        check(abs(stats.week("abc").last! - 1.0) < 0.001, "周图表最后一天为今天")
        let heat = stats.heat("abc")
        check(heat.count == 7 && heat[0].count == 24, "热力图为 7×24")
        check(abs(stats.buckets("abc").reduce(0, +) - 1.0) < 0.001, "分时段汇总等于总消费")
        check(stats.heatTotal("abc") >= 0.99, "热力图汇总正确")
        stats.record(uid: "abc", total: 10)
        check(abs(stats.today("abc") - 1.0) < 0.001, "余额增加不计入消费")

        let gapBase = Date()
        stats.record(uid: "gap", total: 10, at: gapBase)
        let longGap = stats.record(uid: "gap", total: 9, at: gapBase.addingTimeInterval(120))
        check(longGap == 0, "间隔超过 60 秒不补记")
        let shortGap = stats.record(uid: "gap", total: 8.5, at: gapBase.addingTimeInterval(125))
        check(abs(shortGap - 0.5) < 0.001, "间隔 5 秒正常记账")
        try? FileManager.default.removeItem(at: statsURL)

        let json = """
        {"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"9.31","granted_balance":"0.00","topped_up_balance":"9.31"}]}
        """
        if let info = try? ApiClient.decode(Data(json.utf8)) {
            check(abs(info.total - 9.31) < 0.001, "解析余额字符串")
            check(abs(info.topped - 9.31) < 0.001, "解析充值余额")
            check(info.symbol == "¥", "解析人民币符号")
            check(info.available, "解析可用状态")
        } else {
            check(false, "解析标准余额响应")
        }

        let numericJSON = """
        {"is_available":false,"balance_infos":[{"currency":"USD","total_balance":3.5,"granted_balance":1,"topped_up_balance":2.5}]}
        """
        if let info = try? ApiClient.decode(Data(numericJSON.utf8)) {
            check(abs(info.total - 3.5) < 0.001, "解析数字余额")
            check(!info.available, "解析不可用状态")
            check(info.symbol == "USD ", "解析美元符号")
        } else {
            check(false, "解析数字余额响应")
        }

        let emptyJSON = Data(#"{"is_available":true,"balance_infos":[]}"#.utf8)
        check((try? ApiClient.decode(emptyJSON)) == nil, "空余额列表报错")

        let keychainService = "com.k3huazhi.DeepSeekBalanceFloat.selftest"
        do {
            try KeychainStore.save([
                KeyItem(key: "sk-selftest-000000000001", show: true, name: "测试一"),
                KeyItem(key: "sk-selftest-000000000002", show: false, name: "测试二")
            ], service: keychainService)
            let loaded = try KeychainStore.load(service: keychainService)
            check(loaded.count == 2 && loaded[0].name == "测试一" && loaded[1].show == false,
                  "钥匙串保存与读取")
            try KeychainStore.delete(service: keychainService)
            check(try KeychainStore.load(service: keychainService).isEmpty, "钥匙串删除")
        } catch {
            check(false, "钥匙串自检（\(error.localizedDescription)）")
        }

        check(BalanceInfo(currency: "USD", total: 1, granted: 0, topped: 1, available: true).symbol == "USD ",
              "非人民币货币符号")
        check(BalanceTone.tone(total: 9, available: true) == .positive, "余额充足为绿色")
        check(BalanceTone.tone(total: 1, available: true) == .warning, "余额偏低为黄色")
        check(BalanceTone.tone(total: 0.2, available: true) == .negative, "余额不足为红色")

        check((try? ApiClient.validatedURL("http://example.com/user/balance")) == nil,
              "非 localhost 拒绝 HTTP")
        check((try? ApiClient.validatedURL("http://127.0.0.1:8765/user/balance")) != nil,
              "允许 localhost HTTP")
        check((try? ApiClient.validatedURL("https://api.deepseek.com/user/balance")) != nil,
              "允许 HTTPS")
        check((try? ApiClient.validatedURL("http://127.0.0.1.evil.com/user/balance")) == nil,
              "拒绝伪装成 127.x 的域名")

        if passed { print("\n全部自检通过。") } else { print("\n存在失败项。") }
        return passed
    }
}
