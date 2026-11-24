using AwesomeAssertions;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Domain.UnitTests;

public sealed class SlotGeneratorTests
{
    private static readonly TimeZoneInfo Zagreb = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");

    [Fact]
    public void Generates_departures_only_on_selected_days()
    {
        var tour = TestData.Tour();
        var rule = tour.AddScheduleRule(
            Guid.NewGuid(), DaysOfWeek.Saturday | DaysOfWeek.Sunday, new TimeOnly(9, 0), new DateOnly(2026, 6, 1), null).Value;

        // 2026-06-01 is a Monday; the 14 days contain two weekends.
        var slots = SlotGenerator.Generate(tour, rule, 12, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 14), TestData.Now);

        slots.Select(s => s.LocalDate.DayOfWeek).Should().Equal(
            DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Saturday, DayOfWeek.Sunday);
    }

    [Fact]
    public void Local_start_time_is_kept_across_daylight_saving_change()
    {
        // Summer (UTC+2) vs winter (UTC+1): 09:00 local is 07:00 UTC in summer and 08:00 UTC in winter.
        SlotGenerator.ToUtc(new DateTime(2026, 7, 4, 9, 0, 0), Zagreb).Hour.Should().Be(7);
        SlotGenerator.ToUtc(new DateTime(2026, 11, 7, 9, 0, 0), Zagreb).Hour.Should().Be(8);
    }

    [Fact]
    public void Time_skipped_by_spring_forward_moves_one_hour_later()
    {
        // 2026-03-29 02:30 does not exist in Zagreb (clocks jump from 02:00 to 03:00).
        var utc = SlotGenerator.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Zagreb);

        TimeZoneInfo.ConvertTime(utc, Zagreb).TimeOfDay.Should().Be(new TimeSpan(3, 30, 0));
    }

    [Fact]
    public void Inactive_rule_generates_nothing()
    {
        var tour = TestData.Tour();
        var rule = tour.AddScheduleRule(Guid.NewGuid(), DaysOfWeek.Saturday, new TimeOnly(9, 0), new DateOnly(2026, 6, 1), null).Value;
        tour.DeactivateScheduleRule(rule.Id);

        SlotGenerator.Generate(tour, rule, 12, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), TestData.Now).Should().BeEmpty();
    }
}
