// ============================================================================
//  BYTE-LAYOUT CROSS-CHECK vs. the C# reference implementation
//  (mods/shared/HapbeatModCore/HapbeatProtocol.cs — authoritative)
//
//  Every field below was compared one by one against the C# source and against
//  hapbeat-contracts/specs/message-format.md. The Tools/HapbeatWireSelfTest
//  program re-checks these same layouts as literal byte sequences, and the
//  session that wrote this file diffed its hex output against the C# code
//  compiled and run on the same inputs (see README > ローカル検証).
//
//  HEADER (8 bytes) — C# BuildPacket
//    off 0  u16 LE  magic 0x4842            <- WriteUInt16(packet,0,MAGIC)
//    off 2  u8      protocol_version 0x01   <- packet[2] = PROTOCOL_VERSION
//    off 3  u8      command_type            <- packet[3] = commandType
//    off 4  u16 LE  seq                     <- WriteUInt16(packet,4,seq)
//    off 6  u16 LE  payload_length          <- WriteUInt16(packet,6,payloadLength)
//    off 8  ...     payload                 <- BlockCopy(payload,...,HEADER_SIZE,..)
//    size guard: total > 512 -> C# throws / C++ returns empty (see header note)
//
//  PLAY payload — C# BuildPlayPayload
//    event_id UTF-8 bytes, then one 0x00        <- payload[offset++] = 0
//    target   UTF-8 bytes, then one 0x00        <- "" == broadcast
//    i64 LE   target_time (microseconds)        <- WriteInt64
//    f32 LE   gain                              <- WriteFloat32 (IEEE-754)
//    total = len(id)+1 + len(target)+1 + 8 + 4
//
//  STOP payload — C# BuildStopPayload
//    event_id cstr + target cstr                (no time, no gain)
//
//  STOP_ALL payload — C# BuildStopAllPayload
//    target cstr                                (single field)
//
//  PING payload — C# BuildPingPayload
//    i64 LE timestamp (microseconds), 8 bytes exactly
//
//  CONNECT_STATUS payload — C# BuildConnectStatusPayload
//    off 0  u8   connected (1/0)
//    off 1  u8   group
//    then   appName cstr    (truncated to 16 chars BEFORE encoding, DEC-029)
//    then   deviceName cstr
//    NOTE the truncation unit: C# cuts at 16 UTF-16 chars, this file cuts at 16
//    UTF-8 bytes without splitting a multi-byte sequence. Identical for ASCII
//    app names (which is what the settings file is expected to hold); a
//    non-ASCII name may be cut one character earlier here. Keep app names ASCII.
//
//  PONG parse — C# ParsePongExtended
//    off 0  i64 LE timestamp        } mandatory 16 bytes
//    off 8  i64 LE server_time      }
//    then   device_name cstr        } best effort: a short/legacy payload just
//    then   address cstr            } leaves the remaining fields empty / -1
//    then   firmware_version cstr   } rather than failing the whole parse
//    then   u8 volume_level
//    then   u8 volume_wiper
// ============================================================================

#include "HapbeatWire.h"

#include <cstring>

namespace
{
	using hapbeat::Bytes;

	void PutU16(Bytes& b, uint16_t v)
	{
		b.push_back(static_cast<uint8_t>(v & 0xFF));
		b.push_back(static_cast<uint8_t>((v >> 8) & 0xFF));
	}

	void PutI64(Bytes& b, int64_t v)
	{
		const uint64_t u = static_cast<uint64_t>(v);
		for (int i = 0; i < 8; ++i)
		{
			b.push_back(static_cast<uint8_t>((u >> (8 * i)) & 0xFF));
		}
	}

	void PutF32(Bytes& b, float v)
	{
		// memcpy (not a reinterpret_cast) to stay free of strict-aliasing UB.
		uint32_t bits = 0;
		std::memcpy(&bits, &v, sizeof(bits));
		for (int i = 0; i < 4; ++i)
		{
			b.push_back(static_cast<uint8_t>((bits >> (8 * i)) & 0xFF));
		}
	}

