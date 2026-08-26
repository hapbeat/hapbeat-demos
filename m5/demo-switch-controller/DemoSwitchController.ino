#include <M5Unified.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <Preferences.h>
#include <mbedtls/md.h>

static constexpr char WIFI_SSID[] = "YOUR_WIFI_SSID";
static constexpr char WIFI_PASSWORD[] = "YOUR_WIFI_PASSWORD";
static constexpr char HMD_IP[] = "192.168.1.50";
static constexpr char CONTROLLER_ID[] = "m5-main";
static constexpr char TARGET_DEMO_A[] = "gloveball";
static constexpr char TARGET_DEMO_B[] = "boxing";
static constexpr char TARGET_DEMO_C[] = "rhythm";
static constexpr char SHARED_SECRET[] = "";  // Provision locally; never commit a real secret.
static constexpr uint16_t DEMO_SWITCH_PORT = 7710;
static constexpr uint16_t CONTROLLER_PORT = 7711;

WiFiUDP udp;
Preferences preferences;
uint64_t sequenceNumber = 0;

String field(const char* name, const String& value) {
  return String(name) + "=" + String(value.length()) + ":" + value + "\n";
}

String canonicalCommand(uint64_t seq, const char* targetDemoId) {
  char seqBuffer[24];
  snprintf(seqBuffer, sizeof(seqBuffer), "%llu", static_cast<unsigned long long>(seq));
  return String("HAPBEAT-DEMO-SWITCH/1\nCOMMAND\n") +
      field("version", "1") + field("type", "SWITCH") +
      field("controller_id", CONTROLLER_ID) + field("seq", seqBuffer) +
      field("demo_id", targetDemoId);
}

String hmacSha256(const String& canonical) {
  byte digest[32];
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  mbedtls_md_hmac(info,
      reinterpret_cast<const unsigned char*>(SHARED_SECRET), strlen(SHARED_SECRET),
      reinterpret_cast<const unsigned char*>(canonical.c_str()), canonical.length(), digest);
  char hex[65];
  for (size_t index = 0; index < sizeof(digest); ++index) snprintf(hex + index * 2, 3, "%02x", digest[index]);
  hex[64] = '\0';
  return String(hex);
}

void sendSwitch(const char* targetDemoId) {
  sequenceNumber++;
  preferences.putULong64("seq", sequenceNumber);
  const String canonical = canonicalCommand(sequenceNumber, targetDemoId);
  String json = String("{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"") + CONTROLLER_ID +
      "\",\"seq\":" + String(static_cast<unsigned long long>(sequenceNumber)) +
      ",\"demo_id\":\"" + targetDemoId + "\"";
  if (strlen(SHARED_SECRET) > 0) json += ",\"auth\":\"" + hmacSha256(canonical) + "\"";
  json += "}";

  udp.beginPacket(HMD_IP, DEMO_SWITCH_PORT);
  udp.write(reinterpret_cast<const uint8_t*>(json.c_str()), json.length());
  udp.endPacket();
  Serial.printf("sent seq=%llu demo=%s\n", static_cast<unsigned long long>(sequenceNumber), targetDemoId);
}

void setup() {
  auto config = M5.config();
  M5.begin(config);
  Serial.begin(115200);
  preferences.begin("demo-switch", false);
  sequenceNumber = preferences.getULong64("seq", 0);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  while (WiFi.status() != WL_CONNECTED) delay(100);
  udp.begin(CONTROLLER_PORT);
  Serial.printf("ready ip=%s localPort=%u\n", WiFi.localIP().toString().c_str(), CONTROLLER_PORT);
}

void loop() {
  M5.update();
  if (M5.BtnA.wasPressed()) sendSwitch(TARGET_DEMO_A);
  if (M5.BtnB.wasPressed()) sendSwitch(TARGET_DEMO_B);
  if (M5.BtnC.wasPressed()) sendSwitch(TARGET_DEMO_C);
  const int packetSize = udp.parsePacket();
  if (packetSize > 0) {
    char response[1025];
    const int count = udp.read(reinterpret_cast<uint8_t*>(response), sizeof(response) - 1);
    response[count > 0 ? count : 0] = '\0';
    udp.flush();
    Serial.printf("status %s\n", response);
  }
  delay(5);
}
