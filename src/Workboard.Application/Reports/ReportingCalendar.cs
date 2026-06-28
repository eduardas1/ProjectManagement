namespace Workboard.Application.Reports;

public class ReportingCalendar(TimeProvider timeProvider, TimeZoneInfo timeZone)
{
    public string TimeZoneId => timeZone.Id;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
}
