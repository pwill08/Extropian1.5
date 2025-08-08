using Extropian.Classes;
using Plugin.CloudFirestore;
using Shiny;
using Shiny.BluetoothLE;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading;

namespace Extropian.Pages;

public class SensorSample
{
    public uint Timestamp { get; set; }
    public float AccelX { get; set; }
    public float AccelY { get; set; }
    public float AccelZ { get; set; }
    public float GyroX { get; set; }
    public float GyroY { get; set; }
    public float GyroZ { get; set; }
    public float MagX { get; set; }
    public float MagY { get; set; }
    public float MagZ { get; set; }
}

public partial class Connect : ContentPage
{
    private readonly IBleManager _bleManager;
    private ObservableCollection<IPeripheral> _discoveredDevices;
    private readonly ConcurrentDictionary<string, IPeripheral> _connectedDevices = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<byte[]>> _receivedPackets = new();
    private readonly ConcurrentDictionary<string, List<SensorSample>> _deviceSamples = new();
    private readonly string _serviceUuid = "12345678-1234-5678-1234-56789abcdef0";
    private readonly string _commandCharUuid = "12345678-1234-5678-1234-56789abcdef1";
    private readonly string _dataCharUuid = "12345678-1234-5678-1234-56789abcdef2";
    private readonly string _thresholdCharUuid = "12345678-1234-5678-1234-56789abcdef3";
    private const int EXPECTED_PACKET_COUNT = 21;
    private const string DEVICE_NAME = "Extropian";

    private IDisposable _scanSubscription;
    private IDisposable _currentScan;
    private IPeripheral _selectedDevice = null;
    private string _selectedPosition = null;
    private IDocumentReference _activeSessionDoc;

    // Fixed: Use device UUIDs as keys to match device mapping
    private readonly ConcurrentDictionary<string, string> _deviceToLogKeyMap = new();
    private string _device1Id;
    private string _device2Id;
    private string _device3Id;
    private string _device4Id;

    public Connect()
    {
        InitializeComponent();

        _bleManager = Application.Current.Handler.MauiContext.Services.GetRequiredService<IBleManager>();

        foreach (var child in PositionsButtonStack.Children)
        {
            if (child is Button positionButton)
                positionButton.Clicked += OnPositionSelected;
        }

        // Initialize data structures for up to 4 devices
        for (int i = 1; i <= 4; i++)
        {
            _receivedPackets[$"Device{i}"] = new ConcurrentBag<byte[]>();
            _deviceSamples[$"Device{i}"] = new List<SensorSample>();
        }

        // Initialize Firestore and disable offline persistence
        try
        {
            var firestore = CrossCloudFirestore.Current.Instance;
            Debug.WriteLine("Firestore initialized with offline persistence disabled.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing Firestore: {ex.Message}");
        }
    }

    private async Task<bool> CheckPermissionsAsync()
    {
        try
        {
            var status = await _bleManager.RequestAccessAsync();
            if (status != AccessState.Available)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Error", "Bluetooth permissions denied. Please enable Bluetooth and grant permissions.", "OK"));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await DisplayAlert("Error", $"Failed to check permissions: {ex.Message}", "OK"));
            return false;
        }
    }

    private void OnDeviceStatusChanged(object sender, BleDeviceStatusChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.Device == Devices.DeviceRightWristPeripheral)
            {
                Devices.IsDeviceRightWristConnected = e.Status == ConnectionState.Connected;
                RWristMAC.Text = e.Status == ConnectionState.Connected ? "Connected" : "Disconnected";
            }

            if (e.Device == Devices.DeviceLeftWristPeripheral)
            {
                Devices.IsDeviceLeftWristConnected = e.Status == ConnectionState.Connected;
                LWristMAC.Text = e.Status == ConnectionState.Connected ? "Connected" : "Disconnected";
            }

            if (e.Device == Devices.DeviceHipPeripheral)
            {
                Devices.IsDeviceHipConnected = e.Status == ConnectionState.Connected;
                HipMAC.Text = e.Status == ConnectionState.Connected ? "Connected" : "Disconnected";
            }

