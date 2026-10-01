# 🎂 生日提醒（BirthdayReminder）

适用于 **ClassIsland 2.x** 的生日提醒插件。

## 功能

| 功能 | 说明 |
| --- | --- |
| 名单管理 | 姓名 + 公历生日（可不填年份），支持搜索、排序、增删、启用/停用 |
| Excel / CSV 导入导出 | 表头 `姓名`、`生日`（可选 `备注`）；提供模板下载；导入方式：合并 / 同名更新 / 清空覆盖 |
| 多档提前提醒 | 当天、提前 1 / 3 / 7 / 30 天，可再自定义任意天数（可同时多选） |
| 多个提醒时间点 | ① 固定时间（如 08:00）② 第 N 节课下课后 ③ 放学时；可同时配置多个，软件晚启动会在补发窗口内补发 |
| 合并成一条 | 同一时间点命中的所有人（当天 + 各档提前）合并为 **一条** 横幅 |
| 横幅提醒 | 注册为 ClassIsland **提醒提供方**（含“生日当天 / 生日预告”两个提醒渠道），可在【设置 → 提醒】中单独设置语音、音效、强调动画、置顶 |
| 全屏强调动画 | 生日当天播放全屏庆祝动画：彩带纷飞 / 气球升空 / 烟花绽放（Windows 下点击穿透） |
| 语音播报 | 自定义朗读内容；可使用 ClassIsland 语音服务独立播报 |
| 自定义提醒内容 | 遮罩、正文、语音、动画标题全部为模板，支持 `{name}` `{age}` `{when}` `{days}` `{date}` `{items}` `{names}` `{shortnames}` `{count}` |
| 主界面组件 | “生日提醒”组件：今日寿星 + 下一个生日（+倒计时）+ 本月剩余生日（已过的不计入），支持合并一行或轮播 |
| 自动计算年龄 | 按公历自动计算“将满几岁”，每年自动循环 |
| 防止重复提醒 | 每个时间点每天最多触发一次；每个人每档提醒当天只提醒一次；可选“跨时间点去重” |
| 测试按钮 | 完整提醒 / 仅横幅 / 仅全屏动画 / 仅语音 |

> 不使用农历；2 月 29 日出生者在平年默认于 2 月 28 日提醒（可改为 3 月 1 日）。

## 代码结构（方便扩展）

```
Plugin.cs                         入口：注册服务、设置页、组件、提醒提供方
Models/                           BirthdayEntry · ReminderTrigger · PluginSettings · BirthdayComponentSettings
Services/BirthdayCalculator.cs    日期解析与计算（下次生日 / 年龄 / 2 月 29 日）
Services/BirthdayDataService.cs   读写 Settings.json（防抖保存）· 搜索 · 导入合并 · 各类查询
Services/ExcelImporter.cs         xlsx / csv 读取与导出（MiniExcel）
Services/ReminderEngine.cs        何时提醒谁：时间点调度 · 防重复 · 合并 · 测试；事件 MessageReady
Services/ReminderMessageBuilder.cs 模板渲染，合并成一条
Services/BirthdayReminderProvider.cs  把 MessageReady 展示为横幅 / 语音 / 全屏动画
Controls/CelebrationWindow.cs     全屏庆祝动画（纯代码，新增样式只需加一个分支）
Controls/BirthdayComponent*.axaml 主界面组件及其设置
Views/SettingsPages/              设置页
```

扩展提示：
- **新的展示方式**（推送手机、写日志…）：订阅 `ReminderEngine.MessageReady` 即可，无需改动调度逻辑。
- **新的动画**：在 `CelebrationWindow` 增加 `Style*` 常量与 `Seed()` 分支。
- **新的提醒时间点类型**：在 `TriggerKinds` 增加类型，并在 `ReminderEngine` 增加触发判断。
- **新的数据来源**（如从 ClassIsland 档案、在线表格同步）：向 `BirthdayDataService.Import` 传入 `ParsedRow` 序列即可。
