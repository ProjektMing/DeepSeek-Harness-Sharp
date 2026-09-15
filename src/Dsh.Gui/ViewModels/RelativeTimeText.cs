namespace Dsh.Gui.ViewModels;

/** 侧栏与会话流共用的相对时间文案。 */
public static class RelativeTimeText
{
    private const int MinuteSeconds = 60;
    private const int HourSeconds = 60 * MinuteSeconds;
    private const int DaySeconds = 24 * HourSeconds;

    public static string Format(DateTimeOffset moment)
    {
        var now = DateTimeOffset.Now;
        if (moment > now)
            return "刚刚";
        var elapsed = now - moment;
        if (elapsed.TotalSeconds < MinuteSeconds)
            return "刚刚";
        if (elapsed.TotalSeconds < HourSeconds)
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        if (elapsed.TotalSeconds < DaySeconds)
            return $"{(int)elapsed.TotalHours} 小时前";
        if (moment.Date == now.Date.AddDays(-1))
            return "昨天";
        if (elapsed.TotalDays < 30)
            return $"{(int)elapsed.TotalDays} 天前";
        return moment.Year == now.Year ? $"{moment.Month}月{moment.Day}日" : $"{moment.Year}年{moment.Month}月{moment.Day}日";
    }
}
