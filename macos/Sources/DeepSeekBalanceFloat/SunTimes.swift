import Foundation

/// 日出日落计算（NOAA 简化算法），与 Windows 版保持一致。
enum SunTimes {
    static let defaultLatitude = 35.0

    static func location(latitude: Double?, longitude: Double?, date: Date = Date()) -> (lat: Double, lon: Double) {
        let lat = latitude ?? defaultLatitude
        let lon = longitude ?? (Double(TimeZone.current.secondsFromGMT(for: date)) / 3600.0 * 15.0)
        return (lat, lon)
    }

    /// 返回当天日出/日落的“分钟数”（本地时间）。极昼极夜返回 nil。
    static func minutes(date: Date, latitude: Double, longitude: Double) -> (sunrise: Double, sunset: Double)? {
        let calendar = Calendar.current
        guard let dayOfYear = calendar.ordinality(of: .day, in: .year, for: date) else { return nil }
        let rad = Double.pi / 180.0
        let g = 2 * Double.pi / 365.0 * (Double(dayOfYear) - 1 + 0.5)
        let eq = 229.18 * (0.000075 + 0.001868 * cos(g) - 0.032077 * sin(g)
                           - 0.014615 * cos(2 * g) - 0.040849 * sin(2 * g))
        let decl = 0.006918 - 0.399912 * cos(g) + 0.070257 * sin(g)
            - 0.006758 * cos(2 * g) + 0.000907 * sin(2 * g)
            - 0.002697 * cos(3 * g) + 0.00148 * sin(3 * g)
        let latR = latitude * rad
        let cosHa = cos(90.833 * rad) / (cos(latR) * cos(decl)) - tan(latR) * tan(decl)
        guard cosHa >= -1, cosHa <= 1 else { return nil }
        let ha = acos(cosHa) / rad
        let offsetMin = Double(TimeZone.current.secondsFromGMT(for: date)) / 60.0
        var rise = 720 - 4 * (longitude + ha) - eq + offsetMin
        var set = 720 - 4 * (longitude - ha) - eq + offsetMin
        rise = ((rise.truncatingRemainder(dividingBy: 1440)) + 1440).truncatingRemainder(dividingBy: 1440)
        set = ((set.truncatingRemainder(dividingBy: 1440)) + 1440).truncatingRemainder(dividingBy: 1440)
        return (rise, set)
    }

    static func isNight(_ date: Date = Date(), latitude: Double?, longitude: Double?) -> Bool {
        let loc = location(latitude: latitude, longitude: longitude, date: date)
        guard let times = minutes(date: date, latitude: loc.lat, longitude: loc.lon) else {
            let hour = Calendar.current.component(.hour, from: date)
            return hour < 6 || hour >= 18
        }
        let comps = Calendar.current.dateComponents([.hour, .minute], from: date)
        let now = Double((comps.hour ?? 0) * 60 + (comps.minute ?? 0))
        if times.sunset < times.sunrise {
            return !(now >= times.sunset && now < times.sunrise)
        }
        return now < times.sunrise || now >= times.sunset
    }
}
