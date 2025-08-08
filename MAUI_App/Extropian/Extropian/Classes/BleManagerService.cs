using Shiny.BluetoothLE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Linq;


namespace Extropian.Classes
{
    internal class BleManagerService
    {
        public static BleManagerService Instance { get; } = new BleManagerService();

        public event EventHandler<BleDeviceStatusChangedEventArgs> DeviceStatusChanged;

        private BleManagerService() { }

        public void MonitorDevice(IPeripheral device)
        {
            if (device == null)
                return;

            device.WhenStatusChanged()
                .DistinctUntilChanged()
                .Subscribe(status => {
                    var args = new BleDeviceStatusChangedEventArgs
                    {
                        Device = device,
                        Status = status
                    };
                    DeviceStatusChanged?.Invoke(this, args);
                });
        }
    }

    public class BleDeviceStatusChangedEventArgs : EventArgs
    {
        public IPeripheral Device { get; set; }
        public ConnectionState Status { get; set; }
    }
}