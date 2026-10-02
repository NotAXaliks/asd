using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Club.Core;

public partial class PcVm : ObservableObject
{
    public int Id { get; init; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _zone = "";
    [ObservableProperty] private bool _isVip;
    [ObservableProperty] private decimal _rate;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isOff;
    [ObservableProperty] private bool _isFree;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isExpiring;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _player = "";
    [ObservableProperty] private int? _gameId;
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _gameColor = "#8A94B0";
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _todayRevenueText = "";
    [ObservableProperty] private decimal _todayRevenue;
    [ObservableProperty] private string _todayHoursText = "";
    [ObservableProperty] private bool _isTopEarner;
    [ObservableProperty] private string _powerText = "";
    [ObservableProperty] private string _rateText = "";

    DateTime? _start, _end;

    public void Update(PcRow r)
    {
        Name = r.Name; Zone = r.Zone; IsVip = r.Zone == "VIP"; Rate = r.Rate;
        RateText = $"{Fmt.Rub(r.Rate)}/ч";
        Status = r.Status;
        IsOff = r.Status == PcStatus.Off; IsFree = r.Status == PcStatus.Free; IsBusy = r.Status == PcStatus.Busy;
        StatusText = IsOff ? "Выключен" : IsFree ? "Свободен" : "Занят";
        PowerText = IsOff ? "Включить" : "Выключить";
        Player = r.Player ?? ""; GameId = r.GameId; GameName = r.GameName ?? "";
        GameColor = r.GameColor ?? "#8A94B0";
        _start = r.StartedAt; _end = r.EndsAt;
        TodayRevenue = r.TodayRevenue;
        TodayRevenueText = Fmt.Rub(r.TodayRevenue);
        TodayHoursText = Fmt.Hours(r.TodayHours);
        Tick();
    }

    public void Tick()
    {
        if (IsBusy && _start != null && _end != null)
        {
            var left = _end.Value - DateTime.UtcNow;
            var total = (_end.Value - _start.Value).TotalSeconds;
            RemainingText = Fmt.Timer(left);
            Progress = total <= 0 ? 100 : Math.Clamp((1 - left.TotalSeconds / total) * 100, 0, 100);
            IsExpiring = left.TotalSeconds < Math.Min(60, total * 0.3);
        }
        else
        {
            RemainingText = ""; Progress = 0; IsExpiring = false;
        }
    }

    [RelayCommand] Task TogglePower() => ClubService.TogglePowerAsync(Id);
    [RelayCommand] Task QuickStart(string minutes)
    {
        var t = minutes == "30" ? Catalog.Tariffs[1] : Catalog.Tariffs[2];
        return ClubService.StartSessionAsync(Id, Catalog.RandomNick(), Catalog.Rnd.Next(1, 7), t);
    }
    [RelayCommand] Task Extend() => ClubService.ExtendAsync(Id);
    [RelayCommand] Task End() => ClubService.EndSessionAsync(Id);
}

public partial class BarVm : ObservableObject
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private decimal _value;
    [ObservableProperty] private string _valueText = "";
    [ObservableProperty] private double _size;
    [ObservableProperty] private bool _highlight;
    [ObservableProperty] private string _color = "#22D3EE";
    [ObservableProperty] private string _sub = "";

    public static void Sync(ObservableCollection<BarVm> dst, IList<ChartPoint> src, double maxPx)
    {
        while (dst.Count > src.Count) dst.RemoveAt(dst.Count - 1);
        while (dst.Count < src.Count) dst.Add(new BarVm());
        var max = src.Count == 0 ? 0 : src.Max(p => p.Value);
        for (int i = 0; i < src.Count; i++)
        {
            var p = src[i]; var b = dst[i];
            b.Label = p.Label; b.Value = p.Value; b.ValueText = Fmt.Rub(p.Value);
            b.Highlight = p.Highlight; b.Color = p.Color; b.Sub = p.Sub;
            b.Size = max > 0 ? Math.Max(4, (double)(p.Value / max) * maxPx) : 4;
        }
    }
}

public partial class LogItemVm : ObservableObject
{
    public string Time { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Color { get; init; } = "";
    public string Message { get; init; } = "";
    public string SourceIcon { get; init; } = "";
    [ObservableProperty] private bool _isNew;

    public static LogItemVm From(AuditEntry a, bool isNew)
    {
        var (icon, color) = LogStyle.For(a.Type);
        return new LogItemVm
        {
            Time = a.CreatedAt.ToLocalTime().ToString("HH:mm:ss"), Icon = icon, Color = color,
            Message = a.Message, SourceIcon = LogStyle.SourceIcon(a.Source), IsNew = isNew
        };
    }
}
