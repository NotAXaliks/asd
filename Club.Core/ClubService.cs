using Microsoft.EntityFrameworkCore;

namespace Club.Core;

public record PcRow(int Id, string Name, string Zone, decimal Rate, string Status,
    string? Player, int? GameId, string? GameName, string? GameColor,
    DateTime? StartedAt, DateTime? EndsAt, decimal TodayRevenue, double TodayHours);

public record ChartPoint(string Label, decimal Value, bool Highlight, string Color = "#22D3EE", string Sub = "");

public record Stats(
    decimal Today, decimal TodayGame, decimal TodayBar, decimal YesterdaySameTime, decimal MonthForecast,
    int SessionsToday, double HoursToday, decimal AvgCheck, int BusyCount, int PcCount,
    decimal LostPerHour, decimal BarPerSession,
    decimal Balances, decimal TopUpsShift, int TopUpsCount,
    decimal PaybackPercent, decimal ProfitToDate, double PaybackMonths,
    List<ChartPoint> Minutes, List<ChartPoint> Daily, List<ChartPoint> TopGames, List<ChartPoint> PcRanking,
    List<string> Insights);

public static class ClubService
{
    public static string Source = "desktop";
    public static event Action? Changed;
    public static event Action<string>? Error;
    public static event Action<string>? Done;

    static async Task Run(Func<ClubDb, Task<string?>> action)
    {
        try
        {
            await using var db = new ClubDb();
            var msg = await action(db);
            await db.SaveChangesAsync();
            if (msg != null) Done?.Invoke(msg);
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Error?.Invoke(e.InnerException?.Message ?? e.Message);
        }
    }

    static void Log(ClubDb db, Pc pc, string type, string message, string? source) =>
        db.AuditLog.Add(new AuditEntry
        {
            CreatedAt = DateTime.UtcNow, PcId = pc.Id, Type = type,
            Message = $"{pc.Name} · {message}", Source = source ?? Source
        });

    static Task<Session?> Active(ClubDb db, int pcId) =>
        db.Sessions.Include(s => s.Game).FirstOrDefaultAsync(s => s.PcId == pcId && s.EndedAt == null);

    // ───────── Действия ─────────

