using Club.Core;

// Страница брони для телефонов посетителей стенда: QR на большом экране ведёт сюда.
// Работает с той же БД, что и десктоп; десктоп видит действия через журнал (source = mobile).

var b = WebApplication.CreateBuilder();
b.Logging.ClearProviders();
b.WebHost.UseUrls("http://0.0.0.0:5080");
var app = b.Build();

var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "phone.html"));
app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));

app.MapGet("/api/state", async () =>
{
    await ClubService.ExpireAsync();   // десктоп может быть выключен — истёкшие сессии закрываем сами
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
    var ok = await ClubService.StartSessionAsync(r.PcId, nick, r.GameId, Catalog.Demo, Biz.GuestDuration, "mobile");
    return ok != null ? Results.Ok() : Results.Conflict("Этот ПК только что заняли — выбери другой");
});

app.MapPost("/api/extend", async (PcReq r) =>
    await ClubService.ExtendAsync(r.PcId, "mobile") != null ? Results.Ok() : Results.Conflict("Сессия уже закончилась"));

app.MapPost("/api/bar", async (BarReq r) =>
{
    if (r.Item < 0 || r.Item >= Catalog.Menu.Length) return Results.BadRequest("Нет такого товара");
    return await ClubService.SellAsync(r.PcId, Catalog.Menu[r.Item], "mobile") != null ? Results.Ok() : Results.Conflict("Ошибка");
});

app.Run();

record BookReq(int PcId, string Nick, int GameId);
record PcReq(int PcId);
record BarReq(int PcId, int Item);
