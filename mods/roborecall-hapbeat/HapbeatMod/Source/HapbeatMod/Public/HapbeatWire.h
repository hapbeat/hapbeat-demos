// Hapbeat wire protocol — PURE C++, deliberately free of any Unreal Engine type.
//
// Why no UE types: this file is the byte-for-byte counterpart of the C# reference
// implementation (mods/shared/HapbeatModCore/HapbeatProtocol.cs, which is itself a
// port of hapbeat-unity-sdk). Keeping it UE-free means it can be compiled and
// diffed against the C# output with a plain g++ (see Tools/), which is the only
// way to verify the layout without a Robo Recall Mod Kit install.
//
// Normative spec: hapbeat-contracts/specs/message-format.md and
//                 hapbeat-contracts/specs/device-addressing.md
// Reference impl:  mods/shared/HapbeatModCore/HapbeatProtocol.cs (authoritative)
//
// All multi-byte fields are LITTLE-ENDIAN on the wire, on every host byte order.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace hapbeat
{
	typedef std::vector<uint8_t> Bytes;

	// ---- Header constants (message-format.md §2) --------------------------------
	const uint16_t kMagic = 0x4842;   // "HB" as uint16 LE -> bytes 0x42 0x48
	const uint8_t  kProtocolVersion = 0x01;
	const int      kHeaderSize = 8;
	const int      kMaxPacketSize = 512;

	// Device OLED grid width; longer app names are truncated on the wire (DEC-029).
	const int      kMaxAppNameLength = 16;

	// ---- Command types ----------------------------------------------------------
	const uint8_t kCmdPlay = 0x01;
	const uint8_t kCmdStop = 0x02;
	const uint8_t kCmdStopAll = 0x03;
	const uint8_t kCmdPing = 0x10;
	const uint8_t kCmdPong = 0x11;
	const uint8_t kCmdConnectStatus = 0x20;
	const uint8_t kCmdError = 0xFF;

	// ---- Packet building --------------------------------------------------------

	/// Header + payload. Returns an EMPTY vector if the total would exceed
	/// kMaxPacketSize (the C# port throws instead; a mod must never take down the
	/// game for an over-long event id, so callers here check for empty and log).
	Bytes BuildPacket(uint8_t commandType, uint16_t seq, const Bytes& payload);

	/// PLAY payload: event_id(cstr) + target(cstr) + target_time(int64 LE) + gain(f32 LE).
	/// targetTimeUs == 0 means "play immediately".
	Bytes BuildPlayPayload(const std::string& eventId, int64_t targetTimeUs, float gain,
		const std::string& target);

	/// STOP payload: event_id(cstr) + target(cstr).
	Bytes BuildStopPayload(const std::string& eventId, const std::string& target);

	/// STOP_ALL payload: target(cstr).
	Bytes BuildStopAllPayload(const std::string& target);

	/// PING payload: timestamp(int64 LE), microseconds.
	Bytes BuildPingPayload(int64_t timestampUs);

	/// CONNECT_STATUS payload: connected(u8) + group(u8) + appName(cstr) + deviceName(cstr).
	/// appName is truncated to kMaxAppNameLength characters here, so no caller can
	/// push a string wider than the device display regardless of its settings file.
	Bytes BuildConnectStatusPayload(bool connected, uint8_t group,
		const std::string& appName, const std::string& deviceName);

	// ---- Packet parsing ---------------------------------------------------------

	struct ParsedPacket
	{
		uint8_t  Cmd;
		uint16_t Seq;
		Bytes    Payload;

		ParsedPacket() : Cmd(0), Seq(0) {}
	};

	/// Validates magic + version + declared payload length. Returns false (leaving
	/// `out` untouched) for anything malformed — never throws.
	bool ParsePacket(const uint8_t* data, int length, ParsedPacket& out);

	struct PongInfo
	{
		int64_t     Timestamp;    // echoed from our PING
		int64_t     ServerTime;
		std::string DeviceName;   // "" when the field was absent
		std::string Address;      // "" when absent -> caller must FAIL OPEN
		std::string Firmware;     // "" when absent
		int         VolumeLevel;  // -1 when absent
		int         VolumeWiper;  // -1 when absent
		bool        bHasAddress;  // false = device did not report an address at all

		PongInfo() : Timestamp(0), ServerTime(0), VolumeLevel(-1), VolumeWiper(-1),
			bHasAddress(false) {}
	};

	/// Parses the mandatory 16-byte timestamp/server_time pair plus the
	/// device-addressing extension tail (device_name, address, firmware_version,
	/// volume_level, volume_wiper). The tail is BEST EFFORT: a legacy or truncated
	/// payload simply leaves the later fields empty/-1. Returns false only when the
	/// mandatory 16 bytes are missing.
	bool ParsePong(const Bytes& payload, PongInfo& out);

	// ---- Device addressing (device-addressing.md §4.3) --------------------------

	/// Mirrors the firmware's addressMatch() and the C# AddressMatches():
	///  - empty target matches everything
	///  - both strings split on '/', compared segment by segment
	///  - a segment that is entirely "*" matches any ONE address segment
	///    (a partial wildcard such as "pos_*" is compared literally)
	///  - fewer target segments than address segments = front-match (OK)
	///  - more target segments than address segments = mismatch
	///  - a single TRAILING '/' on the target is ignored ("player_1/" == "player_1")
	bool AddressMatches(const std::string& target, const std::string& deviceAddress);

	/// Clamp an address-override value to 1..99; anything else becomes -1 (disabled).
	int NormalizeOverride(int value);

	/// Apply forced player/group overrides to a target string.
	/// Grammar: [prefix/] player_{N} / {position} [/group_{M}]  (device-addressing.md §2)
	/// With both overrides disabled the input is returned completely unchanged.
	std::string ResolveTarget(const std::string& target, int overridePlayer, int overrideGroup);
}
