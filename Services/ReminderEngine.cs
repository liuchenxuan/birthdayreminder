using Avalonia.Threading;
using BirthdayReminder.Models;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace BirthdayReminder.Services;

/// <summary>
/// 提醒引擎：根据“提醒时间点”（固定时间 / 第 N 节课下课后 / 放学）判断何时提醒，
/// 把命中的所有人合并成一条 <see cref="ReminderMessage"/>，并通过“已提醒记录”防止重复提醒。
/// 引擎只负责“何时、提醒谁”，具体怎么展示由订阅 <see cref="MessageReady"/> 的提供方决定，方便扩展。
/// </summary>
public class ReminderEngine
{
    private readonly PluginSettings _settings;
    private readonly BirthdayDataService _data;
    private readonly ILessonsService _lessons;
    private readonly IExactTimeService _time;
    private readonly ILogger<ReminderEngine> _logger;
    private DateTime _lastCheck = DateTime.MinValue;

    /// <summary>一条合并后的提醒准备好了（总是在 UI 线程触发）。</summary>
    public event Action<ReminderMessage>? MessageReady;

    public ReminderEngine(PluginSettings settings, BirthdayDataService data, ILessonsService lessons,
        IExactTimeService time, ILogger<ReminderEngine> logger)
    {
        _settings = settings;
        _data = data;
        _lessons = lessons;
        _time = time;
        _logger = logger;

        // 主计时器每秒多次触发，用于检查“固定时间”类时间点
        _lessons.PostMainTimerTicked += OnTick;
        _lessons.OnBreakingTime += (_, _) => OnLessonEnded(false);
        _lessons.OnAfterSchool += (_, _) => OnLessonEnded(true);
    }

    private DateTime Now => _time.GetCurrentLocalDateTime();

    // ───────────────────────── 触发 ─────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var now = Now;
            if ((now - _lastCheck).TotalSeconds < 1) return;
            _lastCheck = now;

            foreach (var trigger in _settings.Triggers.Where(t => t.Enabled && t.Kind == TriggerKinds.FixedTime).ToList())
            {
                if (!trigger.TryGetTimeOfDay(out var at)) continue;
                var elapsed = now.TimeOfDay - at;
                // 到点后的补发窗口：软件晚于提醒时间启动时仍会补发一次
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(Math.Max(1, _settings.CatchUpMinutes)))
                    continue;
                if (_data.IsFired(TriggerKey(trigger, now))) continue;
                RunOnUi(() => Fire(trigger, now));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查生日提醒时间点时出现异常");
        }
    }

    private void OnLessonEnded(bool afterSchool)
    {
        try
        {
            var now = Now;
            var finished = CountFinishedClasses(now);
            foreach (var trigger in _settings.Triggers.Where(t => t.Enabled).ToList())
            {
                var match = trigger.Kind switch
                {
                    TriggerKinds.AfterClass => finished > 0 && trigger.ClassIndex == finished,
                    TriggerKinds.AfterSchool => afterSchool,
                    _ => false
                };
                if (!match || _data.IsFired(TriggerKey(trigger, now))) continue;
                RunOnUi(() => Fire(trigger, now));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理下课/放学生日提醒时出现异常");
        }
    }

    /// <summary>今天已经结束的“上课”类型时间点数量。</summary>
    private int CountFinishedClasses(DateTime now)
    {
        var layouts = _lessons.CurrentClassPlan?.TimeLayout?.Layouts;
        if (layouts == null) return 0;
        var tolerance = TimeSpan.FromSeconds(2);
        return layouts.Count(l => l.TimeType == 0 && l.EndTime <= now.TimeOfDay + tolerance);
    }

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    // ───────────────────────── 防重复 ─────────────────────────

    private static string TriggerKey(ReminderTrigger t, DateTime day) => $"T|{day:yyyyMMdd}|{t.Id}";

    private string HitKey(ReminderTrigger t, BirthdayHit h, DateTime day) =>
        _settings.DedupAcrossTriggers
            ? $"H|{day:yyyyMMdd}|{h.Entry.Id}|{h.Offset}"
            : $"H|{day:yyyyMMdd}|{t.Id}|{h.Entry.Id}|{h.Offset}";

    private void Fire(ReminderTrigger trigger, DateTime now)
    {
        var day = now.Date;
        _data.MarkFired(TriggerKey(trigger, day)); // 这个时间点今天已处理，无论有没有人过生日

        var hits = _data.GetHits(day, _settings.GetOffsets())
            .Where(h => !_data.IsFired(HitKey(trigger, h, day)))
            .ToList();
        if (hits.Count == 0) return;

        foreach (var h in hits) _data.MarkFired(HitKey(trigger, h, day));

        var message = ReminderMessageBuilder.Build(hits, _settings);
        message.ShowBanner = _settings.EnableBanner;
        message.ShowCelebration = _settings.EnableCelebration && (message.HasToday || !_settings.CelebrationOnlyToday);
        message.Speak = _settings.EnableSpeech;
        _logger.LogInformation("生日提醒：{Mask}", message.MaskText);
        MessageReady?.Invoke(message);
    }

    // ───────────────────────── 测试 ─────────────────────────

    /// <summary>生成用于测试/预览的示例提醒：优先使用真实名单，没有名单时使用示例人物。</summary>
    public ReminderMessage BuildSampleMessage()
    {
        var today = Now.Date;
        var real = _data.ActiveEntries.Take(3).ToList();
        var hits = new List<BirthdayHit>();

        if (real.Count > 0)
        {
            foreach (var e in real.Take(2))
                hits.Add(new BirthdayHit
                {
                    Entry = e, Offset = 0, Date = today,
                    Age = e.Year > 0 ? today.Year - e.Year : 15
                });
            if (real.Count > 2)
                hits.Add(new BirthdayHit
                {
                    Entry = real[2], Offset = 3, Date = today.AddDays(3),
                    Age = real[2].Year > 0 ? today.AddDays(3).Year - real[2].Year : 16
                });
        }
        else
        {
            hits.Add(new BirthdayHit
            {
                Entry = new BirthdayEntry { Name = "张三", Year = today.Year - 15, Month = today.Month, Day = today.Day },
                Offset = 0, Date = today, Age = 15
            });
            hits.Add(new BirthdayHit
            {
                Entry = new BirthdayEntry { Name = "李四", Year = today.Year - 16, Month = today.Month, Day = today.Day },
                Offset = 0, Date = today, Age = 16
            });
            hits.Add(new BirthdayHit
            {
                Entry = new BirthdayEntry { Name = "王老师", Month = today.AddDays(3).Month, Day = today.AddDays(3).Day },
                Offset = 3, Date = today.AddDays(3), Age = null
            });
        }

        return ReminderMessageBuilder.Build(hits, _settings);
    }

    /// <summary>手动触发一次测试提醒（不写入“已提醒”记录，也不受开关限制）。</summary>
    public void RunTest(TestKind kind)
    {
        var message = BuildSampleMessage();
        message.IsTest = true;
        message.ShowBanner = kind is TestKind.Full or TestKind.Banner;
        message.ShowCelebration = kind is TestKind.Full or TestKind.Celebration;
        message.Speak = kind is TestKind.Full or TestKind.Speech;
        RunOnUi(() => MessageReady?.Invoke(message));
    }
}
