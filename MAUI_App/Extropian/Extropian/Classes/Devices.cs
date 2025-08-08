using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shiny.BluetoothLE;

namespace Extropian.Classes
{

    static internal class Devices
    {

        static string DeviceRightWristName { get; set; }
        static string DeviceRightWristMAC { get; set; }
        public static IPeripheral DeviceRightWristPeripheral { get; set; }
        public static bool IsDeviceRightWristConnected { get; set; } = false;



        static string DeviceLeftWristName { get; set; }
        static string DeviceLeftWristMAC { get; set; }
        public static IPeripheral DeviceLeftWristPeripheral { get; set; }
        public static bool IsDeviceLeftWristConnected { get; set; } = false;


        static string DeviceHipName { get; set; }
        static string DeviceHipMAC { get; set; }
        public static IPeripheral DeviceHipPeripheral { get; set; }
        public static bool IsDeviceHipConnected { get; set; } = false;


        static string DeviceTorsoName { get; set; }
        static string DevicTorsoMAC { get; set; }
        public static IPeripheral DeviceTorsoPeripheral { get; set; }
        public static bool IsDeviceTorsoConnected { get; set; } = false;


    }
}
