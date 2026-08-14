using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Hapbeat.ModCore;

namespace Hapbeat.ModCore.Tests
{
    /// <summary>
    /// Plain console test runner for HapbeatModCore — no xunit/NUnit dependency, so it
    /// runs anywhere `dotnet run` works. Exits non-zero if any assertion fails.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static readonly List<string> _failures = new List<string>();

        private static int Main()
        {
            HeaderTests();
            PlayPayloadTests();
            StopPayloadTests();
            StopAllPayloadTests();
            PingPayloadTests();
            ConnectStatusPayloadTests();
            PongParseTests();
            AddressMatchesTests();
            ResolveTargetTests();
            NormalizeOverrideTests();
            SettingsTests();
            RateLimitTests();

            Console.WriteLine();
            Console.WriteLine("PASS: " + _passed + "   FAIL: " + _failures.Count);
            if (_failures.Count > 0)
            {
                Console.WriteLine();
                foreach (string f in _failures)
                    Console.WriteLine("  FAILED: " + f);
                return 1;
            }
            Console.WriteLine("All tests passed.");
            return 0;
        }

        #region Assertions

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            _failures.Add(what);
        }

        private static void AreEqual(object expected, object actual, string what)
        {
            bool ok = expected == null ? actual == null : expected.Equals(actual);
            if (ok) { _passed++; return; }
            _failures.Add(what + " (expected <" + Describe(expected) + ">, got <" + Describe(actual) + ">)");
        }

        private static void BytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (expected.Length != actual.Length)
            {
                _failures.Add(what + " (length: expected " + expected.Length + ", got " + actual.Length +
                              "; actual=" + Hex(actual) + ")");
                return;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    _failures.Add(what + " (byte " + i + ": expected 0x" + expected[i].ToString("X2") +
                                  ", got 0x" + actual[i].ToString("X2") + "; actual=" + Hex(actual) + ")");
                    return;
                }
            }
            _passed++;
        }

        private static void Throws<T>(Action action, string what) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                _passed++;
                return;
            }
            catch (Exception ex)
            {
                _failures.Add(what + " (threw " + ex.GetType().Name + " instead of " + typeof(T).Name + ")");
                return;
            }
            _failures.Add(what + " (did not throw)");
        }

        private static string Describe(object value)
        {
            return value == null ? "null" : value.ToString();
        }

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static void Section(string name)
        {
            Console.WriteLine("== " + name);
        }

        #endregion

        #region Byte-layout helpers (independent of HapbeatProtocol's own writers)

        private static byte[] U16(ushort v) { return new byte[] { (byte)(v & 0xFF), (byte)(v >> 8) }; }

        private static byte[] I64(long v)
        {
            byte[] b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)((v >> (i * 8)) & 0xFF);
            return b;
        }

        private static byte[] F32(float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        private static byte[] Cat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] p in parts) total += p.Length;
            byte[] result = new byte[total];
            int offset = 0;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }
            return result;
        }

        /// <summary>Null-terminated UTF-8 string, as every string field on the wire is.</summary>
        private static byte[] Str(string s)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s ?? "");
            byte[] result = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, result, 0, utf8.Length);
            return result;
        }

        #endregion

        #region Protocol tests

        // message-format.md §3: magic(2) + version(1) + command_type(1) + seq(2) + payload_length(2)
        private static void HeaderTests()
        {
            Section("packet header (message-format.md §3)");

            byte[] payload = new byte[] { 0xAA, 0xBB, 0xCC };
            byte[] packet = HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PLAY, 0x1234, payload);

            AreEqual(11, packet.Length, "header(8) + payload(3)");
            AreEqual((byte)0x42, packet[0], "offset 0: magic low byte ('B' of \"HB\" LE)");
            AreEqual((byte)0x48, packet[1], "offset 1: magic high byte");
            AreEqual((byte)0x01, packet[2], "offset 2: protocol_version");
            AreEqual((byte)0x01, packet[3], "offset 3: command_type = PLAY");
            AreEqual((byte)0x34, packet[4], "offset 4: seq low byte (LE)");
            AreEqual((byte)0x12, packet[5], "offset 5: seq high byte");
            AreEqual((byte)0x03, packet[6], "offset 6: payload_length low byte");
            AreEqual((byte)0x00, packet[7], "offset 7: payload_length high byte");
            AreEqual((byte)0xAA, packet[8], "offset 8: payload starts");

            AreEqual(8, HapbeatProtocol.HEADER_SIZE, "HEADER_SIZE is 8");
            AreEqual((ushort)0x4842, HapbeatProtocol.MAGIC, "MAGIC is 0x4842");
            AreEqual(512, HapbeatProtocol.MAX_PACKET_SIZE, "command packets cap at 512 bytes (§10)");

            // Round-trip through the parser.
            byte[] parsedPayload;
            byte cmd;
            ushort seq;
            HapbeatProtocol.ParsePacket(packet, out cmd, out seq, out parsedPayload);
            AreEqual(HapbeatProtocol.CMD_PLAY, cmd, "round-trip command_type");
            AreEqual((ushort)0x1234, seq, "round-trip seq");
            BytesEqual(payload, parsedPayload, "round-trip payload");

            // Command type constants (§4).
            AreEqual((byte)0x01, HapbeatProtocol.CMD_PLAY, "CMD_PLAY = 0x01");
            AreEqual((byte)0x02, HapbeatProtocol.CMD_STOP, "CMD_STOP = 0x02");
            AreEqual((byte)0x03, HapbeatProtocol.CMD_STOP_ALL, "CMD_STOP_ALL = 0x03");
            AreEqual((byte)0x10, HapbeatProtocol.CMD_PING, "CMD_PING = 0x10");
            AreEqual((byte)0x11, HapbeatProtocol.CMD_PONG, "CMD_PONG = 0x11");
            AreEqual((byte)0x20, HapbeatProtocol.CMD_CONNECT_STATUS, "CMD_CONNECT_STATUS = 0x20");

            // Malformed packets must be rejected, not silently misread.
            byte[] badMagic = (byte[])packet.Clone();
            badMagic[0] = 0x00;
            Throws<ArgumentException>(() =>
            {
                byte c; ushort s; byte[] p;
                HapbeatProtocol.ParsePacket(badMagic, out c, out s, out p);
            }, "bad magic rejected");

            byte[] badVersion = (byte[])packet.Clone();
            badVersion[2] = 0x02;
            Throws<ArgumentException>(() =>
            {
                byte c; ushort s; byte[] p;
                HapbeatProtocol.ParsePacket(badVersion, out c, out s, out p);
            }, "unsupported version rejected");

            byte[] truncated = new byte[9];
            Buffer.BlockCopy(packet, 0, truncated, 0, 9); // claims 3 payload bytes, carries 1
            Throws<ArgumentException>(() =>
            {
                byte c; ushort s; byte[] p;
                HapbeatProtocol.ParsePacket(truncated, out c, out s, out p);
            }, "truncated payload rejected");

            Throws<ArgumentException>(
                () => HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PLAY, 0, new byte[600]),
                "over-size command packet rejected");
        }

        // §0x01: event_id(null-term) + target(null-term) + target_time(int64) + gain(float32)
        private static void PlayPayloadTests()
        {
            Section("PLAY payload (message-format.md §0x01 / device-addressing.md §5.1)");

            byte[] actual = HapbeatProtocol.BuildPlayPayload(
                "vr-shooter-kit.shot_recoil", 0, 1.0f, "player_1/pos_neck");
            byte[] expected = Cat(
                Str("vr-shooter-kit.shot_recoil"),
                Str("player_1/pos_neck"),
                I64(0),
                F32(1.0f));
            BytesEqual(expected, actual, "PLAY field order and encoding");

            // Field offsets, checked individually so a layout regression names the field.
            int idLen = Encoding.UTF8.GetByteCount("vr-shooter-kit.shot_recoil");
            int targetLen = Encoding.UTF8.GetByteCount("player_1/pos_neck");
            AreEqual((byte)0, actual[idLen], "event_id null terminator");
            AreEqual((byte)0, actual[idLen + 1 + targetLen], "target null terminator");
            AreEqual(idLen + 1 + targetLen + 1 + 8 + 4, actual.Length, "PLAY payload total size");

            // Non-trivial time/gain, to catch endianness or float-encoding slips.
            byte[] tuned = HapbeatProtocol.BuildPlayPayload("k.e", 0x0102030405060708L, 0.5f, "");
            BytesEqual(Cat(Str("k.e"), Str(""), I64(0x0102030405060708L), F32(0.5f)), tuned,
                "PLAY int64 target_time is little-endian; gain is float32");

            // null target == "" (broadcast): the empty string still writes its terminator.
            BytesEqual(HapbeatProtocol.BuildPlayPayload("k.e", 0, 1f, ""),
                       HapbeatProtocol.BuildPlayPayload("k.e", 0, 1f, null),
                       "null target encodes identically to empty (broadcast)");
        }

        // §0x02: event_id(null-term) + target(null-term)
        private static void StopPayloadTests()
        {
            Section("STOP payload (message-format.md §0x02)");
            byte[] actual = HapbeatProtocol.BuildStopPayload("vr-shooter-kit.heartbeat", "player_2");
            BytesEqual(Cat(Str("vr-shooter-kit.heartbeat"), Str("player_2")), actual, "STOP field order");
            BytesEqual(Cat(Str("e"), Str("")), HapbeatProtocol.BuildStopPayload("e", null),
                "STOP with broadcast target");
        }

        // §0x03: target(null-term)
        private static void StopAllPayloadTests()
        {
            Section("STOP_ALL payload (message-format.md §0x03)");
            BytesEqual(Str("player_1"), HapbeatProtocol.BuildStopAllPayload("player_1"), "STOP_ALL target only");
            BytesEqual(new byte[] { 0 }, HapbeatProtocol.BuildStopAllPayload(null),
                "STOP_ALL broadcast is a lone null terminator");
        }

        // §0x10: timestamp(int64)
        private static void PingPayloadTests()
        {
            Section("PING payload (message-format.md §0x10)");
            byte[] actual = HapbeatProtocol.BuildPingPayload(0x1122334455667788L);
            AreEqual(8, actual.Length, "PING payload is exactly 8 bytes");
            BytesEqual(I64(0x1122334455667788L), actual, "PING timestamp is int64 LE");
        }

        // §0x20: connected(uint8) + group(uint8) + app_name(null-term) + device_name(null-term)
        private static void ConnectStatusPayloadTests()
        {
            Section("CONNECT_STATUS payload (message-format.md §0x20)");

            byte[] actual = HapbeatProtocol.BuildConnectStatusPayload(true, 3, "PistolWhip", "GAMING-PC");
            BytesEqual(Cat(new byte[] { 1, 3 }, Str("PistolWhip"), Str("GAMING-PC")), actual,
                "CONNECT_STATUS field order (connected, group, app_name, device_name)");
            AreEqual((byte)1, actual[0], "offset 0: connected");
            AreEqual((byte)3, actual[1], "offset 1: group");

            byte[] disconnect = HapbeatProtocol.BuildConnectStatusPayload(false, 0, "A", "B");
            AreEqual((byte)0, disconnect[0], "connected=false encodes as 0");

            // DEC-029: app name is capped at the 16-column display width.
            AreEqual(16, HapbeatProtocol.MaxAppNameLength, "MaxAppNameLength is 16 (DEC-029)");
            byte[] longName = HapbeatProtocol.BuildConnectStatusPayload(
                true, 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "");
            BytesEqual(Cat(new byte[] { 1, 0 }, Str("ABCDEFGHIJKLMNOP"), Str("")), longName,
                "app_name truncated to 16 chars");
        }

        private static void PongParseTests()
        {
            Section("PONG parsing (message-format.md §0x11)");

            byte[] basePayload = Cat(I64(111), I64(222));
            long ts, srv;
            HapbeatProtocol.ParsePong(basePayload, out ts, out srv);
            AreEqual(111L, ts, "PONG timestamp");
            AreEqual(222L, srv, "PONG server_time");

            Throws<ArgumentException>(() =>
            {
                long a, b;
                HapbeatProtocol.ParsePong(new byte[8], out a, out b);
            }, "short PONG rejected");

            // Device extension: device_name, address, firmware_version, volume, wiper.
            byte[] extended = Cat(I64(1), I64(2), Str("DUO-1234"), Str("player_1/pos_neck"), Str("0.3.1"),
                new byte[] { 12, 34 });
            string name, addr, fw;
            int vol, wiper;
            HapbeatProtocol.ParsePongExtended(extended, out ts, out srv, out name, out addr, out fw,
                out vol, out wiper);
            AreEqual("DUO-1234", name, "extended PONG device_name");
            AreEqual("player_1/pos_neck", addr, "extended PONG address");
            AreEqual("0.3.1", fw, "extended PONG firmware_version");
            AreEqual(12, vol, "extended PONG volume_level");
            AreEqual(34, wiper, "extended PONG volume_wiper");

            // Legacy 16-byte PONG: extension fields come back null/-1, not an exception —
            // the unicast routing depends on this to fail open.
            HapbeatProtocol.ParsePongExtended(basePayload, out ts, out srv, out name, out addr, out fw,
                out vol, out wiper);
            AreEqual(null, addr, "legacy PONG yields null address (unknown => fail open)");
            AreEqual(-1, vol, "legacy PONG yields volume_level -1");
        }

        #endregion

        #region Addressing tests (ported from unity-sdk Tests/Runtime)

        private static void AddressMatchesTests()
        {
            Section("AddressMatches (device-addressing.md §4.2/§4.3)");

            // Transcribed from unity-sdk Tests/Runtime/AddressMatchesTests.cs.
            MatchCase("", "player_1/pos_neck", true, "empty target = all devices");
            MatchCase("player_1", "player_1/pos_neck", true, "front-match");
            MatchCase("player_1", "player_2/pos_neck", false, "player mismatch");
            MatchCase("player_1/pos_neck", "player_1/pos_neck", true, "exact match");
            MatchCase("player_1/pos_neck", "player_1/pos_r_wrist", false, "position mismatch");
            MatchCase("*/pos_neck", "player_1/pos_neck", true, "wildcard player");
            MatchCase("*/pos_neck", "player_2/pos_neck", true, "wildcard player, different player");
            // Firmware only treats a segment that is *entirely* "*" as a wildcard
            // (address_match.cpp: `t_len == 1 && *tp == '*'`), so "pos_*" is literal.
            MatchCase("player_1/pos_*", "player_1/pos_neck", false, "partial wildcard is literal");
            MatchCase("player_1/*", "player_1/pos_neck", true, "whole-segment wildcard covers any position");
            MatchCase("red", "red/player_1/pos_neck", true, "prefix front-match");
            MatchCase("red/*/player_1", "red/alpha/player_1/pos_neck", true, "wildcard + front-match");
            MatchCase("player_1/pos_neck/group_1", "player_1/pos_neck/group_1", true, "group exact match");
            MatchCase("player_1/pos_neck/group_1", "player_1/pos_neck/group_2", false, "group mismatch");
            MatchCase("player_1/pos_neck", "player_1/pos_neck/group_1", true, "group omitted = all groups");
            MatchCase("*/*/group_1", "player_2/pos_chest/group_1", true, "player/position wildcard, group pinned");
            MatchCase("player_1/pos_neck/group_1", "player_1/pos_neck", false, "target longer than address");

            // Null handling.
            MatchCase(null, "player_1/pos_neck/group_1", true, "null target matches everything");
            MatchCase("player_1", null, false, "null address behaves like empty");
            MatchCase("", null, true, "empty target still matches a null address");

            // Trailing-slash cases pinned to firmware's pointer walk.
            MatchCase("player_1/", "player_1/pos_neck/group_1", true, "trailing slash = 'player_1'");
            MatchCase("player_1/pos_neck/", "player_1/pos_neck/group_1", true, "trailing slash after position");
            MatchCase("player_1/", "player_1", true, "trailing slash, exact-length address");
            MatchCase("player_2/", "player_1/pos_neck/group_1", false, "trailing slash keeps a mismatch");
            MatchCase("player_1//", "player_1/pos_neck", false, "double slash: one real empty segment remains");
            MatchCase("/", "player_1/pos_neck", false, "bare slash = one empty segment");

            // Group separation.
            MatchCase("*/*/group_2", "player_1/pos_chest/group_1", false, "group-only target, different group");
            MatchCase("*/*/group_2", "player_1/pos_chest/group_2", true, "group-only target, matching group");
        }

        private static void MatchCase(string target, string address, bool expected, string reason)
        {
            AreEqual(expected, HapbeatModClient.AddressMatches(target, address),
                "AddressMatches(\"" + (target ?? "null") + "\", \"" + (address ?? "null") + "\"): " + reason);
        }

        private static void ResolveTargetTests()
        {
            Section("ResolveTarget (address override)");

            // Transcribed from unity-sdk Tests/Runtime/ResolveTargetTests.cs.
            // player=3, group disabled.
            ResolveCase("", 3, -1, "player_3");
            ResolveCase("player_1", 3, -1, "player_3");
            ResolveCase("player_1/pos_chest", 3, -1, "player_3/pos_chest");
            ResolveCase("*/pos_neck", 3, -1, "player_3/pos_neck");
            ResolveCase("player_1/pos_chest/group_5", 3, -1, "player_3/pos_chest/group_5");
            ResolveCase("red/player_1/pos_neck", 3, -1, "red/player_3/pos_neck");
            ResolveCase(null, 3, -1, "player_3"); // null handled like "" once an override is active

            // group=7, player disabled. group_ must land in its positional slot.
            ResolveCase("", -1, 7, "*/*/group_7");
            ResolveCase("*", -1, 7, "*/*/group_7");
            ResolveCase("player_3", -1, 7, "player_3/*/group_7");
            ResolveCase("player_1/pos_chest", -1, 7, "player_1/pos_chest/group_7");
            ResolveCase("player_1/pos_chest/group_5", -1, 7, "player_1/pos_chest/group_7");
            ResolveCase("red/player_1/pos_neck", -1, 7, "red/player_1/pos_neck/group_7");
            ResolveCase("red", -1, 7, "red/*/*/group_7");

            // Both active.
            ResolveCase("player_1/pos_chest/group_5", 3, 7, "player_3/pos_chest/group_7");
            ResolveCase("", 3, 7, "player_3/*/group_7");
            ResolveCase("player_1", 3, 7, "player_3/*/group_7");
            ResolveCase("*", 3, 7, "player_3/*/group_7"); // must not pad a second placeholder

            // Both disabled: byte-for-byte passthrough, including null.
            ResolveCase("", -1, -1, "");
            ResolveCase("player_1/pos_chest", -1, -1, "player_1/pos_chest");
            ResolveCase("player_1/pos_chest/group_5", -1, -1, "player_1/pos_chest/group_5");
            ResolveCase("red/player_1/pos_neck", -1, -1, "red/player_1/pos_neck");
            ResolveCase(null, -1, -1, null);

            // Outside the documented grammar; the only contract is "does not throw".
            try
            {
                HapbeatModClient.ResolveTarget("*/*/group_1", 3, -1);
                HapbeatModClient.ResolveTarget("*/*/group_1", -1, 7);
                HapbeatModClient.ResolveTarget("*/*/group_1", 3, 7);
                _passed++;
            }
            catch (Exception ex)
            {
                _failures.Add("ResolveTarget multi-wildcard threw " + ex.GetType().Name);
            }
        }

        private static void ResolveCase(string input, int player, int group, string expected)
        {
            AreEqual(expected, HapbeatModClient.ResolveTarget(input, player, group),
                "ResolveTarget(\"" + (input ?? "null") + "\", player=" + player + ", group=" + group + ")");
        }

        private static void NormalizeOverrideTests()
        {
            Section("NormalizeOverride (device-addressing 1..99)");
            AreEqual(-1, HapbeatModClient.NormalizeOverride(-1), "-1 stays disabled");
            AreEqual(-1, HapbeatModClient.NormalizeOverride(0), "0 is out of range");
            AreEqual(1, HapbeatModClient.NormalizeOverride(1), "1 is valid");
            AreEqual(50, HapbeatModClient.NormalizeOverride(50), "50 is valid");
            AreEqual(99, HapbeatModClient.NormalizeOverride(99), "99 is valid");
            AreEqual(-1, HapbeatModClient.NormalizeOverride(100), "100 is out of range");
            AreEqual(-1, HapbeatModClient.NormalizeOverride(int.MinValue), "int.MinValue is out of range");
            AreEqual(-1, HapbeatModClient.NormalizeOverride(int.MaxValue), "int.MaxValue is out of range");
        }

        #endregion

        #region Settings tests

        private static void SettingsTests()
        {
            Section("HapbeatModSettings (JSON)");

            // Defaults.
            HapbeatModSettings defaults = HapbeatModSettings.CreateDefault("PistolWhip");
            AreEqual("PistolWhip", defaults.AppName, "default appName comes from the caller");
            AreEqual(-1, defaults.Group, "default group is disabled");
            AreEqual(-1, defaults.Player, "default player is disabled");
            AreEqual(1.0f, defaults.MasterGain, "default masterGain");
            AreEqual(60, defaults.MinIntervalMs, "default minIntervalMs");
            Check(defaults.GetEvent("shot") != null, "default event map contains 'shot'");
            AreEqual("vr-shooter-kit.shot_recoil", defaults.GetEvent("shot").EventId, "default shot event id");
            AreEqual(null, defaults.GetEvent("nope"), "unknown logical event returns null");

            // Round-trip through serialize + parse.
            defaults.Group = 4;
            defaults.Player = 2;
            defaults.MasterGain = 0.75f;
            defaults.MinIntervalMs = 120;
            defaults.Events["shot"].Gain = 0.6f;
            defaults.Events["beat"].Enabled = false;

            HapbeatModSettings round = HapbeatModSettings.Parse(defaults.ToJson(),
                HapbeatModSettings.CreateDefault("other"));
            AreEqual("PistolWhip", round.AppName, "round-trip appName");
            AreEqual(4, round.Group, "round-trip group");
            AreEqual(2, round.Player, "round-trip player");
            AreEqual(0.75f, round.MasterGain, "round-trip masterGain");
            AreEqual(120, round.MinIntervalMs, "round-trip minIntervalMs");
            AreEqual(defaults.Events.Count, round.Events.Count, "round-trip event count");
            AreEqual(0.6f, round.GetEvent("shot").Gain, "round-trip per-event gain");
            AreEqual(false, round.GetEvent("beat").Enabled, "round-trip per-event enabled");
            AreEqual("vr-shooter-kit.heartbeat", round.GetEvent("heartbeat").EventId, "round-trip event id");

            // Partial document: absent fields keep their default value.
            HapbeatModSettings partial = HapbeatModSettings.Parse(
                "{ \"masterGain\": 0.25 }", HapbeatModSettings.CreateDefault("Robo"));
            AreEqual("Robo", partial.AppName, "absent appName keeps the default");
            AreEqual(0.25f, partial.MasterGain, "present masterGain overrides the default");
            Check(partial.GetEvent("shot") != null, "absent events object keeps the default map");

            // An explicit events object replaces the map (so a user can delete a binding).
            HapbeatModSettings replaced = HapbeatModSettings.Parse(
                "{ \"events\": { \"shot\": { \"eventId\": \"k.a\", \"gain\": 0.4, \"enabled\": false } } }",
                HapbeatModSettings.CreateDefault());
            AreEqual(1, replaced.Events.Count, "explicit events object replaces the default map");
            AreEqual("k.a", replaced.GetEvent("shot").EventId, "replaced event id");
            AreEqual(0.4f, replaced.GetEvent("shot").Gain, "replaced event gain");
            AreEqual(false, replaced.GetEvent("shot").Enabled, "replaced event enabled");

            // Escapes and unicode survive the hand-rolled reader/writer.
            HapbeatModSettings escaped = HapbeatModSettings.Parse(
                "{ \"appName\": \"a\\\"b\\\\c\\u0041\" }", HapbeatModSettings.CreateDefault());
            AreEqual("a\"b\\cA", escaped.AppName, "string escapes are decoded");
            AreEqual("a\"b\\cA",
                HapbeatModSettings.Parse(escaped.ToJson(), HapbeatModSettings.CreateDefault()).AppName,
                "string escapes survive re-serialization");

            // Malformed JSON is a hard parse error...
            Throws<FormatException>(() => HapbeatModSettings.Parse("{ \"appName\": ", null),
                "truncated JSON rejected");
            Throws<FormatException>(() => HapbeatModSettings.Parse("not json at all", null),
                "non-JSON rejected");
            Throws<FormatException>(() => HapbeatModSettings.Parse("[1,2,3]", null),
                "non-object root rejected");

            // ...but LoadOrCreate turns it into "defaults + warning", never a crash on
            // the mod's startup path.
            string dir = Path.Combine(Path.GetTempPath(), "hapbeat-modcore-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "hapbeat_settings.json");

                var warnings = new List<string>();
                HapbeatModSettings created = HapbeatModSettings.LoadOrCreate(path, "BladeSorcery", warnings.Add);
                Check(File.Exists(path), "LoadOrCreate writes the file when it is missing");
                AreEqual("BladeSorcery", created.AppName, "created settings use the supplied app name");
                Check(warnings.Count == 1, "LoadOrCreate logs once when it creates the file");

                created.MasterGain = 0.5f;
                created.Save(path);
                HapbeatModSettings reloaded = HapbeatModSettings.LoadOrCreate(path, "BladeSorcery", null);
                AreEqual(0.5f, reloaded.MasterGain, "LoadOrCreate reads back a saved file");

                File.WriteAllText(path, "{ this is broken");
                warnings.Clear();
                HapbeatModSettings recovered = HapbeatModSettings.LoadOrCreate(path, "BladeSorcery", warnings.Add);
                AreEqual(1.0f, recovered.MasterGain, "corrupt file falls back to defaults");
                Check(warnings.Count == 1, "corrupt file logs a warning");
                AreEqual("{ this is broken", File.ReadAllText(path),
                    "corrupt file is left on disk for the user to fix");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }

        #endregion

        #region Rate limiting

        private static void RateLimitTests()
        {
            Section("Fire rate limiting");

            var settings = HapbeatModSettings.CreateDefault("Test");
            settings.MinIntervalMs = 150;
            settings.Events["off"] = new HapbeatEventSetting("k.off", 1.0f, false);

            // No socket is opened: Fire's return value reports the rate-limit decision,
            // and the send itself no-ops while disconnected.
            using (var client = new HapbeatModClient(settings))
            {
                Check(client.Fire("shot"), "first fire passes");
                Check(!client.Fire("shot"), "immediate re-fire of the same event is suppressed");
                Check(client.Fire("hit"), "a different logical event is limited independently");

                Thread.Sleep(200);
                Check(client.Fire("shot"), "fire passes again after the interval elapses");

                Check(!client.Fire("off"), "a disabled event never fires");
                Check(!client.Fire("does_not_exist"), "an unknown event never fires");
                Check(!client.Fire(null), "null logical event never fires");

                AreEqual(0, client.AliveDeviceCount, "no devices are alive without a connection");

                // minIntervalMs = 0 disables limiting entirely.
                settings.MinIntervalMs = 0;
                Check(client.Fire("reload"), "unlimited: first fire");
                Check(client.Fire("reload"), "unlimited: immediate re-fire also passes");
            }

            HapticDelay();
        }

        /// <summary>
        /// hapticDelayMs holds PLAYs back so they land with wirelessly-streamed picture
        /// and sound instead of ahead of it (Air Link and friends add 40–60 ms).
        /// </summary>
        private static void HapticDelay()
        {
            Section("Haptic delay (hapticDelayMs)");

            // Round-trips through JSON like every other setting.
            var written = HapbeatModSettings.CreateDefault("Test");
            written.HapticDelayMs = 45;
            var read = HapbeatModSettings.Parse(written.ToJson(), HapbeatModSettings.CreateDefault("Test"));
            AreEqual(45, read.HapticDelayMs, "hapticDelayMs survives a save/load round trip");
            AreEqual(0, HapbeatModSettings.CreateDefault("Test").HapticDelayMs,
                "the delay is off by default (wired setups need no compensation)");

            var settings = HapbeatModSettings.CreateDefault("Test");
            settings.MinIntervalMs = 0;
            settings.HapticDelayMs = 400;

            // No socket: the queue is still filled, but nothing drains it, so the queued
            // entry stays observable. That is exactly what the stop path has to cancel.
            using (var client = new HapbeatModClient(settings))
            {
                AreEqual(0, client.PendingDelayedCount, "nothing is queued before the first fire");

                Check(client.Fire("heartbeat"), "a delayed fire still reports success");
                AreEqual(1, client.PendingDelayedCount, "the play is queued rather than sent immediately");

                Check(client.FireStop("heartbeat"), "stop succeeds while a play is still queued");
                AreEqual(0, client.PendingDelayedCount,
                    "stop cancels the queued play (otherwise a looping clip would start after its stop)");

                Check(client.Fire("shot"), "a second event queues independently");
                Check(client.Fire("hit"), "and so does a third");
                AreEqual(2, client.PendingDelayedCount, "both remain queued");

                client.FireStop("shot");
                AreEqual(1, client.PendingDelayedCount, "stop only cancels its own clip");
            }

            // With the delay off, Fire must send straight through — nothing may linger.
            settings.HapticDelayMs = 0;
            using (var client = new HapbeatModClient(settings))
            {
                Check(client.Fire("shot"), "fires with the delay disabled");
                AreEqual(0, client.PendingDelayedCount, "no queue is used when the delay is off");
            }

            // With a socket open the worker drains the queue once the delay elapses.
            // Skipped rather than failed where a UDP broadcast socket cannot be opened.
            var live = HapbeatModSettings.CreateDefault("Test");
            live.MinIntervalMs = 0;
            live.HapticDelayMs = 120;
            try
            {
                using (var client = new HapbeatModClient(live))
                {
                    client.OpenBroadcast();
                    client.Fire("shot");
                    AreEqual(1, client.PendingDelayedCount, "queued while the delay has not elapsed");

                    Thread.Sleep(400);
                    AreEqual(0, client.PendingDelayedCount, "the worker sends it once the delay elapses");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  (skipped live-socket drain check: " + ex.Message + ")");
            }
        }

        #endregion
    }
}