	void PutCString(Bytes& b, const std::string& s)
	{
		b.insert(b.end(), s.begin(), s.end());
		b.push_back(0);
	}

	uint16_t GetU16(const uint8_t* p)
	{
		return static_cast<uint16_t>(p[0] | (static_cast<uint16_t>(p[1]) << 8));
	}

	int64_t GetI64(const uint8_t* p)
	{
		uint64_t u = 0;
		for (int i = 7; i >= 0; --i)
		{
			u = (u << 8) | p[i];
		}
		return static_cast<int64_t>(u);
	}

	/// Reads a NUL-terminated string starting at `offset`, advancing past the
	/// terminator. Sets `present` to false (and does not advance) when `offset` is
	/// already at/past the end — i.e. the field simply was not in the payload.
	std::string ReadCString(const Bytes& buf, size_t& offset, bool& present)
	{
		if (offset >= buf.size())
		{
			present = false;
			return std::string();
		}
		present = true;
		const size_t start = offset;
		size_t end = start;
		while (end < buf.size() && buf[end] != 0)
		{
			++end;
		}
		const std::string value(buf.begin() + static_cast<long>(start), buf.begin() + static_cast<long>(end));
		offset = (end < buf.size()) ? end + 1 : end; // skip the terminator when present
		return value;
	}

	/// Split on '/'. When cullEmpty is false the result mirrors C# String.Split
	/// (an empty input yields one empty segment).
	std::vector<std::string> Split(const std::string& s, bool cullEmpty)
	{
		std::vector<std::string> out;
		size_t start = 0;
		while (true)
		{
			const size_t sep = s.find('/', start);
			const std::string seg = (sep == std::string::npos)
				? s.substr(start)
				: s.substr(start, sep - start);
			if (!cullEmpty || !seg.empty())
			{
				out.push_back(seg);
			}
			if (sep == std::string::npos)
			{
				break;
			}
			start = sep + 1;
		}
		return out;
	}

	std::string Join(const std::vector<std::string>& segs)
	{
		std::string out;
		for (size_t i = 0; i < segs.size(); ++i)
		{
			if (i > 0)
			{
				out += '/';
			}
			out += segs[i];
		}
		return out;
	}

	bool StartsWith(const std::string& s, const char* prefix)
	{
		const size_t n = std::strlen(prefix);
		return s.size() >= n && s.compare(0, n, prefix) == 0;
	}

	int IndexOfPrefix(const std::vector<std::string>& segs, const char* prefix)
	{
		for (size_t i = 0; i < segs.size(); ++i)
		{
			if (StartsWith(segs[i], prefix))
			{
				return static_cast<int>(i);
			}
		}
		return -1;
	}

	std::string IntToString(int v)
	{
		// std::to_string is C++11; the Mod Kit's toolchain (VS2015) has it, but
		// this hand-rolled version keeps the file compilable by anything.
		if (v == 0)
		{
			return "0";
		}
		const bool neg = v < 0;
		unsigned int u = neg ? static_cast<unsigned int>(-(v + 1)) + 1u : static_cast<unsigned int>(v);
		char buf[16];
		int i = 0;
		while (u > 0 && i < 15)
		{
			buf[i++] = static_cast<char>('0' + (u % 10));
			u /= 10;
		}
		std::string out;
		if (neg)
		{
			out += '-';
		}
		while (i > 0)
		{
			out += buf[--i];
		}
		return out;
	}

	/// Truncate to at most `maxBytes` UTF-8 bytes without splitting a multi-byte
	/// sequence (a continuation byte is 10xxxxxx).
	std::string TruncateUtf8(const std::string& s, size_t maxBytes)
	{
		if (s.size() <= maxBytes)
		{
			return s;
		}
		size_t cut = maxBytes;
		while (cut > 0 && (static_cast<unsigned char>(s[cut]) & 0xC0) == 0x80)
		{
			--cut;
		}
		return s.substr(0, cut);
	}
}

