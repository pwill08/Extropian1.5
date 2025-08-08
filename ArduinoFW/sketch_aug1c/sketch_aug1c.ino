#include <ArduinoBLE.h>              // Arduino BLE library for Bluetooth Low Energy
#include <Arduino_BMI270_BMM150.h>   // IMU library for BMI270 (accel+gyro) + BMM150 (magnetometer)

// ✅ BLE device name and UUIDs for service and characteristics
#define DEVICE_NAME "Extropian"
#define SERVICE_UUID "12345678-1234-5678-1234-56789abcdef0"
#define COMMAND_CHAR_UUID "12345678-1234-5678-1234-56789abcdef1"
#define DATA_CHAR_UUID    "12345678-1234-5678-1234-56789abcdef2"

#define CMD_RESET_CLOCK 0x02
#define CMD_START_IMU 0x01
#define CMD_STOP_IMU 0x00
#define CMD_COPY_QUEUE  0x03
#define CMD_SEND_QUEUE 0x04

#define QUEUE_SIZE 105

bool IMU_ON = false;
bool threshold_met = false;

uint32_t clock_offset = 0;

unsigned long lastSampleTime = 0;
const unsigned long SAMPLE_INTERVAL_MS = 33;

struct IMUSample {
  uint32_t timestamp;
  float ax, ay, az;
  float gx, gy, gz;
  float mx, my, mz;
};

IMUSample imuQueue[QUEUE_SIZE];  // ring buffer array
int head = 0;    // index for writing
int count = 0; 

IMUSample copiedQueue[QUEUE_SIZE];
int copiedCount = 0;

// ✅ Create a BLE service and two characteristics
BLEService imuService(SERVICE_UUID);

// Command characteristic – central writes 1 byte to trigger data sending
BLEByteCharacteristic commandChar(COMMAND_CHAR_UUID, BLEWriteWithoutResponse);

BLEByteCharacteristic thresholdChar("12345678-1234-5678-1234-56789abcdef3",  BLENotify);

// Data characteristic – sends 221 bytes 
BLECharacteristic dataChar(DATA_CHAR_UUID, BLEIndicate, 221);

void setup() {
  Serial.begin(115200);   // Start serial monitor at 115200 baud
  while (!Serial);        // Wait for Serial to be ready

  // ✅ Initialize the IMU (BMI270 + BMM150)
  if (!IMU.begin()) {
    Serial.println("Failed to initialize IMU!");
    while (1); // Stop program if IMU fails to start
  }

  // Set IMU output data rates
  IMU.setAccelODR(BMI2_ACC_ODR_25HZ);     // Accelerometer at 25 Hz
  IMU.setAccelFS(3);
  IMU.setGyroODR(BMI2_GYR_ODR_25HZ);      // Gyroscope at 25 Hz
  IMU.setMagnetODR(BMM150_DATA_RATE_30HZ); // Magnetometer at 30 Hz

  // ✅ Initialize BLE module
  if (!BLE.begin()) {
    Serial.println("Failed to initialize BLE!");
    while (1); // Stop program if BLE fails to start
  }

  // Configure BLE device name and service

  // ✅ Get MAC suffix
  String mac = BLE.address();                  // e.g., F4:C8:ED:12:34:56
  String suffix = mac.substring(mac.length() - 5);  // e.g., 34:56
  suffix.replace(":", "");                    // Remove ":"

  // ✅ Set dynamic name
  String deviceName = "Extropian-" + suffix;
  BLE.setLocalName(deviceName.c_str());      // Local advertising name
  BLE.setDeviceName(deviceName.c_str());     // Actual device name

  // BLE.setLocalName(DEVICE_NAME);      // Local advertising name
  // BLE.setDeviceName(DEVICE_NAME);     // Actual device name
  BLE.setAdvertisedService(imuService); // Advertise the IMU service

  // Add characteristics to service
  imuService.addCharacteristic(commandChar);
  imuService.addCharacteristic(dataChar);
  imuService.addCharacteristic(thresholdChar);
  BLE.addService(imuService);

  // Initialize command characteristic to 0
  commandChar.writeValue(0);

  // ⚡ Optimize BLE for faster connection and advertising
  BLE.setAdvertisingInterval(32);   // Advertising every 20 ms (32 × 0.625 ms)
  BLE.setConnectionInterval(6, 12); // Connection interval 7.5–15 ms

  // Start advertising the device
  BLE.advertise();
  Serial.println("✅ BLE advertising as: " DEVICE_NAME);
}

void sendThresholdByte() {
  thresholdChar.writeValue(0x05);
  Serial.print("📤 Sent threshold byte: 0x05"); 
}


