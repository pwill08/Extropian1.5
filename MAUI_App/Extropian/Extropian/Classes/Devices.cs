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
        public static string DeviceRightWristUUID { get; set; }
        public static string DeviceRightWristPosition = "right_wrist";
        public static IPeripheral DeviceRightWristPeripheral { get; set; }
        public static bool IsDeviceRightWristConnected { get; set; } = false;



        static string DeviceLeftWristName { get; set; }
        public static string DeviceLeftWristUUID { get; set; }
        public static string DeviceLeftWristPosition = "left_wrist";
        public static IPeripheral DeviceLeftWristPeripheral { get; set; }
        public static bool IsDeviceLeftWristConnected { get; set; } = false;


        static string DeviceHipName { get; set; }
        public static string DeviceHipUUID { get; set; }
        public static string DeviceHipPosition = "hip";
        public static IPeripheral DeviceHipPeripheral { get; set; }
        public static bool IsDeviceHipConnected { get; set; } = false;


        static string DeviceTorsoName { get; set; }
        public static string DeviceTorsoUUID { get; set; }
        public static string DeviceTorsoPosition = "torso";
        public static IPeripheral DeviceTorsoPeripheral { get; set; }
        public static bool IsDeviceTorsoConnected { get; set; } = false;


    }
}