namespace hapbeat
{
	Bytes BuildPacket(uint8_t commandType, uint16_t seq, const Bytes& payload)
	{
		const size_t total = static_cast<size_t>(kHeaderSize) + payload.size();
		if (total > static_cast<size_t>(kMaxPacketSize))
		{
			return Bytes(); // caller logs; never abort the game over one event id
		}

		Bytes packet;
		packet.reserve(total);
		PutU16(packet, kMagic);
		packet.push_back(kProtocolVersion);
		packet.push_back(commandType);
		PutU16(packet, seq);
		PutU16(packet, static_cast<uint16_t>(payload.size()));
		packet.insert(packet.end(), payload.begin(), payload.end());
		return packet;
	}

	Bytes BuildPlayPayload(const std::string& eventId, int64_t targetTimeUs, float gain,
		const std::string& target)
	{
		Bytes p;
		p.reserve(eventId.size() + target.size() + 14);
		PutCString(p, eventId);
		PutCString(p, target);
		PutI64(p, targetTimeUs);
		PutF32(p, gain);
		return p;
	}

	Bytes BuildStopPayload(const std::string& eventId, const std::string& target)
	{
		Bytes p;
		p.reserve(eventId.size() + target.size() + 2);
		PutCString(p, eventId);
		PutCString(p, target);
		return p;
	}

	Bytes BuildStopAllPayload(const std::string& target)
	{
		Bytes p;
		p.reserve(target.size() + 1);
		PutCString(p, target);
		return p;
	}

	Bytes BuildPingPayload(int64_t timestampUs)
	{
		Bytes p;
		p.reserve(8);
		PutI64(p, timestampUs);
		return p;
	}

	Bytes BuildConnectStatusPayload(bool connected, uint8_t group,
		const std::string& appName, const std::string& deviceName)
	{
		Bytes p;
		p.push_back(connected ? 1 : 0);
		p.push_back(group);
		PutCString(p, TruncateUtf8(appName, static_cast<size_t>(kMaxAppNameLength)));
		PutCString(p, deviceName);
		return p;
	}

	bool ParsePacket(const uint8_t* data, int length, ParsedPacket& out)
	{
		if (data == 0 || length < kHeaderSize)
		{
			return false;
		}
		if (GetU16(data) != kMagic)
		{
			return false;
		}
		if (data[2] != kProtocolVersion)
		{
			return false;
		}

		const uint16_t payloadLength = GetU16(data + 6);
		if (length < kHeaderSize + static_cast<int>(payloadLength))
		{
			return false; // truncated
		}

		out.Cmd = data[3];
		out.Seq = GetU16(data + 4);
		out.Payload.assign(data + kHeaderSize, data + kHeaderSize + payloadLength);
		return true;
	}

	bool ParsePong(const Bytes& payload, PongInfo& out)
	{
		if (payload.size() < 16)
		{
			return false;
		}

		out.Timestamp = GetI64(&payload[0]);
		out.ServerTime = GetI64(&payload[8]);

		size_t offset = 16;
		bool present = false;
		out.DeviceName = ReadCString(payload, offset, present);
		out.Address = ReadCString(payload, offset, present);
		out.bHasAddress = present && !out.Address.empty();
		out.Firmware = ReadCString(payload, offset, present);
		if (offset < payload.size())
		{
			out.VolumeLevel = payload[offset++];
		}
		if (offset < payload.size())
		{
			out.VolumeWiper = payload[offset++];
		}
		return true;
	}