            if (e.Device == Devices.DeviceTorsoPeripheral)
            {
                Devices.IsDeviceTorsoConnected = e.Status == ConnectionState.Connected;
                TorsoMAC.Text = e.Status == ConnectionState.Connected ? "Connected" : "Disconnected";
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DevicesButtonStack.Clear();

        BleManagerService.Instance.MonitorDevice(Devices.DeviceRightWristPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceLeftWristPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceHipPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceTorsoPeripheral);

        RWristConnection.Text = (Devices.DeviceRightWristPeripheral != null && Devices.IsDeviceRightWristConnected) ? "Connected" : "Disconnected";
        LWristConnection.Text = (Devices.DeviceLeftWristPeripheral != null && Devices.IsDeviceLeftWristConnected) ? "Connected" : "Disconnected";
        HipConnection.Text = (Devices.DeviceHipPeripheral != null && Devices.IsDeviceHipConnected) ? "Connected" : "Disconnected";
        TorsoConnection.Text = (Devices.DeviceTorsoPeripheral != null && Devices.IsDeviceTorsoConnected) ? "Connected" : "Disconnected";
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await StopActiveScan();
        foreach (var device in _connectedDevices.Values)
        {
            try
            {
                var dataChar = await device.GetCharacteristicAsync(_serviceUuid, _dataCharUuid, CancellationToken.None);
                var thresholdChar = await device.GetCharacteristicAsync(_serviceUuid, _thresholdCharUuid, CancellationToken.None);
                await device.NotifyCharacteristic(dataChar, false);
                await device.NotifyCharacteristic(thresholdChar, false);
                device.CancelConnection();
            }
            catch { }
        }
        _connectedDevices.Clear();
        _deviceToLogKeyMap.Clear();
        _activeSessionDoc = null;
    }

