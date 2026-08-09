// Prints the same case dump as Tools/HapbeatWireSelfTest.cpp, but from the
// authoritative C# implementation (mods/shared/HapbeatModCore). Diff the two
// outputs to prove the C++ port is byte-identical:
//
//   dotnet run --project Tools/csharp-parity > cs.txt
//   ./Tools/wiretest > cpp.txt 2>/dev/null
//   diff cs.txt cpp.txt

using System;
using System.Text;
using Hapbeat.ModCore;

internal static class Program
{
    private static string Hex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    private static void Dump(string name, byte[] b) => Console.WriteLine(name + "=" + Hex(b));

    private static void Main()
    {
        Dump("header", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PLAY, 0x0102,
            new byte[] { 0xAA, 0xBB }));

        Dump("play_payload",
            HapbeatProtocol.BuildPlayPayload("vr-shooter-kit.shot_recoil", 0, 1.0f, ""));
        Dump("play_payload_targeted",
            HapbeatProtocol.BuildPlayPayload("k.e", 1234567890123L, 0.5f, "player_2/*/group_3"));

        Dump("stop_payload", HapbeatProtocol.BuildStopPayload("k.e", ""));
        Dump("stop_all_payload", HapbeatProtocol.BuildStopAllPayload("player_1"));
        Dump("ping_payload", HapbeatProtocol.BuildPingPayload(1L));

        Dump("connect_status",
            HapbeatProtocol.BuildConnectStatusPayload(true, 0, "RoboRecall", "PC-1"));
        Dump("connect_status_truncated",
            HapbeatProtocol.BuildConnectStatusPayload(false, 7, "ABCDEFGHIJKLMNOPQRST", ""));

        // Whole packets, so the diff covers the command_type byte of every command
        // rather than only the payloads. Same order/names as the C++ self-test.
        Dump("packet_play", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PLAY, 1,
            HapbeatProtocol.BuildPlayPayload("k.e", 0, 1.0f, "")));
        Dump("packet_stop", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_STOP, 2,
            HapbeatProtocol.BuildStopPayload("k.e", "")));
        Dump("packet_stop_all", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_STOP_ALL, 3,
            HapbeatProtocol.BuildStopAllPayload("player_1")));
        Dump("packet_ping", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PING, 4,
            HapbeatProtocol.BuildPingPayload(1L)));
        Dump("packet_pong", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PONG, 5, new byte[16]));
        Dump("packet_connect_status", HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_CONNECT_STATUS, 6,
            HapbeatProtocol.BuildConnectStatusPayload(true, 0, "RoboRecall", "PC-1")));

        Console.WriteLine();

        // Full AddressMatches case list, mirroring HapbeatModCore.Tests
        // (AddressMatchesTests) minus its null cases, which C++ cannot express.
        string[,] matchCases =
        {
            { "", "player_1/pos_neck" },
            { "player_1", "player_1/pos_neck" },
            { "player_1", "player_2/pos_neck" },
            { "player_1/pos_neck", "player_1/pos_neck" },
            { "player_1/pos_neck", "player_1/pos_r_wrist" },
            { "*/pos_neck", "player_1/pos_neck" },
            { "*/pos_neck", "player_2/pos_neck" },
            { "player_1/pos_*", "player_1/pos_neck" },
            { "player_1/*", "player_1/pos_neck" },
            { "red", "red/player_1/pos_neck" },
            { "red/*/player_1", "red/alpha/player_1/pos_neck" },
            { "player_1/pos_neck/group_1", "player_1/pos_neck/group_1" },
            { "player_1/pos_neck/group_1", "player_1/pos_neck/group_2" },
            { "player_1/pos_neck", "player_1/pos_neck/group_1" },
            { "*/*/group_1", "player_2/pos_chest/group_1" },
            { "player_1/pos_neck/group_1", "player_1/pos_neck" },
            { "player_1/", "player_1/pos_neck/group_1" },
            { "player_1/pos_neck/", "player_1/pos_neck/group_1" },
            { "player_1/", "player_1" },
            { "player_2/", "player_1/pos_neck/group_1" },
            { "player_1//", "player_1/pos_neck" },
            { "/", "player_1/pos_neck" },
            { "*/*/group_2", "player_1/pos_chest/group_1" },
            { "*/*/group_2", "player_1/pos_chest/group_2" },
            { "pos_*", "pos_chest" },
            { "pos_*", "pos_*" },
        };
        for (int i = 0; i < matchCases.GetLength(0); i++)
        {
            string t = matchCases[i, 0], a = matchCases[i, 1];
            Console.WriteLine($"match[{t}|{a}]=" +
                (HapbeatModClient.AddressMatches(t, a) ? 1 : 0));
        }

        (string Target, int Player, int Group)[] resolveCases =
        {
            ("player_5/pos_chest", -1, -1),
            ("", 2, -1),
            ("player_5/pos_chest", 2, -1),
            ("*/pos_chest", 2, -1),
            ("", -1, 3),
            ("player_1/pos_chest", -1, 3),
            ("player_1/pos_chest/group_9", -1, 3),
            ("", 2, 3),
            ("red", -1, 3),
            ("*", 2, 3),
            ("red/player_1", 4, 5),
        };
        foreach (var c in resolveCases)
        {
            Console.WriteLine($"resolve[{c.Target}|{c.Player}|{c.Group}]=" +
                HapbeatModClient.ResolveTarget(c.Target, c.Player, c.Group));
        }
    }
}
