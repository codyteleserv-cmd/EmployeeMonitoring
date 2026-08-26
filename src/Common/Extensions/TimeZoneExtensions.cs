using System;

namespace EmployeeMonitoring.Common.Extensions;

/// <summary>
/// Time zone and scheduling utilities.
/// </summary>
public static class TimeZoneExtensions
{
    /// <summary>
    /// Gets the current time in the specified IANA time zone.
    /// </summary>
    public static DateTimeOffset InTimeZone(this DateTimeOffset utcTime, string ianaTimeZone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZone);
            return TimeZoneInfo.ConvertTime(utcTime, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback to UTC if time zone not found
            return utcTime;
        }
    }

    /// <summary>
    /// Checks if the current time in the given time zone falls within work hours.
    /// </summary>
    public static bool IsWorkHours(this DateTimeOffset time, WorkSchedule schedule)
    {
        var localTime = time.InTimeZone(schedule.Timezone);
        var dayOfWeek = (int)localTime.DayOfWeek; // 0=Sunday
        
        var daySchedule = schedule.Days.FirstOrDefault(d => d.DayOfWeek == dayOfWeek);
        if (daySchedule == null || !daySchedule.MonitoringEnabled)
            return false;

        if (!TimeSpan.TryParse(daySchedule.StartTime, out var start) ||
            !TimeSpan.TryParse(daySchedule.EndTime, out var end))
            return false;

        var currentTimeOfDay = localTime.TimeOfDay;
        return currentTimeOfDay >= start && currentTimeOfDay <= end;
    }

    /// <summary>
    /// Gets the next work hours start time.
    /// </summary>
    public static DateTimeOffset? GetNextWorkStart(this DateTimeOffset time, WorkSchedule schedule)
    {
        var localTime = time.InTimeZone(schedule.Timezone);
        
        for (int i = 0; i < 7; i++)
        {
            var checkDay = localTime.AddDays(i);
            var dayOfWeek = (int)checkDay.DayOfWeek;
            var daySchedule = schedule.Days.FirstOrDefault(d => d.DayOfWeek == dayOfWeek);
            
            if (daySchedule != null && daySchedule.MonitoringEnabled &&
                TimeSpan.TryParse(daySchedule.StartTime, out var start))
            {
                var nextStart = checkDay.Date + start;
                if (nextStart > localTime)
                    return nextStart;
            }
        }
        return null;
    }
}

/// <summary>
/// Work schedule configuration (simplified for common use).
/// </summary>
public record WorkSchedule(
    string Timezone,
    List<WorkDaySchedule> Days
);

public record WorkDaySchedule(
    int DayOfWeek, // 0=Sunday
    string StartTime, // HH:mm
    string EndTime,   // HH:mm
    bool MonitoringEnabled
);