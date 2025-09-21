using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace NanoBot.Plugins.Native;

public sealed class CalculatorPlugin
{
    private readonly ILogger _logger;

    public CalculatorPlugin(ILogger<CalculatorPlugin> logger)
    {
        _logger = logger;
    }

    #region Basic Operations
    [KernelFunction, Description("Add two numbers")]
    public async Task<double> AddAsync(
        [Description("First number")] double a,
        [Description("Second number")] double b,
        CancellationToken cancellationToken = default)
    {
        var result = a + b;
        _logger.LogInformation("AddAsync: {A} + {B} = {Result}", a, b, result);
        return result;
    }

    [KernelFunction, Description("Subtract the second number from the first number")]
    public async Task<double> SubtractAsync(
        [Description("First number")] double a,
        [Description("Second number")] double b,
        CancellationToken cancellationToken = default)
    {
        var result = a - b;
        _logger.LogInformation("SubtractAsync: {A} - {B} = {Result}", a, b, result);
        return result;
    }

    [KernelFunction, Description("Multiply two numbers")]
    public async Task<double> MultiplyAsync(
        [Description("First number")] double a,
        [Description("Second number")] double b,
        CancellationToken cancellationToken = default)
    {
        var result = a * b;
        _logger.LogInformation("MultiplyAsync: {A} * {B} = {Result}", a, b, result);
        return result;
    }

    [KernelFunction, Description("Divide the first number by the second number")]
    public async Task<string> DivideAsync(
        [Description("First number")] double a,
        [Description("Second number")] double b,
        CancellationToken cancellationToken = default)
    {
        if (b == 0)
        {
            _logger.LogWarning("DivideAsync: Division by zero attempted with a={A}", a);
            return "Error: Division by zero";
        }

        var result = a / b;
        _logger.LogInformation("DivideAsync: {A} / {B} = {Result}", a, b, result);
        return result.ToString();
    }
    #endregion

    #region List Operations
    [KernelFunction, Description("Add a list of numbers")]
    public async Task<double> AddListAsync(
        [Description("A list of numbers to add")] List<double> numbers,
        CancellationToken cancellationToken = default)
    {
        var sum = numbers?.Sum() ?? 0;
        _logger.LogInformation("AddListAsync: [{Numbers}] = {Sum}", string.Join(", ", numbers ?? new()), sum);
        return sum;
    }

    [KernelFunction, Description("Multiply a list of numbers")]
    public async Task<double> MultiplyListAsync(
        [Description("A list of numbers to multiply")] List<double> numbers,
        CancellationToken cancellationToken = default)
    {
        if (numbers == null || numbers.Count == 0)
        {
            _logger.LogWarning("MultiplyListAsync: Empty or null list provided");
            return 0.0;
        }

        double result = 1;
        foreach (var num in numbers)
        {
            result *= num;
        }

        _logger.LogInformation("MultiplyListAsync: [{Numbers}] = {Product}", string.Join(", ", numbers), result);
        return result;
    }
    #endregion

    #region Power and Root Functions
    [KernelFunction, Description("Raise a number to a power")]
    public async Task<double> PowerAsync(
        [Description("Base number")] double baseNumber,
        [Description("Exponent")] double exponent,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Pow(baseNumber, exponent);
        _logger.LogInformation("PowerAsync: {Base}^{Exponent} = {Result}", baseNumber, exponent, result);
        return result;
    }

