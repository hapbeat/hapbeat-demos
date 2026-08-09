// Standalone self-test for the pure-C++ wire layer. No Unreal Engine involved —
// this is the only part of the plugin that can be verified without a Robo Recall
// Mod Kit install, so it carries the whole local-verification burden.
//
// It prints one `name=hexbytes` line per case and asserts the layouts that
// hapbeat-contracts/specs/message-format.md fixes. The same case names are
// produced by Tools/csharp-parity/ (a throwaway C# program that calls
// mods/shared/HapbeatModCore/HapbeatProtocol.cs), so the two outputs can be
// diffed line by line — see README.md > ローカル検証.
//
// Build (from this directory):
//   g++ -std=c++11 -I ../HapbeatMod/Source/HapbeatMod/Public ../HapbeatMod/Source/HapbeatMod/Private/HapbeatWire.cpp HapbeatWireSelfTest.cpp -o wiretest
//   ./wiretest          # prints the dump, exits non-zero on any failed assert

#include "HapbeatWire.h"

#include <cstdio>
#include <iostream>
#include <string>
#include <vector>

namespace
{
	int gFailures = 0;

	std::string Hex(const hapbeat::Bytes& b)
	{
		static const char* kDigits = "0123456789abcdef";
		std::string out;
		out.reserve(b.size() * 2);
		for (size_t i = 0; i < b.size(); ++i)
		{
			out += kDigits[(b[i] >> 4) & 0xF];
			out += kDigits[b[i] & 0xF];
		}
		return out;
	}

	void Dump(const char* name, const hapbeat::Bytes& b)
	{
		std::cout << name << "=" << Hex(b) << "\n";
	}

	void Check(bool condition, const std::string& what)
	{
		if (!condition)
		{
			++gFailures;
			std::cerr << "FAIL: " << what << "\n";
		}
	}

	void AppendCString(hapbeat::Bytes& b, const std::string& s)
	{
		b.insert(b.end(), s.begin(), s.end());
		b.push_back(0);
	}

	void CheckEq(const std::string& actual, const std::string& expected, const std::string& what)
	{
		if (actual != expected)
		{
			++gFailures;
			std::cerr << "FAIL: " << what << "\n  expected: " << expected
					  << "\n  actual:   " << actual << "\n";
		}
	}

	// Full AddressMatches case list, transcribed from the authoritative C# suite
	// (mods/shared/HapbeatModCore.Tests/Program.cs > AddressMatchesTests). Used
	// twice below: once for local asserts, once for the dump that is diffed
	// against Tools/csharp-parity — so the C++ port is held to exactly the same
	// case list as the C# reference, not a subset.
	//
	// The three C# null cases (null target / null address) have no counterpart
	// here: AddressMatches takes std::string by const reference, so "null" is not
	// representable. Their empty-string equivalents are covered.
	struct FMatchCase { const char* Target; const char* Address; bool bExpected; const char* Reason; };
	const FMatchCase kMatchCases[] = {
		{ "", "player_1/pos_neck", true, "empty target = all devices" },
		{ "player_1", "player_1/pos_neck", true, "front-match" },
		{ "player_1", "player_2/pos_neck", false, "player mismatch" },
		{ "player_1/pos_neck", "player_1/pos_neck", true, "exact match" },
		{ "player_1/pos_neck", "player_1/pos_r_wrist", false, "position mismatch" },
		{ "*/pos_neck", "player_1/pos_neck", true, "wildcard player" },
		{ "*/pos_neck", "player_2/pos_neck", true, "wildcard player, different player" },
		{ "player_1/pos_*", "player_1/pos_neck", false, "partial wildcard is literal" },
		{ "player_1/*", "player_1/pos_neck", true, "whole-segment wildcard covers any position" },
		{ "red", "red/player_1/pos_neck", true, "prefix front-match" },
		{ "red/*/player_1", "red/alpha/player_1/pos_neck", true, "wildcard + front-match" },
		{ "player_1/pos_neck/group_1", "player_1/pos_neck/group_1", true, "group exact match" },
		{ "player_1/pos_neck/group_1", "player_1/pos_neck/group_2", false, "group mismatch" },
		{ "player_1/pos_neck", "player_1/pos_neck/group_1", true, "group omitted = all groups" },
		{ "*/*/group_1", "player_2/pos_chest/group_1", true, "player/position wildcard, group pinned" },
		{ "player_1/pos_neck/group_1", "player_1/pos_neck", false, "target longer than address" },
		{ "player_1/", "player_1/pos_neck/group_1", true, "trailing slash = 'player_1'" },
		{ "player_1/pos_neck/", "player_1/pos_neck/group_1", true, "trailing slash after position" },
		{ "player_1/", "player_1", true, "trailing slash, exact-length address" },
		{ "player_2/", "player_1/pos_neck/group_1", false, "trailing slash keeps a mismatch" },
		{ "player_1//", "player_1/pos_neck", false, "double slash: one real empty segment remains" },
		{ "/", "player_1/pos_neck", false, "bare slash = one empty segment" },
		{ "*/*/group_2", "player_1/pos_chest/group_1", false, "group-only target, different group" },
		{ "*/*/group_2", "player_1/pos_chest/group_2", true, "group-only target, matching group" },
		// C++-only extras: the partial wildcard compared against itself, which is
		// the sharpest statement of "pos_* is a literal string, not a pattern".
		{ "pos_*", "pos_chest", false, "partial wildcard does not pattern-match" },
		{ "pos_*", "pos_*", true, "partial wildcard matches itself literally" },
	};
	const size_t kMatchCaseCount = sizeof(kMatchCases) / sizeof(kMatchCases[0]);
}