void loop() {
  BLEDevice central = BLE.central();

  if (central) {
    if (!central.connected()) return;

    // Check for new command
    if (commandChar.written()) {
      uint8_t cmd = commandChar.value();

      if (cmd == CMD_START_IMU) {          // 0x01
        IMU_ON = true;
        Serial.println("IMU ON");
      } 

      if (cmd == CMD_STOP_IMU) {          // 0x01
        IMU_ON = false;
        Serial.println("IMU OFF");
      } 


      else if (cmd == CMD_COPY_QUEUE ) { // 0x03
        IMU_ON = false;
        copyIMUQueue();  // print queue once stopped
      }
      else if (cmd == CMD_RESET_CLOCK) {  // 0x02
        clock_offset = millis();
        Serial.println("Reset Clock");
        Serial.println(clock_offset);
      }

      else if (cmd == CMD_SEND_QUEUE) { // 0x04
        IMU_ON = false;       // Stop collecting
        sendCopiedQueueOverBLE();   // Send frozen queue
      }
    }

    // ✅ Collect IMU data only if IMU_ON == true
    if (IMU_ON && millis() - lastSampleTime >= SAMPLE_INTERVAL_MS) {
      lastSampleTime = millis();
      collectIMU();
    }
  }
}


void copyIMUQueue() {
  if (count < 105) {
    Serial.println("❌ Not enough data to copy");
    copiedCount = 0;
    return;
  }

  int tail = (head - count + QUEUE_SIZE) % QUEUE_SIZE;
  for (int i = 0; i < count; i++) {
    copiedQueue[i] = imuQueue[(tail + i) % QUEUE_SIZE];
    copiedQueue[i].timestamp -= clock_offset;
  }
  copiedCount = count;

  Serial.print("✅ Copied "); Serial.print(copiedCount); Serial.println(" samples to copiedQueue");
}


// void show_queue() {
//   for (int i = 0 ; i < count ; i++) {
//     int idx = (head - count + i + QUEUE_SIZE) % QUEUE_SIZE;
//     IMUSample s = imuQueue[idx];

//     // Serial.print("Sample "); Serial.print(i); Serial.print(": ");
//     // Serial.print("TS="); Serial.print(s.timestamp);
//     // Serial.print(" Accel="); Serial.print(s.ax, 3); Serial.print(",");
//     // Serial.print(s.ay, 3); Serial.print(","); Serial.print(s.az, 3);
//     // Serial.print(" Gyro="); Serial.print(s.gx, 3); Serial.print(",");
//     // Serial.print(s.gy, 3); Serial.print(","); Serial.print(s.gz, 3);
//     // Serial.print(" Mag="); Serial.print(s.mx, 3); Serial.print(",");
//     // Serial.print(s.my, 3); Serial.print(","); Serial.println(s.mz, 3);
//   }
//   Serial.println("Queue copied");
// }

void collectIMU() {
  IMUSample sample;
  sample.timestamp = millis();
  
    IMU.readAcceleration(sample.ax, sample.ay, sample.az);
    IMU.readGyroscope(sample.gx, sample.gy, sample.gz);
    IMU.readMagneticField(sample.mx, sample.my, sample.mz);
  



  pushIMUSample(sample); 
  
  float accelerometer_magnitude = sqrt(sq(sample.ax) + sq(sample.ay) + sq(sample.az));
  if (accelerometer_magnitude >= 3 && threshold_met == false){
    sendThresholdByte();
    threshold_met = true;
    
  }
}

void pushIMUSample(IMUSample sample) {
  imuQueue[head] = sample;                  // store at head
  head = (head + 1) % QUEUE_SIZE;           // move head forward
  if (count < QUEUE_SIZE) {
    count++;
  }  
}

bool popIMUSample(IMUSample &sample) {
  if (count == 0) return false;             // queue empty

  int tail = (head - count + QUEUE_SIZE) % QUEUE_SIZE;  
  sample = imuQueue[tail];                  // copy oldest sample
  count--;                                  // remove it
  return true;
}





void sendCopiedQueueOverBLE() {
  if (copiedCount < 105) {
    Serial.println("❌ Not enough copied data to send");
    return;
  }

  Serial.println("📡 Sending copied queue over BLE");
  for (int pkt = 0; pkt < 21; pkt++) {
    uint8_t payload[221];
    int offset = 0;

    payload[offset++] = pkt + 1;

    for (int s = 0; s < 5; s++) {
      int idx = pkt * 5 + s;
      IMUSample smp = copiedQueue[idx];

      payload[offset++] = 'A';
      memcpy(payload + offset, &smp.timestamp, 4); offset += 4;

      payload[offset++] = 'B';
      memcpy(payload + offset, &smp.ax, 4); offset += 4;
      memcpy(payload + offset, &smp.ay, 4); offset += 4;
      memcpy(payload + offset, &smp.az, 4); offset += 4;

      payload[offset++] = 'C';
      memcpy(payload + offset, &smp.gx, 4); offset += 4;
      memcpy(payload + offset, &smp.gy, 4); offset += 4;
      memcpy(payload + offset, &smp.gz, 4); offset += 4;

      payload[offset++] = 'D';
      memcpy(payload + offset, &smp.mx, 4); offset += 4;
      memcpy(payload + offset, &smp.my, 4); offset += 4;
      memcpy(payload + offset, &smp.mz, 4); offset += 4;
    }

    dataChar.writeValue(payload, 221);
    Serial.print("📦 Sent packet "); Serial.println(pkt + 1);
    delay(20);
  }

  Serial.println("✅ Finished sending copied queue");
}

