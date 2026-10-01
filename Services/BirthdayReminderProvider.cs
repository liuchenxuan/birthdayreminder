using BirthdayReminder.Models;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;

namespace BirthdayReminder.Services;

/// <summary>
/// 生日提醒提供方：出现在 ClassIsland【设置 → 提醒】中，可单独设置语音、音效、强调动画、置顶等。
/// 所有命中的生日会被合并成一条横幅提醒（遮罩 + 正文）。
/// 两个提醒渠道让“生日当天”和“生日预告”可以分别设置。
/// </summary>
[NotificationProviderInfo(ProviderId, "生日提醒",
    description: "在同学/老师生日当天及提前若干天发出合并提醒，并可播放全屏庆祝动画、语音播报。")]
[NotificationChannelInfo(ChannelToday, "生日当天", description: "今日寿星的提醒。")]
[NotificationChannelInfo(ChannelUpcoming, "生日预告", description: "提前 N 天的生日预告提醒。")]
public class BirthdayReminderProvider : NotificationProviderBase
{
    public const string ProviderId = "6F1B7C0E-3D5A-4E9B-8A41-2C7D9E5F0B13";
    public const string ChannelToday = "A3C1E7D2-5B94-4F0A-9E6C-1D2B8F3A7C41";
    public const string ChannelUpcoming = "B8D2F4A6-7C13-4E5B-A09D-3E4C9A1B6D52";

    private readonly PluginSettings _settings;
    private readonly CelebrationService _celebration;
    private readonly ILogger<BirthdayReminderProvider> _logger;

    public BirthdayReminderProvider(ReminderEngine engine, PluginSettings settings, CelebrationService celebration,
        ILogger<BirthdayReminderProvider> logger)
    {
        _settings = settings;
        _celebration = celebration;
        _logger = logger;
        // 引擎只负责“何时、提醒谁”，这里负责“怎么展示”。要增加新的展示方式（如推送到手机）订阅同一个事件即可。
        engine.MessageReady += OnMessageReady;
    }

    private void OnMessageReady(ReminderMessage message)
    {
        try
        {
            if (message.ShowBanner) ShowBanner(message);
            if (message.ShowCelebration)
                _celebration.Show(message.CelebrationTitle, message.CelebrationSubtitle,
                    _settings.CelebrationStyle, _settings.CelebrationSeconds);
            if (message.Speak && !string.IsNullOrWhiteSpace(message.SpeechText) && _settings.StandaloneSpeech)
                Speak(message.SpeechText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示生日提醒失败");
        }
    }

    private void ShowBanner(ReminderMessage message)
    {
        var standalone = _settings.StandaloneSpeech;

        // 遮罩（横幅）：短文本，进入时显示
        var mask = NotificationContent.CreateTwoIconsMask(message.MaskText, factory: x =>
        {
            x.Duration = TimeSpan.FromSeconds(Math.Max(2, _settings.BannerMaskSeconds));
            // 使用“独立语音播报”时由插件自己朗读，避免和宿主语音重复
            x.IsSpeechEnabled = message.Speak && !standalone;
            x.SpeechContent = message.SpeechText;
        });

        // 正文（合并后的完整内容）：过长时自动改为滚动文本
        NotificationContent? overlay = null;
        if (!string.IsNullOrWhiteSpace(message.OverlayText))
        {
            var text = message.OverlayText;
            if (text.Length > 28)
            {
                var seconds = Math.Clamp(text.Length * 0.4, Math.Max(8, _settings.BannerOverlaySeconds), 45);
                overlay = NotificationContent.CreateRollingTextContent(text, TimeSpan.FromSeconds(seconds), 1,
                    x => x.IsSpeechEnabled = false);
            }
            else
            {
                overlay = NotificationContent.CreateSimpleTextContent(text, x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Math.Max(3, _settings.BannerOverlaySeconds));
                    x.IsSpeechEnabled = false;
                });
            }
        }

        var request = new NotificationRequest { MaskContent = mask, OverlayContent = overlay };

        try
        {
            Channel(message.HasToday ? ChannelToday : ChannelUpcoming).ShowNotification(request);
        }
        catch (InvalidOperationException)
        {
            // 渠道不可用时退回到提供方级别发送
            ShowNotification(request);
        }
    }

    private void Speak(string text)
    {
        try
        {
            IAppHost.GetService<ISpeechService>().EnqueueSpeechQueue(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "语音播报失败");
        }
    }
}
