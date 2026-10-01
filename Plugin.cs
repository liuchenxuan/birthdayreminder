using BirthdayReminder.Controls;
using BirthdayReminder.Services;
using BirthdayReminder.Views.SettingsPages;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BirthdayReminder;

/// <summary>
/// 生日提醒插件入口（ClassIsland 2.x）。
///
/// 结构一览（方便扩展）：
///   Models/     数据模型（名单、提醒时间点、全局设置）
///   Services/   BirthdayCalculator（日期计算）→ BirthdayDataService（存取/搜索/导入）
///               → ReminderEngine（何时提醒谁，防重复）→ BirthdayReminderProvider / CelebrationService（怎么展示）
///   Controls/   主界面组件、全屏庆祝动画窗口
///   Views/      设置页
///
/// 想增加新的展示方式（如推送到手机、写入日志）只需订阅 ReminderEngine.MessageReady；
/// 想增加新的庆祝动画，在 CelebrationWindow 里增加一种 Style 即可。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var data = new BirthdayDataService(Path.Combine(PluginConfigFolder, "Settings.json"));
        services.AddSingleton(data);
        services.AddSingleton(data.Settings);
        services.AddSingleton<CelebrationService>();
        services.AddSingleton<ReminderEngine>();

        services.AddSettingsPage<BirthdaySettingsPage>();
        services.AddComponent<BirthdayComponent, BirthdayComponentSettingsControl>();
        services.AddNotificationProvider<BirthdayReminderProvider>();

        // 退出前保存，避免丢失最近的修改
        AppBase.Current.AppStopping += (_, _) => data.Flush();
    }
}