    private async Task StopActiveScan()
    {
        try
        {
            if (_bleManager.IsScanning)
            {
                _bleManager.StopScan();
                _scanSubscription?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping scan: {ex.Message}");
            ScanStatusLabel.Text = $"Error stopping scan: {ex.Message}";
        }
    }

    private async void ScanForAllDevicesAsync(object sender, EventArgs e)
    {
        if (!await CheckPermissionsAsync())
        {
            ScanStatusLabel.Text = "Permissions denied. Cannot scan.";
            return;
        }

        DevicesButtonStack.Children.Clear();
        var seenDevices = new HashSet<string>();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            ScanStatusLabel.Text = "Scanning for devices...";

            await _bleManager
                .Scan()
                .Where(result => result.Peripheral.Name?.Contains("Extropian") == true)
                .ForEachAsync(async result =>
                {
                    var macAddress = result.Peripheral.Uuid.ToString();
                    var lastSegment = macAddress.Split('-').Last();

                    if (!seenDevices.Contains(lastSegment))
                    {
                        seenDevices.Add(lastSegment);

                        var deviceButton = new Button
                        {
                            Text = $"{result.Peripheral.Name} ({lastSegment})",
                            AutomationId = macAddress,
                            WidthRequest = 150,
                            BackgroundColor = Colors.Blue
                        };

                        deviceButton.Clicked += (s, args) =>
                        {
                            _selectedDevice = result.Peripheral;
                            ScanStatusLabel.Text = $"Selected device: {_selectedDevice.Name}";
                        };

                        DevicesButtonStack.Children.Add(deviceButton);
                    }
                }, cts.Token);

            ScanStatusLabel.Text = "Scan complete. Select a device, then a position.";
        }
        catch (OperationCanceledException)
        {
            ScanStatusLabel.Text = "Scan complete.";
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error scanning: {ex.Message}";
            Debug.WriteLine($"Error scanning: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async void OnPositionSelected(object sender, EventArgs e)
    {
        if (_selectedDevice == null)
        {
            ScanStatusLabel.Text = "Please select a device first.";
            return;
        }

        var button = sender as Button;
        if (button == null) return;

        _selectedPosition = button.Text;

        bool success = await AssignAndConnectDeviceToPosition(_selectedDevice, _selectedPosition);

        if (success)
        {
            ScanStatusLabel.Text = $"Device {_selectedDevice.Name} assigned to {_selectedPosition} and connected.";
            _selectedDevice = null;
            _selectedPosition = null;
        }
        else
        {
            ScanStatusLabel.Text = $"Failed to connect device {_selectedDevice.Name} to {_selectedPosition}.";
        }
    }

    private async Task<bool> AssignAndConnectDeviceToPosition(IPeripheral device, string position)
    {
        try
        {
            string logKey;
            string deviceId = device.Uuid.ToString();

            switch (position)
            {
                case "Right Wrist":
                    Devices.DeviceRightWristPeripheral = device;
                    Devices.IsDeviceRightWristConnected = false;
                    RWristMAC.Text = deviceId;
                    RWristConnection.Text = "Connecting...";
                    logKey = "Device1";
                    _device1Id = deviceId;
                    break;

                case "Left Wrist":
                    Devices.DeviceLeftWristPeripheral = device;
                    Devices.IsDeviceLeftWristConnected = false;
                    LWristMAC.Text = deviceId;
                    LWristConnection.Text = "Connecting...";
                    logKey = "Device2";
                    _device2Id = deviceId;
                    break;

                case "Hip Wrist":
                    Devices.DeviceHipPeripheral = device;
                    Devices.IsDeviceHipConnected = false;
                    HipMAC.Text = deviceId;
                    HipConnection.Text = "Connecting...";
                    logKey = "Device3";
                    _device3Id = deviceId;
                    break;

                case "Torso Wrist":
                    Devices.DeviceTorsoPeripheral = device;
                    Devices.IsDeviceTorsoConnected = false;
                    TorsoMAC.Text = deviceId;
                    TorsoConnection.Text = "Connecting...";
                    logKey = "Device4";
                    _device4Id = deviceId;
                    break;

                default:
                    return false;
            }

            // Fixed: Store device-to-logKey mapping
            _deviceToLogKeyMap[deviceId] = logKey;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await device.ConnectAsync(new ConnectionConfig { AutoConnect = true }, cts.Token);
                    await device.TryRequestMtuAsync(247);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == 3)
                    {
                        ScanStatusLabel.Text = $"Connection failed after 3 attempts: {ex.Message}";
                        Debug.WriteLine($"Connection failed after 3 attempts: {ex.Message}");
                        return false;
                    }
                    await Task.Delay(1000);
                }
            }

            _connectedDevices.TryAdd(deviceId, device);

            await SetupNotifications(device, logKey);
            await StopImu(device);

            switch (position)
            {
                case "Right Wrist":
                    Devices.IsDeviceRightWristConnected = true;
                    RWristConnection.Text = "Connected";
                    break;

                case "Left Wrist":
                    Devices.IsDeviceLeftWristConnected = true;
                    LWristConnection.Text = "Connected";
                    break;

                case "Hip Wrist":
                    Devices.IsDeviceHipConnected = true;
                    HipConnection.Text = "Connected";
                    break;

                case "Torso Wrist":
                    Devices.IsDeviceTorsoConnected = true;
                    TorsoConnection.Text = "Connected";
                    break;
            }

            await CheckAndStartSession();

            return true;
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error connecting device: {ex.Message}";
            Debug.WriteLine($"Error connecting device: {ex.Message}");
            return false;
        }
    }

