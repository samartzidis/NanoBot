using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace NanoBot.Plugins.Native;

public sealed class DateTimePlugin
{
    private readonly ILogger _logger;

    public DateTimePlugin(ILogger<DateTimePlugin> logger)
    {
        _logger = logger;
    }

    #region Current Time Functions
    [KernelFunction, Description("Get the current date and time")]
    public async Task<string> GetCurrentDateTimeAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var result = now.ToString("yyyy-MM-dd HH:mm:ss");
        _logger.LogInformation("GetCurrentDateTimeAsync: Current time is {DateTime}", result);
        return result;
    }

    [KernelFunction, Description("Get the current date")]
    public async Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var result = today.ToString("yyyy-MM-dd");
        _logger.LogInformation("GetCurrentDateAsync: Current date is {Date}", result);
        return result;
    }

    [KernelFunction, Description("Get the current time")]
    public async Task<string> GetCurrentTimeAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var result = now.ToString("HH:mm:ss");
        _logger.LogInformation("GetCurrentTimeAsync: Current time is {Time}", result);
        return result;
    }

    [KernelFunction, Description("Get the current UTC date and time")]
    public async Task<string> GetCurrentUtcDateTimeAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var result = utcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        _logger.LogInformation("GetCurrentUtcDateTimeAsync: Current UTC time is {DateTime}", result);
        return result;
    }
    #endregion
    
    #region Date Calculation Functions
    [KernelFunction, Description("Add days to a date")]
    public async Task<string> AddDaysAsync(
        [Description("Date string")] string dateString,
        [Description("Number of days to add (can be negative)")] int days,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(dateString, out var date))
            {
                var result = date.AddDays(days).ToString("yyyy-MM-dd HH:mm:ss");
                _logger.LogInformation("AddDaysAsync: '{DateString}' + {Days} days = '{Result}'", dateString, days, result);
                return result;
            }
            else
            {
                _logger.LogWarning("AddDaysAsync: Invalid date string provided: {DateString}", dateString);
                return $"Error: Invalid date string '{dateString}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddDaysAsync: Error adding {Days} days to '{DateString}'", days, dateString);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Add hours to a date")]
    public async Task<string> AddHoursAsync(
        [Description("Date string")] string dateString,
        [Description("Number of hours to add (can be negative)")] int hours,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(dateString, out var date))
            {
                var result = date.AddHours(hours).ToString("yyyy-MM-dd HH:mm:ss");
                _logger.LogInformation("AddHoursAsync: '{DateString}' + {Hours} hours = '{Result}'", dateString, hours, result);
                return result;
            }
            else
            {
                _logger.LogWarning("AddHoursAsync: Invalid date string provided: {DateString}", dateString);
                return $"Error: Invalid date string '{dateString}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddHoursAsync: Error adding {Hours} hours to '{DateString}'", hours, dateString);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Add minutes to a date")]
    public async Task<string> AddMinutesAsync(
        [Description("Date string")] string dateString,
        [Description("Number of minutes to add (can be negative)")] int minutes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(dateString, out var date))
            {
                var result = date.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm:ss");
                _logger.LogInformation("AddMinutesAsync: '{DateString}' + {Minutes} minutes = '{Result}'", dateString, minutes, result);
                return result;
            }
            else
            {
                _logger.LogWarning("AddMinutesAsync: Invalid date string provided: {DateString}", dateString);
                return $"Error: Invalid date string '{dateString}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddMinutesAsync: Error adding {Minutes} minutes to '{DateString}'", minutes, dateString);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Calculate the difference between two dates in days")]
    public async Task<string> GetDaysDifferenceAsync(
        [Description("First date string")] string date1String,
        [Description("Second date string")] string date2String,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(date1String, out var date1) && DateTime.TryParse(date2String, out var date2))
            {
                var difference = (date2 - date1).TotalDays;
                var result = Math.Round(difference, 2).ToString();
                _logger.LogInformation("GetDaysDifferenceAsync: '{Date1String}' to '{Date2String}' = {Difference} days", 
                    date1String, date2String, result);
                return result;
            }
            else
            {
                _logger.LogWarning("GetDaysDifferenceAsync: Invalid date string provided: '{Date1String}' or '{Date2String}'", 
                    date1String, date2String);
                return "Error: Invalid date string(s) provided";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDaysDifferenceAsync: Error calculating difference between '{Date1String}' and '{Date2String}'", 
                date1String, date2String);
            return $"Error: {ex.Message}";
        }
    }
    #endregion

    #region Date Information Functions
    [KernelFunction, Description("Get the day of the week for a date")]
    public async Task<string> GetDayOfWeekAsync(
        [Description("Date string")] string dateString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(dateString, out var date))
            {
                var result = date.DayOfWeek.ToString();
                _logger.LogInformation("GetDayOfWeekAsync: '{DateString}' is a {DayOfWeek}", dateString, result);
                return result;
            }
            else
            {
                _logger.LogWarning("GetDayOfWeekAsync: Invalid date string provided: {DateString}", dateString);
                return $"Error: Invalid date string '{dateString}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDayOfWeekAsync: Error getting day of week for '{DateString}'", dateString);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Check if a year is a leap year")]
    public async Task<string> IsLeapYearAsync(
        [Description("Year to check")] int year,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isLeap = DateTime.IsLeapYear(year);
            var result = isLeap ? "Yes" : "No";
            _logger.LogInformation("IsLeapYearAsync: {Year} is {Result} a leap year", year, isLeap ? "" : "not");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsLeapYearAsync: Error checking leap year for {Year}", year);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get the number of days in a specific month")]
    public async Task<string> GetDaysInMonthAsync(
        [Description("Year")] int year,
        [Description("Month (1-12)")] int month,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (month < 1 || month > 12)
            {
                _logger.LogWarning("GetDaysInMonthAsync: Invalid month provided: {Month}", month);
                return "Error: Month must be between 1 and 12";
            }

            var days = DateTime.DaysInMonth(year, month);
            _logger.LogInformation("GetDaysInMonthAsync: {Year}-{Month} has {Days} days", year, month, days);
            return days.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDaysInMonthAsync: Error getting days in month {Year}-{Month}", year, month);
            return $"Error: {ex.Message}";
        }
    }
    #endregion

    #region Time Zone Functions
    [KernelFunction, Description("Convert a date from one time zone to another")]
    public async Task<string> ConvertTimeZoneAsync(
        [Description("Date and time string")] string dateTimeString,
        [Description("Source time zone (e.g., 'UTC', 'Eastern Standard Time')")] string sourceTimeZone,
        [Description("Target time zone (e.g., 'UTC', 'Eastern Standard Time')")] string targetTimeZone,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.TryParse(dateTimeString, out var dateTime))
            {
                var sourceTz = TimeZoneInfo.FindSystemTimeZoneById(sourceTimeZone);
                var targetTz = TimeZoneInfo.FindSystemTimeZoneById(targetTimeZone);
                
                var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(dateTime, sourceTz);
                var convertedDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, targetTz);
                
                var result = convertedDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                _logger.LogInformation("ConvertTimeZoneAsync: '{DateTimeString}' from {SourceTz} to {TargetTz} = '{Result}'", 
                    dateTimeString, sourceTimeZone, targetTimeZone, result);
                return result;
            }
            else
            {
                _logger.LogWarning("ConvertTimeZoneAsync: Invalid date string provided: {DateTimeString}", dateTimeString);
                return $"Error: Invalid date string '{dateTimeString}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConvertTimeZoneAsync: Error converting '{DateTimeString}' from {SourceTz} to {TargetTz}", 
                dateTimeString, sourceTimeZone, targetTimeZone);
            return $"Error: {ex.Message}";
        }
    }
    #endregion
}
