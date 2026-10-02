using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Club.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ProDay;

// Мини-сайт для телефонов посетителей стенда: сканируют QR на экране и бронируют ПК сами.
// Телефон должен быть в той же Wi-Fi сети, что и ноутбук (проще всего — раздать точку доступа с ноутбука).
public static class PhoneServer
{
    const int Port = 5080;
    public const string Source = "mobile";

    public static string Url { get; } = $"http://{LanIp()}:{Port}/";

    public record BookReq(int PcId, string Nick, int GameId);
    public record PcReq(int PcId);
    public record BarReq(int PcId, int Item);

    public static void Start()
    {
        var b = WebApplication.CreateBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        var app = b.Build();

        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "phone.html"));
        app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));

        app.MapGet("/api/state", async () =>
        {
            var hall = await ClubService.GetHallAsync();
            var games = await ClubService.GetGamesAsync();
            return Results.Ok(new
            {
                pcs = hall.Select(p => new
                {
                    p.Id, p.Name, p.Zone, p.Status, p.Player, p.GameName,
                    left = p.EndsAt is { } e ? Math.Max(0, (int)(e - DateTime.UtcNow).TotalSeconds) : 0
                }),
                games = games.Select(g => new { g.Id, g.Name, g.Color }),
                menu = Catalog.Menu.Select((m, i) => new { id = i, m.Name, m.Price }),
            });
        });

        app.MapPost("/api/book", async (BookReq r) =>
        {
            var nick = (r.Nick ?? "").Trim();
            if (nick.Length is < 2 or > 16) return Results.BadRequest("Ник: от 2 до 16 символов");
            if (!(await ClubService.GetGamesAsync()).Any(g => g.Id == r.GameId)) return Results.BadRequest("Выбери игру");
            var ok = await ClubService.StartSessionAsync(r.PcId, nick, r.GameId, Catalog.Demo, Biz.GuestDuration, Source);
            return ok != null ? Results.Ok() : Results.Conflict("Этот ПК только что заняли — выбери другой");
        });

        app.MapPost("/api/extend", async (PcReq r) =>
            await ClubService.ExtendAsync(r.PcId, Source) != null ? Results.Ok() : Results.Conflict("Сессия уже закончилась"));

        app.MapPost("/api/bar", async (BarReq r) =>
        {
            if (r.Item < 0 || r.Item >= Catalog.Menu.Length) return Results.BadRequest("Нет такого товара");
            return await ClubService.SellAsync(r.PcId, Catalog.Menu[r.Item], Source) != null ? Results.Ok() : Results.Conflict("Ошибка");
        });

        _ = app.RunAsync();
    }

    // IP ноутбука в локальной сети. Не угадал — задать вручную: переменная окружения PRODAY_HOST=192.168.x.x
    static string LanIp()
    {
        if (Environment.GetEnvironmentVariable("PRODAY_HOST") is { Length: > 0 } h) return h;
        var addrs = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .OrderByDescending(p => p.GatewayAddresses.Count > 0)
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
        return addrs.FirstOrDefault()?.ToString() ?? "localhost";
    }
}