	bool AddressMatches(const std::string& target, const std::string& deviceAddress)
	{
		if (target.empty())
		{
			return true;
		}

		const std::vector<std::string> targetSegments = Split(target, /*cullEmpty=*/false);
		const std::vector<std::string> addressSegments = Split(deviceAddress, /*cullEmpty=*/false);

		// A single TRAILING '/' terminates the target rather than adding an empty
		// segment: the firmware's pointer walk advances past the separator and then
		// exits on `while (*tp)`, so "player_1/" behaves exactly like "player_1".
		// Only the LAST empty segment is dropped, and only once — "a//" really does
		// compare an empty segment in firmware, and still mismatches.
		size_t targetCount = targetSegments.size();
		if (targetCount > 1 && targetSegments[targetCount - 1].empty())
		{
			--targetCount;
		}

		for (size_t i = 0; i < targetCount; ++i)
		{
			if (i >= addressSegments.size())
			{
				return false; // target longer than address = mismatch
			}
			// Only a segment that is ENTIRELY "*" wildcards; "pos_*" is literal.
			if (targetSegments[i] != "*" && targetSegments[i] != addressSegments[i])
			{
				return false;
			}
		}

		return true; // front-match or exact match
	}

	int NormalizeOverride(int value)
	{
		return (value >= 1 && value <= 99) ? value : -1;
	}

	std::string ResolveTarget(const std::string& target, int overridePlayer, int overrideGroup)
	{
		if (overridePlayer < 1 && overrideGroup < 1)
		{
			return target; // both disabled: full passthrough, byte-identical
		}

		std::vector<std::string> segs = Split(target, /*cullEmpty=*/true);

		if (overridePlayer >= 1)
		{
			const std::string playerSeg = "player_" + IntToString(overridePlayer);
			const int i = IndexOfPrefix(segs, "player_");
			if (i >= 0)
			{
				segs[static_cast<size_t>(i)] = playerSeg;
			}
			else
			{
				const int j = IndexOfPrefix(segs, "pos_");
				if (j > 0)
				{
					// Replace the placeholder (e.g. "*") sitting right before position.
					segs[static_cast<size_t>(j - 1)] = playerSeg;
				}
				else
				{
					// j == 0 (position at front) or j == -1 (no position segment).
					segs.insert(segs.begin(), playerSeg);
				}
			}
		}

		if (overrideGroup >= 1)
		{
			const std::string groupSeg = "group_" + IntToString(overrideGroup);
			const int k = IndexOfPrefix(segs, "group_");
			if (k >= 0)
			{
				segs[static_cast<size_t>(k)] = groupSeg;
			}
			else
			{
				// Matching is POSITIONAL (device-addressing.md §2): the i-th target
				// segment is only ever compared against the i-th address segment, and
				// "*" consumes exactly one slot. group_ must therefore land in its
				// grammar slot (immediately after {position}) — appending at the end
				// would drop it in whatever slot happens to be next and the firmware
				// would never match.
				const int posIdx = IndexOfPrefix(segs, "pos_");
				if (posIdx >= 0)
				{
					segs.insert(segs.begin() + (posIdx + 1), groupSeg);
				}
				else
				{
					// No explicit position segment. Find the player slot: an explicit
					// player_ segment, or a leading bare "*" acting as the wildcard.
					int playerIdx = IndexOfPrefix(segs, "player_");
					if (playerIdx < 0 && !segs.empty() && segs[0] == "*")
					{
						playerIdx = 0;
					}

					if (playerIdx >= 0)
					{
						// The position slot is right after the player slot. Only pad a
						// "*" when that slot is genuinely empty — reusing an existing
						// segment keeps group_ in the 3rd slot instead of pushing it to
						// a 4th, which would make the target longer than the device
						// address and break the positional match outright.
						const size_t posSlot = static_cast<size_t>(playerIdx) + 1;
						if (posSlot >= segs.size())
						{
							segs.push_back("*");
						}
						segs.insert(segs.begin() + static_cast<long>(posSlot + 1), groupSeg);
					}
					else
					{
						// Whatever is present is a free prefix with no player/position
						// slot. Append player + position placeholders, then group, so
						// group stays after position and the prefix survives ahead of it.
						segs.push_back("*");
						segs.push_back("*");
						segs.push_back(groupSeg);
					}
				}
			}
		}

		return Join(segs);
	}
}
