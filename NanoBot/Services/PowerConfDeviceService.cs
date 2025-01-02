using NanoBot.Events;
using HidSharp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoBot.Configuration;
using Microsoft.Extensions.Options;

namespace NanoBot.Services;

public interface IPowerConfDeviceService : IHostedService
{
    bool SetCallState(bool state);
    bool SetMuteState(bool state);
}

public class PowerConfDeviceService : BackgroundService, IPowerConfDeviceService
{
    private const int VendorId = 0x291A;
    private const int ProductId = 0x3308;
    private const int TelephonyDeviceUsagePage = 0x0B;
    private const int ConsumerControlDeviceUsagePage = 0x0C;
    private const int VendorDefinedDeviceUsagePage = 0xFF82;

    private readonly ILogger _logger;
    private readonly IEventBus _bus;
    private readonly IOptionsMonitor<AppConfig> _appConfigOptionsMonitor;
    private bool _enabled = false;

    private HidDevice _telephonyDevice;

    public PowerConfDeviceService(
        ILogger<PowerConfDeviceService> logger,
        IOptionsMonitor<AppConfig> appConfigOptionsMonitor,
        IEventBus bus)
    {
        _logger = logger;
        _bus = bus;
        _appConfigOptionsMonitor = appConfigOptionsMonitor;
        _enabled = appConfigOptionsMonitor.CurrentValue.PowerConfS330DriverEnabled;

        _appConfigOptionsMonitor.OnChange((appConfig) => {
            _enabled = appConfig.PowerConfS330DriverEnabled;
        });

        WireUpEventHandlers();
    }

    private void WireUpEventHandlers()
    {
        _bus.Subscribe<StartListeningEvent>(e => {
            SetCallState(true);
        });
        _bus.Subscribe<StopListeningEvent>(e => {
            SetCallState(false);
        });

        _bus.Subscribe<StartTalkingEvent>(e => {
            SetCallState(true);
        });
        _bus.Subscribe<StopTalkingEvent>(e => {
            SetCallState(false);
        });

        _bus.Subscribe<ShutdownEvent>(e => {
            SetCallState(false);
            SetMuteState(false);
        });
    }

    private HidDevice FindDevice(int usagePage)
    {
        var deviceList = DeviceList.Local;
        var devices = deviceList.GetHidDevices(VendorId, ProductId);

        foreach (var device in devices)
        {
            _logger.LogDebug($"Device: {device.DevicePath}");

            var reportDescriptor = device.GetRawReportDescriptor();
            if (reportDescriptor == null)
            {
                _logger.LogDebug("Failed to retrieve report descriptor.");
                continue;
            }

            _logger.LogDebug($"Report Descriptor: {BitConverter.ToString(reportDescriptor)}");

            if (ContainsUsagePage(reportDescriptor, usagePage))
            {
                    
                _logger.LogDebug("Device usage page 0x{0:x} found.", usagePage);

                return device;
            }
        }

        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_enabled)
            {
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            try
            {
                if (_telephonyDevice == null)
                {
                    _telephonyDevice = FindDevice(TelephonyDeviceUsagePage);
                    if (_telephonyDevice == null)
                    {
                        _logger.LogWarning($"Could not find {nameof(TelephonyDeviceUsagePage)}.");

                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogDebug($"Found {nameof(TelephonyDeviceUsagePage)}.");
                }

                await using var stream = OpenDeviceStream(_telephonyDevice);

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_enabled)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    try
                    {
                        var report = await ReceiveReport(stream, cancellationToken);

                        _logger.LogDebug($"Report: {BitConverter.ToString(report)}");

                        if (report.Length >= 2)
                        {
                            if (report[0] == 0x1)
                            {
                                if (report[1] == 0x10)
                                    _bus.Publish(new VolumeCtrlDownInputEvent(this));
                                else if (report[1] == 0x8)
                                    _bus.Publish(new VolumeCtrlUpInputEvent(this));
                            }
                            else if (report[0] == 0x2)
                            {
                                if (report[1] == 0x2 || report[1] == 0x3)
                                    _bus.Publish<HangupInputEvent>(this);
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Ignore ReceiveReport timeouts
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"{nameof(ExecuteAsync)} cancellation.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                await Task.Delay(5000, cancellationToken);
            }
            finally
            {
                _telephonyDevice = null;
            }
        }
    }        

    private Stream OpenDeviceStream(HidDevice device)
    {
        if (!device.TryOpen(out var stream))
        {
            throw new Exception($"Failed to open device stream: {device.DevicePath}");
        }

        return stream;
    }        

    public async Task<byte[]> ReceiveReport(Stream stream, CancellationToken cancellationToken)
    {
        stream.ReadTimeout = 100;
        var buffer = new byte[2];

        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)                            
            return buffer;            

        return null;
    }

    public bool SetCallState(bool state) => SendControlReport(_telephonyDevice, 0x03, state);

    public bool SetMuteState(bool state) => SendControlReport(_telephonyDevice, 0x04, state);

    private bool SendControlReport(HidDevice device, byte reportId, bool state)
    {
        if (device == null)
        {
            _logger.LogWarning("Device not connected.");
            return false;
        }

        try
        {
            using var stream = OpenDeviceStream(device);
            var report = new byte[] { reportId, state ? (byte)0x01 : (byte)0x00 };
            stream.Write(report);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send control report: {reportId}");
            return false;
        }
    }

    private bool ContainsUsagePage(byte[] reportDescriptor, int usagePage)
    {
        // Check for 1-byte usage pages (Usage Page tag: 0x05)
        if (usagePage <= 0xFF)
        {
            byte usagePageTag = 0x05;
            byte usagePageByte = (byte)usagePage;

            for (int i = 0; i < reportDescriptor.Length - 1; i++)
            {
                if (reportDescriptor[i] == usagePageTag && reportDescriptor[i + 1] == usagePageByte)
                {
                    return true;
                }
            }
        }
        else
        {
            // Check for 2-byte vendor-defined usage pages (Usage Page tag: 0x06)
            byte usagePageTag = 0x06;
            byte usagePageLowByte = (byte)(usagePage & 0xFF);        // Low byte of the usage page
            byte usagePageHighByte = (byte)((usagePage >> 8) & 0xFF); // High byte of the usage page

            for (int i = 0; i < reportDescriptor.Length - 2; i++)
            {
                if (reportDescriptor[i] == usagePageTag &&
                    reportDescriptor[i + 1] == usagePageLowByte &&
                    reportDescriptor[i + 2] == usagePageHighByte)
                {
                    return true;
                }
            }
        }

        return false;
    }
}