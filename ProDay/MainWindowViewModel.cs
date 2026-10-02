using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Club.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace ProDay;

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<PcVm> Pcs { get; } = new();
    public ObservableCollection<LogItemVm> Log { get; } = new();
    public ObservableCollection<BarVm> MinuteBars { get; } = new();
    public ObservableCollection<BarVm> DailyBars { get; } = new();
    public ObservableCollection<BarVm> GameBars { get; } = new();

    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private string _revenueText = "0 ₽";
    [ObservableProperty] private string _incrementText = "";
    [ObservableProperty] private double _incrementOpacity;
    [ObservableProperty] private string _vsYesterdayText = "";
    [ObservableProperty] private bool _vsYesterdayUp = true;
    [ObservableProperty] private string _forecastText = "";
    [ObservableProperty] private string _occupancyText = "";
    [ObservableProperty] private double _occupancy;
    [ObservableProperty] private string _avgCheckText = "";
    [ObservableProperty] private string _hoursText = "";
    [ObservableProperty] private string _sessionsText = "";
    [ObservableProperty] private string _lostText = "";
    [ObservableProperty] private string _gameRevenueText = "";
    [ObservableProperty] private string _barRevenueText = "";
    [ObservableProperty] private double _gameShare;
    [ObservableProperty] private string _gameShareText = "";
    [ObservableProperty] private string _barShareText = "";
    [ObservableProperty] private string _barPerSessionText = "";
    [ObservableProperty] private string _eventsToday = "";
    [ObservableProperty] private string _balancesText = "";
    [ObservableProperty] private string _topUpsText = "";
    [ObservableProperty] private double _payback;
    [ObservableProperty] private string _paybackText = "";
    [ObservableProperty] private string _paybackSubText = "";
    [ObservableProperty] private bool _showDaily;
    [ObservableProperty] private bool _isSimulation;
    [ObservableProperty] private bool _dbOnline = true;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private double _toastOpacity;

    // Страница брони для телефонов (проект Phone, крутится на сервере)
    public string PhoneUrl { get; } = Environment.GetEnvironmentVariable("PRODAY_PHONE_URL") ?? "http://213.226.112.230:5080/";
    public Bitmap PhoneQr => new(new MemoryStream(PngByteQRCodeHelper.GetQRCode(PhoneUrl, QRCodeGenerator.ECCLevel.M, 10)));

    public bool ShowMinutes => !ShowDaily;
    partial void OnShowDailyChanged(bool value) => OnPropertyChanged(nameof(ShowMinutes));

    decimal _target, _shown;
    bool _first = true, _busy;
    long _lastLogId;

    public MainWindowViewModel()
    {
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, async (_, _) => await Refresh()).Start();
        new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (_, _) => AnimateRevenue()).Start();
        new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, async (_, _) =>
        {
            if (IsSimulation) await ClubService.SimulateStepAsync();
        }).Start();

        ClubService.Changed += () => Dispatcher.UIThread.Post(async () => await Refresh());
        ClubService.Error += e => Dispatcher.UIThread.Post(() => { DbOnline = false; ErrorText = e; });
        _ = Refresh();
    }

    async Task Refresh()
    {
        Clock = DateTime.Now.ToString("HH:mm:ss");
        foreach (var p in Pcs) p.Tick();
        if (_busy) return;
        _busy = true;
        try
        {
            await ClubService.ExpireAsync();

            var hall = await ClubService.GetHallAsync();
            foreach (var r in hall)
            {
                var vm = Pcs.FirstOrDefault(p => p.Id == r.Id);
                if (vm == null) Pcs.Add(vm = new PcVm { Id = r.Id });
                vm.Update(r);
            }
            var top = Pcs.MaxBy(p => p.TodayRevenue);
            foreach (var p in Pcs) p.IsTopEarner = p == top;

            var log = await ClubService.GetLogAsync(_lastLogId);
            foreach (var a in log)
            {
                var item = LogItemVm.From(a, !_first);
                Log.Insert(0, item);
                if (!_first) _ = UnmarkLater(item);
                if (!_first && a.Source == "mobile" && a.Type != "game") _ = ShowToast(a.Message);
            }
            if (log.Count > 0) _lastLogId = Math.Max(_lastLogId, log.Max(a => a.Id));
            while (Log.Count > 80) Log.RemoveAt(Log.Count - 1);

            var st = await ClubService.GetStatsAsync();
            ApplyStats(st);

            DbOnline = true;
            _first = false;
        }
        catch (Exception e)
        {
            DbOnline = false;
            ErrorText = e.InnerException?.Message ?? e.Message;
        }
        finally { _busy = false; }
    }

    void ApplyStats(Stats st)
    {
        if (!_first && st.Today > _target)
            _ = ShowIncrement(st.Today - _target);
        _target = st.Today;
        if (_first) _shown = _target * 0.6m;

        var diff = st.YesterdaySameTime > 0 ? (double)(st.Today / st.YesterdaySameTime - 1) : 0;
        VsYesterdayUp = diff >= 0;
        VsYesterdayText = $"{(diff >= 0 ? "▲" : "▼")} {Math.Abs(diff):P0} к средней смене";
        ForecastText = "≈ " + Fmt.Rub(st.MonthForecast);
        Occupancy = st.PcCount == 0 ? 0 : 100.0 * st.BusyCount / st.PcCount;
        OccupancyText = $"{st.BusyCount} / {st.PcCount}  ·  {Occupancy:0}%";
        AvgCheckText = Fmt.Rub(st.AvgCheck);
        HoursText = Fmt.Hours(st.HoursToday);
        SessionsText = $"{st.SessionsToday} сессий";
        LostText = st.LostPerHour > 0 ? $"простой: −{Fmt.Rub(st.LostPerHour)} в час" : "зал загружен полностью";
        GameRevenueText = Fmt.Rub(st.TodayGame);
        BarRevenueText = Fmt.Rub(st.TodayBar);
        GameShare = st.Today == 0 ? 0 : (double)(st.TodayGame / st.Today * 100);
        GameShareText = $"{GameShare:0}%";
        BarShareText = $"{100 - GameShare:0}%";
        BarPerSessionText = $"{st.SessionsToday} сессий · бар {Fmt.Rub(st.BarPerSession)}";
        BalancesText = Fmt.Rub(st.Balances);
        TopUpsText = $"+{Fmt.Rub(st.TopUpsShift)} за смену · {st.TopUpsCount} пополн.";
        Payback = (double)st.PaybackPercent;
        PaybackText = $"{st.PaybackPercent:0.0}%";
        PaybackSubText = st.PaybackPercent >= 100 ? "клуб окупился" : $"ещё ≈ {st.PaybackMonths:0.0} мес · вложено {Fmt.Rub(Biz.Investment)}";
        EventsToday = $"{Log.Count}";

        BarVm.Sync(MinuteBars, st.Minutes, 100);
        BarVm.Sync(DailyBars, st.Daily, 100);
        BarVm.Sync(GameBars, st.TopGames, 120);
    }

    void AnimateRevenue()
    {
        if (_shown == _target) return;
        var d = (_target - _shown) * 0.12m;
        _shown = Math.Abs(_target - _shown) < 2 ? _target : _shown + d;
        RevenueText = Fmt.Rub(_shown);
    }

    async Task ShowIncrement(decimal v)
    {
        IncrementText = "+" + Fmt.Rub(v);
        IncrementOpacity = 1;
        await Task.Delay(2200);
        IncrementOpacity = 0;
    }

    async Task ShowToast(string text)
    {
        Toast = "С телефона: " + text;
        ToastOpacity = 1;
        await Task.Delay(4000);
        if (Toast.EndsWith(text)) ToastOpacity = 0;
    }

    static async Task UnmarkLater(LogItemVm item)
    {
        await Task.Delay(2500);
        item.IsNew = false;
    }

    [RelayCommand]
    async Task ResetDemo()
    {
        await ClubService.ResetDemoAsync();
        Log.Clear();
        _lastLogId = 0;
        _first = true;
        await Refresh();
    }
}