    private async Task ResetClocks(IPeripheral device)
    {
        try
        {
            var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
            await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x02 }, false);
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error resetting clock: {ex.Message}";
            Debug.WriteLine($"Error resetting clock: {ex.Message}");
        }
    }

    private async Task StopImu(IPeripheral device)
    {
        try
        {
            var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
            await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x00 }, false);
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error stopping IMU: {ex.Message}";
            Debug.WriteLine($"Error stopping IMU: {ex.Message}");
        }
    }

    private async Task SetupNotifications(IPeripheral device, string logKey)
    {
        try
        {
            var dataChar = await device.GetCharacteristicAsync(_serviceUuid, _dataCharUuid, CancellationToken.None);
            var thresholdChar = await device.GetCharacteristicAsync(_serviceUuid, _thresholdCharUuid, CancellationToken.None);

            if ((dataChar.Properties & CharacteristicProperties.Indicate) == 0)
                throw new Exception("Data characteristic does not support indicate");
            if ((thresholdChar.Properties & CharacteristicProperties.Notify) == 0)
                throw new Exception("Threshold characteristic does not support notify");

            device.NotifyCharacteristic(dataChar, true).Subscribe(e =>
            {
                StorePacket(device.Uuid.ToString(), e.Data, logKey);
                Debug.WriteLine($"Received packet from {device.Uuid}: {e.Data.Length} bytes");
            });
            device.NotifyCharacteristic(thresholdChar, true).Subscribe(e => HandleThreshold(device.Uuid.ToString(), e.Data));
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error setting up notifications: {ex.Message}";
            Debug.WriteLine($"Error setting up notifications: {ex.Message}");
        }
    }

    private async void OnStartImuClicked(object sender, EventArgs e)
    {
        if (_connectedDevices.Count < 2)
        {
            await DisplayAlert("Error", "Need at least two connected devices.", "OK");
            return;
        }

        // Fixed: Create session only if it doesn't exist
        if (_activeSessionDoc == null)
        {
            // Force session creation with retries
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                await CheckAndStartSession();
                if (_activeSessionDoc != null)
                    break;
                Debug.WriteLine($"Session creation attempt {attempt} failed. Retrying...");
                await Task.Delay(1000);
            }

            if (_activeSessionDoc == null)
            {
                ScanStatusLabel.Text = "Error: Failed to create Firestore session after retries.";
                Debug.WriteLine("Failed to create Firestore session after 3 attempts.");
                return;
            }
        }

        // Process any queued packets
        await ProcessQueuedPackets();

        await StartImu();
    }

    private async Task StartImu()
    {
        foreach (var device in _connectedDevices.Values)
        {
            try
            {
                await ResetClocks(device);
                //await Task.Delay(20);
                var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
                await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x01 }, false);
            }
            catch (Exception ex)
            {
                ScanStatusLabel.Text = $"Error starting IMU: {ex.Message}";
                Debug.WriteLine($"Error starting IMU: {ex.Message}");
            }
        }
    }

    private void StorePacket(string deviceId, byte[] data, string logKey)
    {
        _receivedPackets[logKey].Add(data);
        Debug.WriteLine($"Stored packet for {logKey}: {data.Length} bytes (Total: {_receivedPackets[logKey].Count})");

        // Fixed: Only upload to Firestore if we have an active session
        if (_activeSessionDoc != null)
        {
            // Use async method but don't await to avoid blocking the notification callback
            Task.Run(() => UploadSamplesToFirestoreAsync(deviceId, data, logKey));
        }
        else
        {
            Debug.WriteLine($"No active session - packet stored for later processing");
        }

        if (_receivedPackets[logKey].Count >= EXPECTED_PACKET_COUNT)
        {
            Task.Run(() => ParsePackets(logKey));
        }
    }

    private void ParsePackets(string logKey)
    {
        var samples = _deviceSamples[logKey];
        samples.Clear();

        foreach (var data in _receivedPackets[logKey])
        {
            if (data.Length != 221)
            {
                Debug.WriteLine($"Invalid packet length: {data.Length} bytes");
                continue;
            }

            int offset = 0;
            byte packetNum = data[offset++];

            for (int s = 0; s < 5; s++)
            {
                if (data[offset++] != (byte)'A')
                {
                    Debug.WriteLine("Invalid packet format: Missing 'A' marker");
                    return;
                }

                uint ts = BitConverter.ToUInt32(data, offset);
                offset += 4;

                if (data[offset++] != (byte)'B')
                {
                    Debug.WriteLine("Invalid packet format: Missing 'B' marker");
                    return;
                }

                float ax = BitConverter.ToSingle(data, offset);
                float ay = BitConverter.ToSingle(data, offset + 4);
                float az = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                if (data[offset++] != (byte)'C')
                {
                    Debug.WriteLine("Invalid packet format: Missing 'C' marker");
                    return;
                }

                float gx = BitConverter.ToSingle(data, offset);
                float gy = BitConverter.ToSingle(data, offset + 4);
                float gz = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                if (data[offset++] != (byte)'D')
                {
                    Debug.WriteLine("Invalid packet format: Missing 'D' marker");
                    return;
                }

                float mx = BitConverter.ToSingle(data, offset);
                float my = BitConverter.ToSingle(data, offset + 4);
                float mz = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                samples.Add(new SensorSample
                {
                    Timestamp = ts,
                    AccelX = ax,
                    AccelY = ay,
                    AccelZ = az,
                    GyroX = gx,
                    GyroY = gy,
                    GyroZ = gz,
                    MagX = mx,
                    MagY = my,
                    MagZ = mz
                });
            }
        }
        _receivedPackets[logKey] = new ConcurrentBag<byte[]>();
    }

    private async void HandleThreshold(string deviceId, byte[] data)
    {
        if (data.Length > 0 && data[0] == 0x05)
        {
            await Task.Delay(1000);
            await TriggerFreezeOnAll(deviceId);
        }
    }

    private async Task TriggerFreezeOnAll(string triggeredDeviceId)
    {
        await Task.WhenAll(_connectedDevices.Values.Select(async device =>
        {
            try
            {
                var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
                await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x03 }, false);
            }
            catch (Exception ex)
            {
                ScanStatusLabel.Text = $"Error triggering freeze: {ex.Message}";
                Debug.WriteLine($"Error triggering freeze: {ex.Message}");
            }
        }));

        await Task.Delay(50);

        // Fixed: Use the mapping to get correct logKey for each device
        var devicesWithKeys = _connectedDevices.Keys.Select(deviceId =>
        {
            var logKey = _deviceToLogKeyMap.GetValueOrDefault(deviceId, "Unknown");
            return (deviceId, logKey);
        }).Where(d => d.logKey != "Unknown").ToList();

        await Task.WhenAll(devicesWithKeys.Select(d => SendQueueFromDevice(d.deviceId, d.logKey)));
    }

    private async Task SendQueueFromDevice(string deviceId, string logKey)
    {
        var device = _connectedDevices[deviceId];
        try
        {
            var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
            await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x04 }, false);

            for (int i = 0; i < 10; i++)
            {
                if (_receivedPackets[logKey].Count >= EXPECTED_PACKET_COUNT)
                    break;
                await Task.Delay(10);
            }
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error sending queue: {ex.Message}";
            Debug.WriteLine($"Error sending queue: {ex.Message}");
        }
    }

    private async Task StartNewFirestoreSession(string deviceId1, string deviceId2, string deviceId3 = null, string deviceId4 = null)
    {
        try
        {
            var firestore = CrossCloudFirestore.Current.Instance;

            var sessionData = new Dictionary<string, object>
            {
                { "device1Id", deviceId1 },
                { "device2Id", deviceId2 },
                { "device3Id", deviceId3 ?? string.Empty },
                { "device4Id", deviceId4 ?? string.Empty },
                { "startTime", DateTime.UtcNow },
                { "device1Samples", new List<object>() },
                { "device2Samples", new List<object>() }
            };

            if (deviceId3 != null)
                sessionData["device3Samples"] = new List<object>();
            if (deviceId4 != null)
                sessionData["device4Samples"] = new List<object>();

            var sessionDoc = await firestore
                .Collection("imu_sessions")
                .AddAsync(sessionData);

            _activeSessionDoc = sessionDoc;
            Debug.WriteLine($"Firestore session created with ID: {sessionDoc.Id}");
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error starting Firestore session: {ex.Message}";
            Debug.WriteLine($"Error starting Firestore session: {ex.Message} (StackTrace: {ex.StackTrace})");
        }
    }

    private async Task ProcessQueuedPackets()
    {
        if (_activeSessionDoc == null)
        {
            Debug.WriteLine("No active session for processing queued packets.");
            return;
        }

        Debug.WriteLine($"Processing queued packets for {_receivedPackets.Count} devices");

        foreach (var kvp in _receivedPackets)
        {
            var logKey = kvp.Key;
            var packets = kvp.Value;

            var deviceId = logKey switch
            {
                "Device1" => _device1Id,
                "Device2" => _device2Id,
                "Device3" => _device3Id,
                "Device4" => _device4Id,
                _ => null
            };

            if (deviceId == null)
                continue;

            Debug.WriteLine($"Processing {packets.Count} queued packets for {logKey} (device: {deviceId})");

            while (packets.TryTake(out var packet))
            {
                Debug.WriteLine($"Processing queued packet for {logKey}: {packet.Length} bytes");
                await UploadSamplesToFirestoreAsync(deviceId, packet, logKey);
            }
        }
    }

    private async Task UploadSamplesToFirestoreAsync(string deviceId, byte[] data, string logKey)
    {
        if (_activeSessionDoc == null)
        {
            Debug.WriteLine($"No active Firestore session for {logKey}. Skipping upload.");
            return;
        }

        if (data.Length != 221)
        {
            Debug.WriteLine($"Invalid packet length for {logKey}: {data.Length} bytes");
            return;
        }

        try
        {
            var samples = new List<object>();
            int offset = 0;
            byte packetNum = data[offset++];

            Debug.WriteLine($"Parsing packet {packetNum} for {logKey}");

            for (int s = 0; s < 5; s++)
            {
                if (offset >= data.Length || data[offset++] != (byte)'A')
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'A' marker at sample {s}");
                    return;
                }

                if (offset + 4 > data.Length)
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Insufficient data for timestamp at sample {s}");
                    return;
                }
                uint ts = BitConverter.ToUInt32(data, offset);
                offset += 4;

                if (offset >= data.Length || data[offset++] != (byte)'B')
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'B' marker at sample {s}");
                    return;
                }

                if (offset + 12 > data.Length)
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Insufficient data for accel at sample {s}");
                    return;
                }
                float ax = BitConverter.ToSingle(data, offset);
                float ay = BitConverter.ToSingle(data, offset + 4);
                float az = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                if (offset >= data.Length || data[offset++] != (byte)'C')
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'C' marker at sample {s}");
                    return;
                }

                if (offset + 12 > data.Length)
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Insufficient data for gyro at sample {s}");
                    return;
                }
                float gx = BitConverter.ToSingle(data, offset);
                float gy = BitConverter.ToSingle(data, offset + 4);
                float gz = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                if (offset >= data.Length || data[offset++] != (byte)'D')
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'D' marker at sample {s}");
                    return;
                }

                if (offset + 12 > data.Length)
                {
                    Debug.WriteLine($"Invalid packet format for {logKey}: Insufficient data for mag at sample {s}");
                    return;
                }
                float mx = BitConverter.ToSingle(data, offset);
                float my = BitConverter.ToSingle(data, offset + 4);
                float mz = BitConverter.ToSingle(data, offset + 8);
                offset += 12;

                // Validate sample data
                if (float.IsNaN(ax) || float.IsNaN(ay) || float.IsNaN(az) ||
                    float.IsNaN(gx) || float.IsNaN(gy) || float.IsNaN(gz) ||
                    float.IsNaN(mx) || float.IsNaN(my) || float.IsNaN(mz))
                {
                    Debug.WriteLine($"Invalid sample data for {logKey} at sample {s}: Contains NaN values");
                    return;
                }

                samples.Add(new
                {
                    timestamp = ts,
                    accel = new { x = ax, y = ay, z = az },
                    gyro = new { x = gx, y = gy, z = gz },
                    mag = new { x = mx, y = my, z = mz }
                });
            }

            if (samples.Count != 5)
            {
                Debug.WriteLine($"Invalid sample count for {logKey}: Expected 5, got {samples.Count}");
                return;
            }

            string fieldName = deviceId == _device1Id ? "device1Samples" :
                               deviceId == _device2Id ? "device2Samples" :
                               deviceId == _device3Id ? "device3Samples" :
                               deviceId == _device4Id ? "device4Samples" : null;
            if (fieldName == null)
            {
                Debug.WriteLine($"Unknown device ID for {logKey}: {deviceId}");
                return;
            }

            Debug.WriteLine($"Preparing to upload {samples.Count} samples to Firestore for {fieldName}");
            await _activeSessionDoc.UpdateAsync(fieldName, FieldValue.ArrayUnion(samples.ToArray()));
            Debug.WriteLine($"Successfully uploaded {samples.Count} samples to Firestore for {fieldName}");
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Error uploading to Firestore for {logKey}: {ex.Message}";
            Debug.WriteLine($"Error uploading to Firestore for {logKey}: {ex.Message} (StackTrace: {ex.StackTrace})");
        }
    }

    private async Task CheckAndStartSession()
    {
        if (_connectedDevices.Count >= 2 && _activeSessionDoc == null)
        {
            var deviceIds = _connectedDevices.Keys.ToList();

            // Fixed: Use the stored device IDs instead of assuming order
            if (_device1Id == null && deviceIds.Count > 0) _device1Id = deviceIds[0];
            if (_device2Id == null && deviceIds.Count > 1) _device2Id = deviceIds[1];
            if (_device3Id == null && deviceIds.Count > 2) _device3Id = deviceIds[2];
            if (_device4Id == null && deviceIds.Count > 3) _device4Id = deviceIds[3];

            await StartNewFirestoreSession(_device1Id, _device2Id, _device3Id, _device4Id);
        }
    }

    //// Fixed: Remove the duplicate synchronous method
    //private async void UploadSamplesToFirestore(string deviceId, byte[] data, string logKey)
    //{
    //    // This method is now just a wrapper for the async version
    //    await UploadSamplesToFirestoreAsync(deviceId, data, logKey);
    //}



    ////private void StorePacket(string deviceId, byte[] data, string logKey)
    ////{
    ////    _receivedPackets[logKey].Add(data);
    ////    Debug.WriteLine($"Stored packet for {logKey}: {data.Length} bytes (Total: {_receivedPackets[logKey].Count})");

    ////    if (_receivedPackets[logKey].Count >= EXPECTED_PACKET_COUNT)
    ////    {
    ////        Task.Run(() => ParseAndUploadPackets(logKey));
    ////    }
    ////}

    //private async Task ParseAndUploadPackets(string logKey)
    //{
    //    var samples = _deviceSamples[logKey];
    //    samples.Clear();

    //    foreach (var data in _receivedPackets[logKey])
    //    {
    //        if (data.Length != 221)
    //        {
    //            Debug.WriteLine($"Invalid packet length for {logKey}: {data.Length} bytes");
    //            continue;
    //        }

    //        int offset = 0;
    //        byte packetNum = data[offset++];

    //        for (int s = 0; s < 5; s++)
    //        {
    //            if (data[offset++] != (byte)'A')
    //            {
    //                Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'A' marker");
    //                continue;
    //            }

    //            uint ts = BitConverter.ToUInt32(data, offset);
    //            offset += 4;

    //            if (data[offset++] != (byte)'B')
    //            {
    //                Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'B' marker");
    //                continue;
    //            }

    //            float ax = BitConverter.ToSingle(data, offset);
    //            float ay = BitConverter.ToSingle(data, offset + 4);
    //            float az = BitConverter.ToSingle(data, offset + 8);
    //            offset += 12;

    //            if (data[offset++] != (byte)'C')
    //            {
    //                Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'C' marker");
    //                continue;
    //            }

    //            float gx = BitConverter.ToSingle(data, offset);
    //            float gy = BitConverter.ToSingle(data, offset + 4);
    //            float gz = BitConverter.ToSingle(data, offset + 8);
    //            offset += 12;

    //            if (data[offset++] != (byte)'D')
    //            {
    //                Debug.WriteLine($"Invalid packet format for {logKey}: Missing 'D' marker");
    //                continue;
    //            }

    //            float mx = BitConverter.ToSingle(data, offset);
    //            float my = BitConverter.ToSingle(data, offset + 4);
    //            float mz = BitConverter.ToSingle(data, offset + 8);
    //            offset += 12;

    //            if (float.IsNaN(ax) || float.IsNaN(ay) || float.IsNaN(az) ||
    //                float.IsNaN(gx) || float.IsNaN(gy) || float.IsNaN(gz) ||
    //                float.IsNaN(mx) || float.IsNaN(my) || float.IsNaN(mz))
    //            {
    //                Debug.WriteLine($"Invalid sample data for {logKey} at sample {s}: Contains NaN values");
    //                continue;
    //            }

    //            samples.Add(new SensorSample
    //            {
    //                Timestamp = ts,
    //                AccelX = ax,
    //                AccelY = ay,
    //                AccelZ = az,
    //                GyroX = gx,
    //                GyroY = gy,
    //                GyroZ = gz,
    //                MagX = mx,
    //                MagY = my,
    //                MagZ = mz
    //            });
    //        }
    //    }

    //    _receivedPackets[logKey] = new ConcurrentBag<byte[]>();

    //    if (_activeSessionDoc != null && samples.Count > 0)
    //    {
    //        var deviceId = logKey switch
    //        {
    //            "Device1" => _device1Id,
    //            "Device2" => _device2Id,
    //            "Device3" => _device3Id,
    //            "Device4" => _device4Id,
    //            _ => null
    //        };

    //        if (deviceId == null)
    //        {
    //            Debug.WriteLine($"Unknown device ID for {logKey}");
    //            return;
    //        }

    //        try
    //        {
    //            var firestoreSamples = samples.Select(s => new
    //            {
    //                timestamp = s.Timestamp,
    //                accel = new { x = s.AccelX, y = s.AccelY, z = s.AccelZ },
    //                gyro = new { x = s.GyroX, y = s.GyroY, z = s.GyroZ },
    //                mag = new { x = s.MagX, y = s.MagY, z = s.MagZ }
    //            }).ToList();

    //            string fieldName = deviceId == _device1Id ? "device1Samples" :
    //                               deviceId == _device2Id ? "device2Samples" :
    //                               deviceId == _device3Id ? "device3Samples" :
    //                               deviceId == _device4Id ? "device4Samples" : null;

    //            if (fieldName == null)
    //            {
    //                Debug.WriteLine($"Unknown device ID for {logKey}: {deviceId}");
    //                return;
    //            }

    //            Debug.WriteLine($"Uploading {firestoreSamples.Count} samples to Firestore for {fieldName}");
    //            await _activeSessionDoc.UpdateAsync(fieldName, FieldValue.ArrayUnion(firestoreSamples.ToArray()));
    //            Debug.WriteLine($"Successfully uploaded {firestoreSamples.Count} samples to Firestore for {fieldName}");
    //        }
    //        catch (Exception ex)
    //        {
    //            ScanStatusLabel.Text = $"Error uploading to Firestore for {logKey}: {ex.Message}";
    //            Debug.WriteLine($"Error uploading to Firestore for {logKey}: {ex.Message} (StackTrace: {ex.StackTrace})");
    //        }
    //    }
    //    else
    //    {
    //        Debug.WriteLine($"No active session or no valid samples for {logKey}. Skipping upload.");
    //    }
    //}
}