int main()
{
	using namespace hapbeat;

	// ---- header ------------------------------------------------------------
	// magic 0x4842 LE -> 42 48 | version 01 | cmd | seq LE | payload_len LE
	{
		Bytes payload;
		payload.push_back(0xAA);
		payload.push_back(0xBB);
		const Bytes packet = BuildPacket(kCmdPlay, 0x0102, payload);
		Dump("header", packet);
		CheckEq(Hex(packet), "424801010201" "0200" "aabb", "header layout");
		Check(packet.size() == 10, "header size = 8 + payload");
	}

	// ---- PLAY --------------------------------------------------------------
	// event_id cstr + target cstr + int64 LE time + float32 LE gain
	{
		const Bytes p = BuildPlayPayload("vr-shooter-kit.shot_recoil", 0, 1.0f, "");
		Dump("play_payload", p);
		// "vr-shooter-kit.shot_recoil" = 26 bytes, +1 NUL, +1 empty target NUL,
		// +8 time, +4 gain = 40
		Check(p.size() == 40, "PLAY payload size");
		Check(p[26] == 0, "PLAY event_id terminator");
		Check(p[27] == 0, "PLAY empty target terminator");
		// 1.0f = 0x3F800000 -> little-endian 00 00 80 3f
		CheckEq(Hex(Bytes(p.end() - 4, p.end())), "0000803f", "PLAY gain float32 LE");
		// target_time 0
		CheckEq(Hex(Bytes(p.end() - 12, p.end() - 4)), "0000000000000000", "PLAY target_time int64 LE");
	}
	{
		// Non-empty target + a non-trivial gain and time.
		const Bytes p = BuildPlayPayload("k.e", 1234567890123LL, 0.5f, "player_2/*/group_3");
		Dump("play_payload_targeted", p);
		CheckEq(Hex(Bytes(p.end() - 4, p.end())), "0000003f", "PLAY gain 0.5");
		CheckEq(Hex(Bytes(p.end() - 12, p.end() - 4)), "cb04fb711f010000", "PLAY target_time 1234567890123");
	}

	// ---- STOP / STOP_ALL ---------------------------------------------------
	{
		const Bytes p = BuildStopPayload("k.e", "");
		Dump("stop_payload", p);
		CheckEq(Hex(p), "6b2e650000", "STOP = event_id cstr + target cstr");
	}
	{
		const Bytes p = BuildStopAllPayload("player_1");
		Dump("stop_all_payload", p);
		CheckEq(Hex(p), "706c617965725f3100", "STOP_ALL = target cstr");
	}

	// ---- PING --------------------------------------------------------------
	{
		const Bytes p = BuildPingPayload(1LL);
		Dump("ping_payload", p);
		CheckEq(Hex(p), "0100000000000000", "PING = int64 LE timestamp");
	}

	// ---- CONNECT_STATUS ----------------------------------------------------
	{
		const Bytes p = BuildConnectStatusPayload(true, 0, "RoboRecall", "PC-1");
		Dump("connect_status", p);
		CheckEq(Hex(p), "0100" "526f626f526563616c6c00" "50432d3100",
			"CONNECT_STATUS = connected + group + appName cstr + deviceName cstr");
	}
	{
		// 20 chars -> truncated to 16 (DEC-029).
		const Bytes p = BuildConnectStatusPayload(false, 7, "ABCDEFGHIJKLMNOPQRST", "");
		Dump("connect_status_truncated", p);
		CheckEq(Hex(p), "0007" "4142434445464748494a4b4c4d4e4f5000" "00",
			"CONNECT_STATUS app name truncated to 16");
	}

	// ---- full packets, one per command type --------------------------------
	// The payload dumps above prove the payload layouts, but they say nothing
	// about the command_type byte each one is actually wrapped with. These wrap
	// every command through BuildPacket so the diff against the C# reference
	// covers kCmdPlay/Stop/StopAll/Ping/Pong/ConnectStatus themselves — a
	// transposed constant (e.g. STOP 0x02 <-> STOP_ALL 0x03) fails here instead
	// of only being caught by eye.
	{
		const Bytes Play = BuildPacket(kCmdPlay, 1, BuildPlayPayload("k.e", 0, 1.0f, ""));
		const Bytes Stop = BuildPacket(kCmdStop, 2, BuildStopPayload("k.e", ""));
		const Bytes StopAll = BuildPacket(kCmdStopAll, 3, BuildStopAllPayload("player_1"));
		const Bytes Ping = BuildPacket(kCmdPing, 4, BuildPingPayload(1LL));
		const Bytes Pong = BuildPacket(kCmdPong, 5, Bytes(16, 0));
		const Bytes Status = BuildPacket(kCmdConnectStatus, 6,
			BuildConnectStatusPayload(true, 0, "RoboRecall", "PC-1"));

		Dump("packet_play", Play);
		Dump("packet_stop", Stop);
		Dump("packet_stop_all", StopAll);
		Dump("packet_ping", Ping);
		Dump("packet_pong", Pong);
		Dump("packet_connect_status", Status);

		// message-format.md §4 command_type values, at header offset 3.
		Check(Play[3] == 0x01, "PLAY command_type = 0x01");
		Check(Stop[3] == 0x02, "STOP command_type = 0x02");
		Check(StopAll[3] == 0x03, "STOP_ALL command_type = 0x03");
		Check(Ping[3] == 0x10, "PING command_type = 0x10");
		Check(Pong[3] == 0x11, "PONG command_type = 0x11");
		Check(Status[3] == 0x20, "CONNECT_STATUS command_type = 0x20");
	}

	// ---- packet parse ------------------------------------------------------
	{
		Bytes payload;
		payload.push_back(0x01);
		const Bytes packet = BuildPacket(kCmdPong, 9, payload);
		ParsedPacket out;
		Check(ParsePacket(&packet[0], (int)packet.size(), out), "ParsePacket accepts a valid packet");
		Check(out.Cmd == kCmdPong && out.Seq == 9 && out.Payload.size() == 1, "ParsePacket fields");

		Bytes bad = packet;
		bad[0] = 0x00; // wrong magic
		ParsedPacket ignored;
		Check(!ParsePacket(&bad[0], (int)bad.size(), ignored), "ParsePacket rejects bad magic");

		Bytes truncated(packet.begin(), packet.begin() + 8); // header claims 1 payload byte
		Check(!ParsePacket(&truncated[0], (int)truncated.size(), ignored), "ParsePacket rejects truncation");
	}

	// ---- PONG parse --------------------------------------------------------
	{
		// Legacy 16-byte payload: base fields only, extension absent.
		Bytes legacy(16, 0);
		legacy[0] = 0x2A; // timestamp = 42
		PongInfo info;
		Check(ParsePong(legacy, info), "ParsePong accepts a legacy 16-byte payload");
		Check(info.Timestamp == 42, "ParsePong timestamp");
		Check(!info.bHasAddress, "legacy PONG reports no address (caller must fail open)");

		Bytes full(16, 0);
		AppendCString(full, "dev1");
		AppendCString(full, "player_1/pos_chest");
		AppendCString(full, "0.3.1");
		full.push_back(64);  // volume_level
		full.push_back(100); // volume_wiper
		PongInfo ext;
		Check(ParsePong(full, ext), "ParsePong accepts an extended payload");
		CheckEq(ext.DeviceName, "dev1", "PONG device_name");
		CheckEq(ext.Address, "player_1/pos_chest", "PONG address");
		CheckEq(ext.Firmware, "0.3.1", "PONG firmware_version");
		Check(ext.bHasAddress, "extended PONG reports an address");
		Check(ext.VolumeLevel == 64 && ext.VolumeWiper == 100, "PONG volume fields");
	}

	// ---- AddressMatches ----------------------------------------------------
	// The FULL case list of the C# reference suite (see kMatchCases above), not a
	// subset: group-segment matching, prefix + wildcard combinations and the
	// trailing-slash walk are all exercised against the C++ port.
	{
		for (size_t i = 0; i < kMatchCaseCount; ++i)
		{
			const FMatchCase& C = kMatchCases[i];
			Check(AddressMatches(C.Target, C.Address) == C.bExpected,
				std::string("AddressMatches(\"") + C.Target + "\", \"" + C.Address + "\"): " + C.Reason);
		}
	}

	// ---- NormalizeOverride / ResolveTarget ---------------------------------
	{
		Check(NormalizeOverride(0) == -1 && NormalizeOverride(100) == -1, "override range low/high");
		Check(NormalizeOverride(1) == 1 && NormalizeOverride(99) == 99, "override range bounds");

		CheckEq(ResolveTarget("player_5/pos_chest", -1, -1), "player_5/pos_chest",
			"both overrides disabled = passthrough");
		CheckEq(ResolveTarget("", 2, -1), "player_2", "player override on an empty target");
		CheckEq(ResolveTarget("player_5/pos_chest", 2, -1), "player_2/pos_chest",
			"player override replaces an existing player segment");
		CheckEq(ResolveTarget("*/pos_chest", 2, -1), "player_2/pos_chest",
			"player override replaces the placeholder before position");
		CheckEq(ResolveTarget("", -1, 3), "*/*/group_3", "group override pads player and position slots");
		CheckEq(ResolveTarget("player_1/pos_chest", -1, 3), "player_1/pos_chest/group_3",
			"group lands right after position");
		CheckEq(ResolveTarget("player_1/pos_chest/group_9", -1, 3), "player_1/pos_chest/group_3",
			"group override replaces an existing group segment");
		CheckEq(ResolveTarget("", 2, 3), "player_2/*/group_3", "both overrides on an empty target");
		CheckEq(ResolveTarget("red", -1, 3), "red/*/*/group_3", "free prefix is preserved ahead of the slots");
	}

	// ---- addressing dump (diffed against the C# implementation) -------------
	// Same case list, same order, same line format as Tools/csharp-parity.
	{
		std::cout << "\n"; // separator, kept identical in both dumps

		for (size_t i = 0; i < kMatchCaseCount; ++i)
		{
			std::cout << "match[" << kMatchCases[i].Target << "|" << kMatchCases[i].Address << "]="
					  << (AddressMatches(kMatchCases[i].Target, kMatchCases[i].Address) ? 1 : 0) << "\n";
		}

		struct FResolveCase { const char* Target; int Player; int Group; };
		const FResolveCase kResolveCases[] = {
			{ "player_5/pos_chest", -1, -1 },
			{ "", 2, -1 },
			{ "player_5/pos_chest", 2, -1 },
			{ "*/pos_chest", 2, -1 },
			{ "", -1, 3 },
			{ "player_1/pos_chest", -1, 3 },
			{ "player_1/pos_chest/group_9", -1, 3 },
			{ "", 2, 3 },
			{ "red", -1, 3 },
			{ "*", 2, 3 },
			{ "red/player_1", 4, 5 },
		};
		for (size_t i = 0; i < sizeof(kResolveCases) / sizeof(kResolveCases[0]); ++i)
		{
			std::cout << "resolve[" << kResolveCases[i].Target << "|" << kResolveCases[i].Player
					  << "|" << kResolveCases[i].Group << "]="
					  << ResolveTarget(kResolveCases[i].Target, kResolveCases[i].Player,
							 kResolveCases[i].Group) << "\n";
		}
	}

	if (gFailures > 0)
	{
		std::cerr << "\n" << gFailures << " check(s) FAILED\n";
		return 1;
	}
	std::cerr << "\nall checks passed\n";
	return 0;
}
