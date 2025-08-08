using Extropian.Classes;
using Plugin.CloudFirestore;
using Shiny;
using Shiny.BluetoothLE;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly ConcurrentDictionary<string, string> _deviceToLogKeyMap = new();

    public Connect()
    {
        InitializeComponent();

        _bleManager = Application.Current.Handler.MauiContext.Services.GetRequiredService<IBleManager>();

        foreach (var child in PositionsButtonStack.Children)
        {
            if (child is Button positionButton)
                positionButton.Clicked += OnPositionSelected;
        }

        // Initialize Firestore
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

    private void UpdateUI(Action action)
    {
        MainThread.BeginInvokeOnMainThread(action);
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
        UpdateUI(() =>
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
        UpdateUI(() => DevicesButtonStack.Clear());

        BleManagerService.Instance.MonitorDevice(Devices.DeviceRightWristPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceLeftWristPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceHipPeripheral);
        BleManagerService.Instance.MonitorDevice(Devices.DeviceTorsoPeripheral);

        UpdateUI(() =>
        {
            RWristConnection.Text = (Devices.DeviceRightWristPeripheral != null && Devices.IsDeviceRightWristConnected) ? "Connected" : "Disconnected";
            LWristConnection.Text = (Devices.DeviceLeftWristPeripheral != null && Devices.IsDeviceLeftWristConnected) ? "Connected" : "Disconnected";
            HipConnection.Text = (Devices.DeviceHipPeripheral != null && Devices.IsDeviceHipConnected) ? "Connected" : "Disconnected";
            TorsoConnection.Text = (Devices.DeviceTorsoPeripheral != null && Devices.IsDeviceTorsoConnected) ? "Connected" : "Disconnected";
        });
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
        _receivedPackets.Clear();
        _deviceSamples.Clear();
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
            UpdateUI(() => ScanStatusLabel.Text = $"Error stopping scan: {ex.Message}");
        }
    }

    private async void ScanForAllDevicesAsync(object sender, EventArgs e)
    {
        if (!await CheckPermissionsAsync())
        {
            UpdateUI(() => ScanStatusLabel.Text = "Permissions denied. Cannot scan.");
            return;
        }

        UpdateUI(() => DevicesButtonStack.Children.Clear());
        var seenDevices = new HashSet<string>();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            UpdateUI(() => ScanStatusLabel.Text = "Scanning for devices...");

            await _bleManager
                .Scan()
                .Where(result => result.Peripheral.Name?.Contains(DEVICE_NAME) == true)
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
                            UpdateUI(() => ScanStatusLabel.Text = $"Selected device: {_selectedDevice.Name}");
                        };

                        UpdateUI(() => DevicesButtonStack.Children.Add(deviceButton));
                    }
                }, cts.Token);

            UpdateUI(() => ScanStatusLabel.Text = "Scan complete. Select a device, then a position.");
        }
        catch (OperationCanceledException)
        {
            UpdateUI(() => ScanStatusLabel.Text = "Scan complete.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error scanning: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error scanning: {ex.Message}");
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
            UpdateUI(() => ScanStatusLabel.Text = "Please select a device first.");
            return;
        }

        var button = sender as Button;
        if (button == null) return;

        _selectedPosition = button.Text;

        bool success = await AssignAndConnectDeviceToPosition(_selectedDevice, _selectedPosition);

        UpdateUI(() =>
        {
            ScanStatusLabel.Text = success
                ? $"Device {_selectedDevice.Name} assigned to {_selectedPosition} and connected."
                : $"Failed to connect device {_selectedDevice.Name} to {_selectedPosition}.";
        });

        if (success)
        {
            _selectedDevice = null;
            _selectedPosition = null;
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
                    UpdateUI(() =>
                    {
                        RWristMAC.Text = deviceId;
                        RWristConnection.Text = "Connecting...";
                    });
                    logKey = "DevRWristID";
                    Devices.DeviceRightWristUUID = deviceId;
                    break;

                case "Left Wrist":
                    Devices.DeviceLeftWristPeripheral = device;
                    Devices.IsDeviceLeftWristConnected = false;
                    UpdateUI(() =>
                    {
                        LWristMAC.Text = deviceId;
                        LWristConnection.Text = "Connecting...";
                    });
                    logKey = "DevLWristID";
                    Devices.DeviceLeftWristUUID = deviceId;
                    break;

                case "Hip Wrist":
                    Devices.DeviceHipPeripheral = device;
                    Devices.IsDeviceHipConnected = false;
                    UpdateUI(() =>
                    {
                        HipMAC.Text = deviceId;
                        HipConnection.Text = "Connecting...";
                    });
                    logKey = "DevHipID";
                    Devices.DeviceHipUUID = deviceId;
                    break;

                case "Torso Wrist":
                    Devices.DeviceTorsoPeripheral = device;
                    Devices.IsDeviceTorsoConnected = false;
                    UpdateUI(() =>
                    {
                        TorsoMAC.Text = deviceId;
                        TorsoConnection.Text = "Connecting...";
                    });
                    logKey = "DevTorsoID";
                    Devices.DeviceTorsoUUID = deviceId;
                    break;

                default:
                    return false;
            }

            _deviceToLogKeyMap[deviceId] = logKey;
            _receivedPackets.TryAdd(logKey, new ConcurrentBag<byte[]>());
            _deviceSamples.TryAdd(logKey, new List<SensorSample>());

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
                        Debug.WriteLine($"Connection failed after 3 attempts: {ex.Message}");
                        UpdateUI(() => ScanStatusLabel.Text = $"Connection failed after 3 attempts: {ex.Message}");
                        return false;
                    }
                    await Task.Delay(1000);
                }
            }

            _connectedDevices.TryAdd(deviceId, device);

            await SetupNotifications(device, logKey);
            await StopImu(device);

            UpdateUI(() =>
            {
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
            });

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error connecting device: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error connecting device: {ex.Message}");
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
            Debug.WriteLine($"Error resetting clock: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error resetting clock: {ex.Message}");
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
            Debug.WriteLine($"Error stopping IMU: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error stopping IMU: {ex.Message}");
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
            Debug.WriteLine($"Error setting up notifications: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error setting up notifications: {ex.Message}");
        }
    }

    private async void OnStartImuClicked(object sender, EventArgs e)
    {
        if (_connectedDevices.Count < 2)
        {
            await DisplayAlert("Error", "Need at least two connected devices.", "OK");
            return;
        }

        await StartImu();
    }

    private async Task StartImu()
    {
        foreach (var device in _connectedDevices.Values)
        {
            try
            {
                await ResetClocks(device);
                var commandChar = await device.GetCharacteristicAsync(_serviceUuid, _commandCharUuid, CancellationToken.None);
                await device.WriteCharacteristicAsync(commandChar, new byte[] { 0x01 }, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting IMU: {ex.Message}");
                UpdateUI(() => ScanStatusLabel.Text = $"Error starting IMU: {ex.Message}");
            }
        }
    }

    private void StorePacket(string deviceId, byte[] data, string logKey)
    {
        if (!_receivedPackets.ContainsKey(logKey))
        {
            _receivedPackets.TryAdd(logKey, new ConcurrentBag<byte[]>());
        }

        _receivedPackets[logKey].Add(data);
        Debug.WriteLine($"Stored packet for {logKey}: {data.Length} bytes (Total: {_receivedPackets[logKey].Count})");

        if (_receivedPackets[logKey].Count >= EXPECTED_PACKET_COUNT)
        {
            Task.Run(async () =>
            {
                ParsePackets(logKey);
                await TryUploadToFirestoreAsync();
            });
        }
    }

    private void ParsePackets(string logKey)
    {
        if (!_deviceSamples.ContainsKey(logKey))
        {
            _deviceSamples.TryAdd(logKey, new List<SensorSample>());
        }

        var samples = _deviceSamples[logKey];
        samples.Clear();

        foreach (var data in _receivedPackets[logKey])
        {
            if (data.Length != 221)
            {
                Debug.WriteLine($"Invalid packet length for {logKey}: {data.Length} bytes");
                continue;
            }

            int offset = 0;
            byte packetNum = data[offset++];

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

        Debug.WriteLine($"Parsed {samples.Count} samples for {logKey}");
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
                Debug.WriteLine($"Error triggering freeze: {ex.Message}");
                UpdateUI(() => ScanStatusLabel.Text = $"Error triggering freeze: {ex.Message}");
            }
        }));

        await Task.Delay(50);

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
                {
                    ParsePackets(logKey);
                    await TryUploadToFirestoreAsync();
                    break;
                }
                await Task.Delay(10);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending queue: {ex.Message}");
            UpdateUI(() => ScanStatusLabel.Text = $"Error sending queue: {ex.Message}");
        }
    }

    private async Task TryUploadToFirestoreAsync()
    {
        // Check if all connected devices have parsed data
        var devicesWithKeys = _connectedDevices.Keys.Select(deviceId =>
        {
            var logKey = _deviceToLogKeyMap.GetValueOrDefault(deviceId, "Unknown");
            return (deviceId, logKey);
        }).Where(d => d.logKey != "Unknown").ToList();

        bool allDevicesReady = devicesWithKeys.All(d => _deviceSamples.ContainsKey(d.logKey) && _deviceSamples[d.logKey].Count >= EXPECTED_PACKET_COUNT * 5);

        if (!allDevicesReady || _activeSessionDoc != null)
        {
            Debug.WriteLine($"Not uploading to Firestore: All devices not ready ({devicesWithKeys.Count}/{_connectedDevices.Count}) or session already created.");
            return;
        }

        try
        {
            var firestore = CrossCloudFirestore.Current.Instance;

            var sessionData = new Dictionary<string, object>
            {
                { "DevRWristID", Devices.DeviceRightWristUUID ?? string.Empty },
                { "DevLWristID", Devices.DeviceLeftWristUUID ?? string.Empty },
                { "DevHipID", Devices.DeviceHipUUID ?? string.Empty },
                { "DevTorsoID", Devices.DeviceTorsoUUID ?? string.Empty },
                { "startTime", DateTime.UtcNow }
            };

            foreach (var (deviceId, logKey) in devicesWithKeys)
            {
                var fieldName = logKey switch
                {
                    "DevRWristID" => "DevRWrist",
                    "DevLWristID" => "DevLWrist",
                    "DevHipID" => "DevHip",
                    "DevTorsoID" => "DevTorso",
                    _ => null
                };

                if (fieldName == null) continue;

                var samples = _deviceSamples[logKey].Select(s => new
                {
                    timestamp = s.Timestamp,
                    accel = new { x = s.AccelX, y = s.AccelY, z = s.AccelZ },
                    gyro = new { x = s.GyroX, y = s.GyroY, z = s.GyroZ },
                    mag = new { x = s.MagX, y = s.MagY, z = s.MagZ }
                }).ToList();

                sessionData[fieldName] = samples;
            }

            var docRef = firestore
                .Collection("imu_sessions")
                .Document(DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            await docRef.SetAsync(sessionData);



            _activeSessionDoc = docRef;
            Debug.WriteLine($"Firestore session created with ID: {docRef.Id}");
            UpdateUI(() => ScanStatusLabel.Text = "Data uploaded to Firestore successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error uploading to Firestore: {ex.Message} (StackTrace: {ex.StackTrace})");
            UpdateUI(() => ScanStatusLabel.Text = $"Error uploading to Firestore: {ex.Message}");
        }
    }

    private async Task ProcessQueuedPackets()
    {
        foreach (var kvp in _receivedPackets)
        {
            var logKey = kvp.Key;
            var packets = kvp.Value;

            var deviceId = logKey switch
            {
                "DevRWristID" => Devices.DeviceRightWristUUID, // Fixed: Corrected typo
                "DevLWristID" => Devices.DeviceLeftWristUUID,
                "DevHipID" => Devices.DeviceHipUUID,
                "DevTorsoID" => Devices.DeviceTorsoUUID,
                _ => null
            };

            if (deviceId == null)
            {
                Debug.WriteLine($"Unknown log key: {logKey}");
                continue;
            }

            Debug.WriteLine($"Processing {packets.Count} queued packets for {logKey} (device: {deviceId})");

            while (packets.TryTake(out var packet))
            {
                Debug.WriteLine($"Processing queued packet for {logKey}: {packet.Length} bytes");
                ParsePackets(logKey);
            }
        }

        await TryUploadToFirestoreAsync();
    }

    private async Task CheckAndStartSession()
    {
        // No longer creates session immediately
        Debug.WriteLine("Session creation delayed until all devices have parsed data.");
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