    [KernelFunction, Description("Calculate the square root of a number")]
    public async Task<string> SquareRootAsync(
        [Description("Number to find square root of")] double number,
        CancellationToken cancellationToken = default)
    {
        if (number < 0)
        {
            _logger.LogWarning("SquareRootAsync: Negative number provided: {Number}", number);
            return "Error: Cannot calculate square root of negative number";
        }

        var result = Math.Sqrt(number);
        _logger.LogInformation("SquareRootAsync: √{Number} = {Result}", number, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate the nth root of a number")]
    public async Task<string> NthRootAsync(
        [Description("Number to find root of")] double number,
        [Description("Root degree (e.g., 3 for cube root)")] double n,
        CancellationToken cancellationToken = default)
    {
        if (n == 0)
        {
            _logger.LogWarning("NthRootAsync: Root degree cannot be zero");
            return "Error: Root degree cannot be zero";
        }

        if (number < 0 && n % 2 == 0)
        {
            _logger.LogWarning("NthRootAsync: Even root of negative number: {Number}^(1/{N})", number, n);
            return "Error: Cannot calculate even root of negative number";
        }

        var result = Math.Pow(Math.Abs(number), 1.0 / n);
        if (number < 0 && n % 2 != 0)
        {
            result = -result;
        }

        _logger.LogInformation("NthRootAsync: {Number}^(1/{N}) = {Result}", number, n, result);
        return result.ToString();
    }
    #endregion

    #region Trigonometric Functions
    [KernelFunction, Description("Calculate sine of an angle in radians")]
    public async Task<double> SinAsync(
        [Description("Angle in radians")] double angle,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Sin(angle);
        _logger.LogInformation("SinAsync: sin({Angle}) = {Result}", angle, result);
        return result;
    }

    [KernelFunction, Description("Calculate cosine of an angle in radians")]
    public async Task<double> CosAsync(
        [Description("Angle in radians")] double angle,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Cos(angle);
        _logger.LogInformation("CosAsync: cos({Angle}) = {Result}", angle, result);
        return result;
    }

    [KernelFunction, Description("Calculate tangent of an angle in radians")]
    public async Task<double> TanAsync(
        [Description("Angle in radians")] double angle,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Tan(angle);
        _logger.LogInformation("TanAsync: tan({Angle}) = {Result}", angle, result);
        return result;
    }

    [KernelFunction, Description("Calculate arcsine (inverse sine) of a value, returns angle in radians")]
    public async Task<string> AsinAsync(
        [Description("Value between -1 and 1")] double value,
        CancellationToken cancellationToken = default)
    {
        if (value < -1 || value > 1)
        {
            _logger.LogWarning("AsinAsync: Value out of range [-1, 1]: {Value}", value);
            return "Error: Value must be between -1 and 1";
        }

        var result = Math.Asin(value);
        _logger.LogInformation("AsinAsync: asin({Value}) = {Result}", value, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate arccosine (inverse cosine) of a value, returns angle in radians")]
    public async Task<string> AcosAsync(
        [Description("Value between -1 and 1")] double value,
        CancellationToken cancellationToken = default)
    {
        if (value < -1 || value > 1)
        {
            _logger.LogWarning("AcosAsync: Value out of range [-1, 1]: {Value}", value);
            return "Error: Value must be between -1 and 1";
        }

        var result = Math.Acos(value);
        _logger.LogInformation("AcosAsync: acos({Value}) = {Result}", value, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate arctangent (inverse tangent) of a value, returns angle in radians")]
    public async Task<double> AtanAsync(
        [Description("Value")] double value,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Atan(value);
        _logger.LogInformation("AtanAsync: atan({Value}) = {Result}", value, result);
        return result;
    }
    #endregion

    #region Angle Conversion
    [KernelFunction, Description("Convert degrees to radians")]
    public async Task<double> DegreesToRadiansAsync(
        [Description("Angle in degrees")] double degrees,
        CancellationToken cancellationToken = default)
    {
        var result = degrees * Math.PI / 180.0;
        _logger.LogInformation("DegreesToRadiansAsync: {Degrees}° = {Result} rad", degrees, result);
        return result;
    }

    [KernelFunction, Description("Convert radians to degrees")]
    public async Task<double> RadiansToDegreesAsync(
        [Description("Angle in radians")] double radians,
        CancellationToken cancellationToken = default)
    {
        var result = radians * 180.0 / Math.PI;
        _logger.LogInformation("RadiansToDegreesAsync: {Radians} rad = {Result}°", radians, result);
        return result;
    }
    #endregion

    #region Logarithmic and Exponential Functions
    [KernelFunction, Description("Calculate natural logarithm (base e) of a number")]
    public async Task<string> LogAsync(
        [Description("Number (must be positive)")] double number,
        CancellationToken cancellationToken = default)
    {
        if (number <= 0)
        {
            _logger.LogWarning("LogAsync: Non-positive number provided: {Number}", number);
            return "Error: Logarithm undefined for non-positive numbers";
        }

        var result = Math.Log(number);
        _logger.LogInformation("LogAsync: ln({Number}) = {Result}", number, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate logarithm base 10 of a number")]
    public async Task<string> Log10Async(
        [Description("Number (must be positive)")] double number,
        CancellationToken cancellationToken = default)
    {
        if (number <= 0)
        {
            _logger.LogWarning("Log10Async: Non-positive number provided: {Number}", number);
            return "Error: Logarithm undefined for non-positive numbers";
        }

        var result = Math.Log10(number);
        _logger.LogInformation("Log10Async: log10({Number}) = {Result}", number, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate logarithm with custom base")]
    public async Task<string> LogBaseAsync(
        [Description("Number (must be positive)")] double number,
        [Description("Base (must be positive and not equal to 1)")] double baseValue,
        CancellationToken cancellationToken = default)
    {
        if (number <= 0)
        {
            _logger.LogWarning("LogBaseAsync: Non-positive number provided: {Number}", number);
            return "Error: Logarithm undefined for non-positive numbers";
        }

        if (baseValue <= 0 || baseValue == 1)
        {
            _logger.LogWarning("LogBaseAsync: Invalid base provided: {Base}", baseValue);
            return "Error: Base must be positive and not equal to 1";
        }

        var result = Math.Log(number) / Math.Log(baseValue);
        _logger.LogInformation("LogBaseAsync: log_{Base}({Number}) = {Result}", baseValue, number, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate e raised to the power of x")]
    public async Task<double> ExpAsync(
        [Description("Exponent")] double x,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Exp(x);
        _logger.LogInformation("ExpAsync: e^{X} = {Result}", x, result);
        return result;
    }
    #endregion

    #region Additional Mathematical Functions
    [KernelFunction, Description("Calculate absolute value of a number")]
    public async Task<double> AbsAsync(
        [Description("Number")] double number,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Abs(number);
        _logger.LogInformation("AbsAsync: |{Number}| = {Result}", number, result);
        return result;
    }

    [KernelFunction, Description("Round a number to the nearest integer")]
    public async Task<double> RoundAsync(
        [Description("Number to round")] double number,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Round(number);
        _logger.LogInformation("RoundAsync: round({Number}) = {Result}", number, result);
        return result;
    }

    [KernelFunction, Description("Round a number to specified decimal places")]
    public async Task<double> RoundToDecimalPlacesAsync(
        [Description("Number to round")] double number,
        [Description("Number of decimal places")] int decimalPlaces,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Round(number, decimalPlaces);
        _logger.LogInformation("RoundToDecimalPlacesAsync: round({Number}, {DecimalPlaces}) = {Result}", number, decimalPlaces, result);
        return result;
    }

    [KernelFunction, Description("Get the largest integer less than or equal to the number (floor)")]
    public async Task<double> FloorAsync(
        [Description("Number")] double number,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Floor(number);
        _logger.LogInformation("FloorAsync: floor({Number}) = {Result}", number, result);
        return result;
    }

    [KernelFunction, Description("Get the smallest integer greater than or equal to the number (ceiling)")]
    public async Task<double> CeilingAsync(
        [Description("Number")] double number,
        CancellationToken cancellationToken = default)
    {
        var result = Math.Ceiling(number);
        _logger.LogInformation("CeilingAsync: ceiling({Number}) = {Result}", number, result);
        return result;
    }

    [KernelFunction, Description("Calculate factorial of a non-negative integer")]
    public async Task<string> FactorialAsync(
        [Description("Non-negative integer")] int n,
        CancellationToken cancellationToken = default)
    {
        if (n < 0)
        {
            _logger.LogWarning("FactorialAsync: Negative number provided: {N}", n);
            return "Error: Factorial undefined for negative numbers";
        }

        if (n > 170) // Factorial of 171 would overflow double
        {
            _logger.LogWarning("FactorialAsync: Number too large: {N}", n);
            return "Error: Number too large for factorial calculation";
        }

        double result = 1;
        for (var i = 2; i <= n; i++)
        {
            result *= i;
        }

        _logger.LogInformation("FactorialAsync: {N}! = {Result}", n, result);
        return result.ToString();
    }

    [KernelFunction, Description("Calculate the remainder when dividing two numbers")]
    public async Task<double> ModuloAsync(
        [Description("Dividend")] double dividend,
        [Description("Divisor")] double divisor,
        CancellationToken cancellationToken = default)
    {
        if (divisor == 0)
        {
            _logger.LogWarning("ModuloAsync: Division by zero attempted");
            return double.NaN;
        }

        var result = dividend % divisor;
        _logger.LogInformation("ModuloAsync: {Dividend} mod {Divisor} = {Result}", dividend, divisor, result);
        return result;
    }
    #endregion

    #region Constants
    [KernelFunction, Description("Get the value of Pi (π)")]
    public async Task<double> GetPiAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetPiAsync: π = {Pi}", Math.PI);
        return Math.PI;
    }

    [KernelFunction, Description("Get the value of Euler's number (e)")]
    public async Task<double> GetEAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetEAsync: e = {E}", Math.E);
        return Math.E;
    }
    #endregion
}