    public static Task TogglePowerAsync(int pcId, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        if (pc == null) return null;
        if (pc.Status == PcStatus.Off)
        {
            pc.Status = PcStatus.Free;
            Log(db, pc, "power_on", "включён", source);
            return $"{pc.Name} включён";
        }
        var s = await Active(db, pcId);
        if (s != null)
        {
            s.EndedAt = DateTime.UtcNow;
            Log(db, pc, "power_off", $"выключен, сессия {s.Player} прервана", source);
        }
        else Log(db, pc, "power_off", "выключен", source);
        pc.Status = PcStatus.Off;
        return $"{pc.Name} выключен";
    });

    public static Task StartSessionAsync(int pcId, string player, int gameId, Tariff tariff,
        TimeSpan? realDuration = null, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        if (pc == null || pc.Status == PcStatus.Busy) return null;
        var game = await db.Games.FindAsync(gameId);
        if (pc.Status == PcStatus.Off) Log(db, pc, "power_on", "включён", source);

        var now = DateTime.UtcNow;
        var price = tariff.Price(pc.HourlyRate);
        var s = new Session
        {
            PcId = pcId, GameId = gameId, Player = string.IsNullOrWhiteSpace(player) ? Catalog.RandomNick() : player.Trim(),
            StartedAt = now, EndsAt = now + (realDuration ?? Biz.RealDuration(tariff.Minutes))
        };
        db.Sessions.Add(s);
        await db.SaveChangesAsync();
        db.Payments.Add(new Payment { PcId = pcId, SessionId = s.Id, Kind = "session", Title = "Тариф " + tariff.Name, Amount = price, CreatedAt = now });
        pc.Status = PcStatus.Busy;
        var pl = await db.Players.FirstOrDefaultAsync(x => x.Nickname == s.Player);
        var how = "";
        if (pl != null && pl.Balance >= price) { pl.Balance -= price; how = " с баланса"; }
        Log(db, pc, "session_start", $"{s.Player}: {tariff.Name} · +{Fmt.Rub(price)}{how}", source);
        db.AuditLog.Add(new AuditEntry
        {
            CreatedAt = now.AddMilliseconds(5), PcId = pc.Id, Type = "game",
            Message = $"{pc.Name} · запущена {game?.Name}", Source = source ?? Source
        });
        return $"+{Fmt.Rub(price)}";
    });

    public static Task ExtendAsync(int pcId, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        var s = await Active(db, pcId);
        if (pc == null || s == null) return null;
        var price = Catalog.Extend.Price(pc.HourlyRate);
        s.EndsAt = s.EndsAt + Biz.RealExtend;
        db.Payments.Add(new Payment { PcId = pcId, SessionId = s.Id, Kind = "extend", Title = "Продление 30 мин", Amount = price, CreatedAt = DateTime.UtcNow });
        Log(db, pc, "extend", $"продление +30 мин · +{Fmt.Rub(price)}", source);
        return $"+{Fmt.Rub(price)}";
    });

    public static Task EndSessionAsync(int pcId, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        var s = await Active(db, pcId);
        if (pc == null || s == null) return null;
        s.EndedAt = DateTime.UtcNow;
        pc.Status = PcStatus.Free;
        Log(db, pc, "session_end", $"сессия {s.Player} завершена", source);
        return $"{pc.Name} свободен";
    });

    public static Task ChangeGameAsync(int pcId, int gameId, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        var s = await Active(db, pcId);
        var g = await db.Games.FindAsync(gameId);
        if (pc == null || s == null || g == null || s.GameId == gameId) return null;
        s.GameId = gameId;
        Log(db, pc, "game", $"{s.Player} запустил {g.Name}", source);
        return g.Name;
    });

    public static Task SellAsync(int pcId, MenuItem item, string? source = null) => Run(async db =>
    {
        var pc = await db.Pcs.FindAsync(pcId);
        if (pc == null) return null;
        var s = await Active(db, pcId);
        db.Payments.Add(new Payment { PcId = pcId, SessionId = s?.Id, Kind = "bar", Title = item.Name, Amount = item.Price, CreatedAt = DateTime.UtcNow });
        Log(db, pc, "bar", $"{item.Name} · +{Fmt.Rub(item.Price)}", source);
        return $"+{Fmt.Rub(item.Price)}";
    });

    public static Task TopUpAsync(string nickname, TopUpOption opt, string? source = null) => Run(async db =>
    {
        nickname = string.IsNullOrWhiteSpace(nickname) ? Catalog.RandomNick() : nickname.Trim();
        var pl = await db.Players.FirstOrDefaultAsync(x => x.Nickname == nickname);
        if (pl == null) db.Players.Add(pl = new Player { Nickname = nickname });
        pl.Balance += opt.Amount + opt.Bonus;
        await db.SaveChangesAsync();
        db.TopUps.Add(new TopUp { PlayerId = pl.Id, Amount = opt.Amount, Bonus = opt.Bonus, CreatedAt = DateTime.UtcNow });
        db.AuditLog.Add(new AuditEntry
        {
            CreatedAt = DateTime.UtcNow, Type = "topup", Source = source ?? Source,
            Message = $"{nickname} пополнил баланс · +{Fmt.Rub(opt.Amount)}" + (opt.Bonus > 0 ? $" (+{Fmt.Rub(opt.Bonus)} бонус)" : "")
        });
        return $"Баланс {nickname}: {Fmt.Rub(pl.Balance)}";
    });

    public static async Task<List<Player>> GetPlayersAsync()
    {
        await using var db = new ClubDb();
        return await db.Players.AsNoTracking().OrderByDescending(p => p.Balance).ToListAsync();
    }

    public static async Task ExpireAsync()
    {
        await using var db = new ClubDb();
        await db.Database.ExecuteSqlRawAsync("SELECT expire_sessions()");
    }

    public static async Task ResetDemoAsync()
    {
        await using var db = new ClubDb();
        await db.Database.ExecuteSqlRawAsync("SELECT reset_demo()");
        Changed?.Invoke();
    }

    // Авто-режим: ускоренное время — сессия от входа до ухода длится 10–20 секунд
    public static async Task SimulateStepAsync()
    {
        var rnd = Catalog.Rnd;
        List<Pc> pcs;
        await using (var db = new ClubDb()) pcs = await db.Pcs.AsNoTracking().ToListAsync();
        Pc? Pick(Func<Pc, bool> f) { var l = pcs.Where(f).ToList(); return l.Count == 0 ? null : l[rnd.Next(l.Count)]; }

        // Каждое событие — в своём диапазоне, без «проваливания» в соседние ветки
        var roll = rnd.NextDouble();
        Pc? busy = Pick(p => p.Status == PcStatus.Busy);
        Pc? free = Pick(p => p.Status == PcStatus.Free);
        Pc? off = Pick(p => p.Status == PcStatus.Off);

        if (roll < 0.55)
        {
            // за ход садятся 1–2 гостя — в зале обычно занято 5–7 ПК из 9
            var guests = rnd.NextDouble() < 0.5 ? 2 : 1;
            foreach (var p in pcs.Where(p => p.Status == PcStatus.Free).OrderBy(_ => rnd.Next()).Take(guests))
                await StartSessionAsync(p.Id, Catalog.RandomNick(), rnd.Next(1, 7), rnd.NextDouble() < 0.75 ? Catalog.Express : Catalog.Tariffs[1],
                    TimeSpan.FromSeconds(rnd.Next(10, 21)), "sim");
        }
        else if (roll < 0.60) { if (busy != null) await SellAsync(busy.Id, Catalog.Menu[rnd.Next(Catalog.Menu.Length)], "sim"); }
        else if (roll < 0.65) { if (busy != null) await ChangeGameAsync(busy.Id, rnd.Next(1, 7), "sim"); }
        else if (roll < 0.67) await TopUpAsync(Catalog.RandomNick(), Catalog.TopUps[rnd.Next(Catalog.TopUps.Length)], "sim");
        else if (roll < 0.70) { if (busy != null) await EndSessionAsync(busy.Id, "sim"); }
        else if (roll < 0.76) { if (off != null) await TogglePowerAsync(off.Id, "sim"); }
        else if (roll < 0.78)
        {
            if (free != null && pcs.Count(p => p.Status == PcStatus.Off) < 2) await TogglePowerAsync(free.Id, "sim");
        }
    }

    // ───────── Чтение ─────────

    public static async Task<List<Game>> GetGamesAsync()
    {
        await using var db = new ClubDb();
        return await db.Games.AsNoTracking().OrderBy(g => g.Id).ToListAsync();
    }

    public static async Task<List<PcRow>> GetHallAsync()
    {
        await using var db = new ClubDb();
        var now = DateTime.UtcNow;
        var today = now.AddHours(-Biz.ShiftHours);
        var pcs = await db.Pcs.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var active = await db.Sessions.AsNoTracking().Include(s => s.Game).Where(s => s.EndedAt == null).ToListAsync();
        var rev = await db.Payments.Where(p => p.CreatedAt >= today && p.PcId != null)
            .GroupBy(p => p.PcId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);
        var sess = await db.Sessions.AsNoTracking().Where(s => s.StartedAt >= today)
            .Select(s => new { s.PcId, s.StartedAt, End = s.EndedAt ?? s.EndsAt }).ToListAsync();

        return pcs.Select(p =>
        {
            var s = active.FirstOrDefault(a => a.PcId == p.Id);
            var hours = sess.Where(x => x.PcId == p.Id)
                .Sum(x => ((x.End < now ? x.End : now) - x.StartedAt).TotalHours);
            return new PcRow(p.Id, p.Name, p.Zone, p.HourlyRate, p.Status, s?.Player, s?.GameId, s?.Game?.Name, s?.Game?.Color,
                s?.StartedAt, s?.EndsAt, rev.GetValueOrDefault(p.Id), Math.Max(0, hours));
        }).ToList();
    }

    public static async Task<List<AuditEntry>> GetLogAsync(long afterId, int take = 60)
    {
        await using var db = new ClubDb();
        if (afterId == 0)
            return (await db.AuditLog.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync())
                .OrderBy(a => a.CreatedAt).ToList();
        return await db.AuditLog.AsNoTracking().Where(a => a.Id > afterId).OrderBy(a => a.Id).ToListAsync();
    }

    public static async Task<Stats> GetStatsAsync()
    {
        await using var db = new ClubDb();
        var now = DateTime.UtcNow;
        var today = now.AddHours(-Biz.ShiftHours);
        var weekStart = now.AddDays(-7);

        var pays = await db.Payments.AsNoTracking().Where(p => p.CreatedAt >= weekStart)
            .Select(p => new { p.CreatedAt, p.Amount, p.Kind, p.PcId, p.SessionId }).ToListAsync();
        var sess = await db.Sessions.AsNoTracking().Where(s => s.StartedAt >= today)
            .Select(s => new { s.Id, s.GameId, s.PcId, s.StartedAt, End = s.EndedAt ?? s.EndsAt }).ToListAsync();
        var games = await db.Games.AsNoTracking().ToListAsync();
        var pcs = await db.Pcs.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var todayPays = pays.Where(p => p.CreatedAt >= today).ToList();
        decimal total = todayPays.Sum(p => p.Amount);
        decimal bar = todayPays.Where(p => p.Kind == "bar").Sum(p => p.Amount);
        // «к обычному дню»: средняя выручка за такую же смену в прошлые 6 дней
        decimal yesterday = Enumerable.Range(1, 6).Average(d =>
            pays.Where(p => p.CreatedAt >= today.AddDays(-d) && p.CreatedAt <= now.AddDays(-d)).Sum(p => p.Amount));
        var prevDays = Enumerable.Range(1, 6).Select(d => pays
            .Where(p => p.CreatedAt >= now.AddDays(-d - 1) && p.CreatedAt < now.AddDays(-d))
            .Sum(p => p.Amount)).ToList();
        decimal forecast = prevDays.Average() * 30;
        double hours = sess.Sum(s => Math.Max(0, ((s.End < now ? s.End : now) - s.StartedAt).TotalHours));
        int busy = pcs.Count(p => p.Status == PcStatus.Busy);
        decimal lost = pcs.Where(p => p.Status != PcStatus.Busy).Sum(p => p.HourlyRate);
        decimal barPerSession = sess.Count == 0 ? 0 : bar / sess.Count;

        // Живой график: выручка по минутам за последние 12 минут
        var minuteStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var minutes = Enumerable.Range(0, 12).Reverse().Select(i =>
        {
            var fu = minuteStart.AddMinutes(-i); var tu = fu.AddMinutes(1);
            return new ChartPoint(fu.ToLocalTime().ToString("HH:mm"), pays.Where(p => p.CreatedAt >= fu && p.CreatedAt < tu).Sum(p => p.Amount), i == 0);
        }).ToList();

        var daily = Enumerable.Range(0, 7).Reverse().Select(i =>
        {
            var tu = now.AddDays(-i); var fu = tu.AddDays(-1);
            return new ChartPoint(i == 0 ? "Сегодня" : Fmt.Day(fu.AddHours(12).ToLocalTime()),
                pays.Where(p => p.CreatedAt >= fu && p.CreatedAt < tu).Sum(p => p.Amount), i == 0);
        }).ToList();

        var sessGame = sess.ToDictionary(s => s.Id, s => s.GameId);
        var topGames = games.Select(g =>
        {
            var rev = todayPays.Where(p => p.SessionId != null && sessGame.TryGetValue(p.SessionId.Value, out var gid) && gid == g.Id).Sum(p => p.Amount);
            var h = sess.Where(s => s.GameId == g.Id).Sum(s => Math.Max(0, ((s.End < now ? s.End : now) - s.StartedAt).TotalHours));
            return new ChartPoint(g.Name, rev, false, g.Color, Fmt.Hours(h));
        }).OrderByDescending(x => x.Value).ToList();

        var ranking = pcs.Select(p => new ChartPoint(p.Name, todayPays.Where(x => x.PcId == p.Id).Sum(x => x.Amount), false,
                p.Zone == "VIP" ? "#FBBF24" : "#22D3EE", p.Zone))
            .OrderByDescending(x => x.Value).ToList();
        if (ranking.Count > 0) ranking[0] = ranking[0] with { Highlight = true };

        decimal balances = await db.Players.SumAsync(p => p.Balance);
        var shiftTopUps = await db.TopUps.Where(t => t.CreatedAt >= today).Select(t => t.Amount).ToListAsync();
        decimal week = pays.Sum(p => p.Amount);
        decimal profit = Biz.ProfitBefore + week * Biz.Margin;
        decimal monthlyProfit = forecast * Biz.Margin;
        decimal paybackPct = Math.Min(100, profit / Biz.Investment * 100);
        double months = monthlyProfit <= 0 ? 0 : Math.Max(0, (double)((Biz.Investment - profit) / monthlyProfit));

        var insights = new List<string>();

        return new Stats(total, total - bar, bar, yesterday, forecast, sess.Count, hours,
            sess.Count == 0 ? 0 : total / sess.Count, busy, pcs.Count, lost, barPerSession,
            balances, shiftTopUps.Sum(), shiftTopUps.Count, paybackPct, profit, months,
            minutes, daily, topGames, ranking, insights);
    }
}
