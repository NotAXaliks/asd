using System.Globalization;

namespace Club.Core;

public static class PcStatus
{
    public const string Off = "off", Free = "free", Busy = "busy";
}

public record Tariff(string Name, int Minutes, decimal Mult)
{
    public decimal Price(decimal rate) => Math.Round(rate * Mult / 10m) * 10m;
}

public record MenuItem(string Emoji, string Name, decimal Price)
{
    public string Caption => $"{Name}\n{Fmt.Rub(Price)}";
}

public static class Catalog
{
    public static readonly Tariff Demo = new("Демо 1 мин", 1, 0.25m);
    public static readonly Tariff Extend = new("+30 мин", 30, 0.5m);
    public static readonly Tariff Express = new("15 мин", 15, 0.25m);   // авто-режим

    public static readonly Tariff[] Tariffs =
    [
        Demo,
        new("30 мин", 30, 0.5m),
        new("1 час", 60, 1m),
        new("2 часа −10%", 120, 1.8m),
    ];

    public static readonly MenuItem[] Menu =
    [
        new("🥤", "Энергетик", 150),
        new("🥤", "Кола", 100),
        new("🍟", "Чипсы", 120),
        new("🍕", "Пицца", 390),
        new("☕", "Кофе", 130),
        new("🍔", "Бургер", 290),
    ];

    // Пополнения: чем больше вносишь, тем больше бонус (клиент «заперт» в клубе)
    public static readonly TopUpOption[] TopUps =
    [
        new(300, 0), new(500, 0), new(1000, 0.10m), new(2000, 0.15m),
    ];

    public static readonly string[] Nicknames =
        ["Shadow", "Kira", "xX_Pro_Xx", "Nagibator", "Frost", "Luna", "Ghost", "Tanker", "Rin", "Zeus", "Hitman", "Pudge_Main"];

    public static readonly Random Rnd = new();
    public static string RandomNick() => Nicknames[Rnd.Next(Nicknames.Length)];
}

public record TopUpOption(decimal Amount, decimal BonusRate)
{
    public decimal Bonus => Amount * BonusRate;
    public string Caption => BonusRate > 0 ? $"{Fmt.Rub(Amount)}\n+{Fmt.Rub(Bonus)} бонус" : Fmt.Rub(Amount);
}

public static class Biz
{
    // Демо идёт в ускоренном времени: любая сессия длится 10–20 реальных секунд
    public static TimeSpan RealDuration(int tariffMinutes) => TimeSpan.FromSeconds(Math.Clamp(tariffMinutes / 3.0, 10, 20));
    public static readonly TimeSpan RealExtend = TimeSpan.FromSeconds(10);
    // Гость с телефона играет дольше, чтобы успел найти свой ник на большом экране
    public static readonly TimeSpan GuestDuration = TimeSpan.FromSeconds(60);

    // Окупаемость: сколько вложено в клуб и сколько чистой прибыли уже заработано до этой недели
    public const decimal Investment = 1_100_000m;   // 6 ПК, мебель, ремонт, сеть
    public const decimal ProfitBefore = 560_000m;   // прибыль с открытия до последних 7 дней
    public const decimal Margin = 0.40m;            // доля прибыли в выручке (после аренды, зарплат, света)

    // «Сегодня» = текущая смена: последние 12 часов (клуб работает через полночь)
    public const int ShiftHours = 12;
}

public static class Fmt
{
    static readonly NumberFormatInfo Nfi = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };
    static readonly string[] Days = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];

    public static string Rub(decimal v) => Math.Round(v).ToString("#,0", Nfi) + " ₽";
    public static string Num(decimal v) => Math.Round(v).ToString("#,0", Nfi);

    public static string Timer(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string Hours(double h)
    {
        var hh = (int)h;
        var mm = (int)Math.Round((h - hh) * 60);
        return hh > 0 ? $"{hh} ч {mm} мин" : $"{mm} мин";
    }

    public static string Day(DateTime local) => Days[(int)local.DayOfWeek];
}

public static class LogStyle
{
    public static (string Icon, string Color) For(string type) => type switch
    {
        "power_on" => ("", "#3FB950"),
        "power_off" => ("", "#545D68"),
        "session_start" => ("", "#4C8DFF"),
        "session_end" => ("", "#545D68"),
        "session_expired" => ("", "#D29922"),
        "extend" => ("", "#4C8DFF"),
        "game" => ("", "#8B93A1"),
        "bar" => ("", "#3FB950"),
        "topup" => ("", "#3FB950"),
        _ => ("", "#545D68"),
    };

    public static string SourceIcon(string source) => source switch
    {
        "mobile" => "📱",
        "desktop" => "🖥",
        "sim" => "🤖",
        _ => "⚙",
    };
}
