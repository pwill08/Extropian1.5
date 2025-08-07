using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extropian.Classes
{
    public class SwingData
    {
        public string TimestampString { get; set; }
        public DateTime Timestamp { get; set; }
        public double WristSpeed { get; set; }
        public int HipRotation { get; set; }
        public int WristInitTS { get; set; }
        public int HipInitTS { get; set; }
        public int TorsoInitTS { get; set; }
    }
}
