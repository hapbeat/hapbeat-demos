using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Hapbeat.ModCore
{
    /// <summary>
    /// Outcome of a PLAY/STOP/STOP_ALL send — see <see cref="HapbeatModClient.SendPlay"/>
    /// and the routing they share in <c>SendCommandRaw</c>.
    /// </summary>
    public enum CommandSendResult
    {
        /// <summary>Sent via plain broadcast — the client isn't in broadcast mode, or
        /// there was no live device to unicast to (none PONGed recently, or none whose
        /// reported address matched the target).</summary>
        Broadcast,

        /// <summary>Sent via unicast to one or more known devices — either their
        /// reported address matched the resolved target, or their address is unknown
        /// and was therefore kept (fail-open).</summary>
        Unicast,
    }

    /// <summary>
    /// Minimal UDP client for sending Hapbeat commands from a game mod. Ported from
    /// hapbeat-unity-sdk Runtime/HapbeatClient.cs with the Unity-specific parts removed:
    /// <list type="bullet">
    /// <item><c>UnityEngine.Debug</c> logging became the <see cref="Log"/> callback.</item>
    /// <item>The main-thread dispatch queue is gone — a mod has no Update() pump, so
    /// callbacks fire directly on the receive thread.</item>
    /// <item>Keep-alive (PING + CONNECT_STATUS) runs on an internal thread instead of
    /// being driven by <c>HapbeatManager.Update</c>.</item>
    /// <item><see cref="Fire"/> adds logical-event lookup and rate limiting so mod hook
    /// code never touches event ids, gains or the protocol.</item>
    /// </list>
    /// The wire format and the unicast routing rules are unchanged.
    /// </summary>
    public class HapbeatModClient : IDisposable
    {
        /// <summary>Optional log sink. Invoked from arbitrary threads (receive /
        /// keep-alive / caller). Mods should marshal to their own logger as needed.</summary>
        public Action<string> Log;

        /// <summary>Invoked on the receive thread when a PONG response is received.</summary>
        public event Action<long, long> OnPong; // (rttUs, serverTimeUs)

        /// <summary>Invoked on the receive thread for each PONG, with the source endpoint.</summary>
        public event Action<IPEndPoint, long> OnPongFrom; // (sender, rttUs)

        /// <summary>Invoked on the receive thread when an ERROR response is received.</summary>
        public event Action<ushort, string> OnError; // (errorCode, message)

        /// <summary>Invoked when connection state changes.</summary>
        public event Action<bool> OnConnectionStateChanged; // (isConnected)

        /// <summary>Whether the client is currently ready to send/receive.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Whether the client is in broadcast mode.</summary>
        public bool IsBroadcast { get; private set; }

        /// <summary>Settings driving app name, address override, gains and the logical
        /// event map. Never null (a default instance is used when none is supplied).</summary>
        public HapbeatModSettings Settings { get; private set; }

        /// <summary>Number of devices that have PONGed within the known-device TTL.
        /// Mods can surface this in a HUD/log to confirm devices are reachable.</summary>
        public int AliveDeviceCount
        {
            get
            {
                long nowUs = GetLocalTimestampUs();
                long ttlUs = (long)(_knownDeviceTtlSeconds * 1000000f);
                int count = 0;
                foreach (var kv in _knownDeviceIps)
                {
                    if (nowUs - kv.Value <= ttlUs)
                        count++;
                }
                return count;
            }
        }

        /// <summary>Default UDP port for Hapbeat devices (contracts specs/ports.md).</summary>
        public const int DefaultPort = 7700;

        /// <summary>Keep-alive interval. Sending PING is mandatory: PONG only ever
        /// answers a PING, so without it the known-device table ages out and every
        /// command falls back to broadcast forever (2026-08-03 unicast rollout).</summary>
        private const int KeepAliveIntervalMs = 2000;

        private UdpClient _udpClient;
        private IPEndPoint _targetEndPoint;
        private Thread _receiveThread;
        private Thread _keepAliveThread;
        private volatile bool _isRunning;
        private ushort _sequenceNumber;
        private readonly object _seqLock = new object();
        private readonly Stopwatch _stopwatch;

        // How long a device stays a unicast destination after its last PONG. Without
        // expiry the set only ever grows: a powered-off device would keep absorbing
        // datagrams AND keep the set non-empty, permanently suppressing the broadcast
        // fallback for a LAN with no live device left.
        private float _knownDeviceTtlSeconds = 15f;

        // Windows-only socket ioctl that stops an ICMP "port unreachable" (drawn by a
        // unicast send to a device that is off/rebooting) from surfacing as a
        // WSAECONNRESET on the NEXT Receive() call. See SuppressUdpConnReset.
        private const int SIO_UDP_CONNRESET = -1744830452; // 0x9800000C

        // One-shot guard so a recoverable receive error logs once per connection
        // instead of once per stale destination per command.
        private bool _loggedRecoverableReceiveError;

        // Last known device-addressing address string per sender IP, learned from the
        // PONG extension fields (device-addressing.md §5.4). A device with no entry
        // here is "unknown" — callers fail open (treat as matching) rather than as a
        // hard mismatch, since firmware predating this extension, or a PONG that
        // hasn't arrived yet, shouldn't silently lose its commands.
        private readonly ConcurrentDictionary<IPAddress, string> _deviceAddresses =
            new ConcurrentDictionary<IPAddress, string>();

        // Device IPs that have PONGed recently, mapped to the local timestamp (us) of
        // that most recent PONG. Recorded regardless of whether the PONG reported an
        // address, so an address-unknown device still receives commands via unicast.
        private readonly ConcurrentDictionary<IPAddress, long> _knownDeviceIps =
            new ConcurrentDictionary<IPAddress, long>();

        // Track ping timestamps for RTT calculation.
        private readonly ConcurrentDictionary<ushort, long> _pendingPings =
            new ConcurrentDictionary<ushort, long>();

        // Last fire time (ms on _stopwatch) per logical event name, for rate limiting.
        private readonly Dictionary<string, long> _lastFireMs =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly object _fireLock = new object();

        private bool _disposed;

        public HapbeatModClient(HapbeatModSettings settings = null)
        {
            Settings = settings ?? new HapbeatModSettings();
            _stopwatch = Stopwatch.StartNew();
        }

        #region Connection

        /// <summary>
        /// Open for UDP broadcast sending (standard Wi-Fi UDP mode) and start the
        /// receive + keep-alive threads.
        /// </summary>
        /// <param name="port">Target UDP port (default: 7700).</param>
        public void OpenBroadcast(int port = DefaultPort)
        {
            if (IsConnected)
                Close();

            try
            {
                _udpClient = new UdpClient(0); // bind to OS-assigned local port
                _udpClient.EnableBroadcast = true;
                SuppressUdpConnReset(_udpClient);
                _targetEndPoint = new IPEndPoint(IPAddress.Broadcast, port);
                IsBroadcast = true;

                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.Name = "HapbeatModReceive";
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                _keepAliveThread = new Thread(KeepAliveLoop);
                _keepAliveThread.Name = "HapbeatModKeepAlive";
                _keepAliveThread.IsBackground = true;
                _keepAliveThread.Start();

                IsConnected = true;
                InvokeConnectionStateChanged(true);

                // Announce immediately instead of waiting a full keep-alive tick: until
                // a device PONGs it is invisible to the unicast routing, so anything
                // fired in that window broadcasts while later commands unicast.
                SendPing();
                SendConnectStatus(true);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                IsBroadcast = false;
                throw new InvalidOperationException(
                    "Failed to open broadcast on port " + port + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Send a final CONNECT_STATUS(connected=false) so the device clears its display
        /// immediately, then stop the threads and release the socket.
        /// </summary>
        public void Close()
        {
            if (!IsConnected)
                return;

            // Fire-and-forget: UDP teardown can race with process shutdown.
            try { SendConnectStatus(false); }
            catch { /* shutdown race — ignore */ }

            _isRunning = false;
            IsConnected = false;
            IsBroadcast = false;

            try { if (_udpClient != null) _udpClient.Close(); }
            catch { /* Suppress exceptions during cleanup */ }

            if (_receiveThread != null && _receiveThread.IsAlive)
                _receiveThread.Join(1000);
            if (_keepAliveThread != null && _keepAliveThread.IsAlive)
                _keepAliveThread.Join(1000);

            _udpClient = null;
            _receiveThread = null;
            _keepAliveThread = null;
            _pendingPings.Clear();

            // Device knowledge is per-connection: after a reconnect the previous IPs may
            // belong to a different network entirely, and a stale non-empty set would
            // suppress the broadcast fallback (total silence on the new network).
            _knownDeviceIps.Clear();
            _deviceAddresses.Clear();
            _loggedRecoverableReceiveError = false;

            InvokeConnectionStateChanged(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Close();
        }

        #endregion

        #region Logical events

        /// <summary>
        /// Fire a logical event ("shot", "hit", ...) as defined in the settings file.
        /// The mod never sees event ids or gains — those are settings-side so they can
        /// be retuned without a rebuild. Safe to call from any thread.
        /// </summary>
        /// <returns>
        /// True if a PLAY was sent. False when the event is unknown, disabled, or
        /// suppressed by the per-event rate limit (<c>minIntervalMs</c>).
        /// </returns>
        public bool Fire(string logicalEvent)
        {
            if (string.IsNullOrEmpty(logicalEvent))
                return false;

            HapbeatModSettings settings = Settings;
            HapbeatEventSetting ev = settings.GetEvent(logicalEvent);
            if (ev == null)
            {
                Write("Unknown logical event '" + logicalEvent + "' (not in settings).");
                return false;
            }
            if (!ev.Enabled)
                return false;

            if (!PassesRateLimit(logicalEvent, settings.MinIntervalMs))
                return false;

            // targetTimeUs = 0 means "play immediately" (message-format.md §8) — the
            // same value HapbeatManager.Play uses for its immediate path.
            SendPlay(ev.EventId, 0, settings.MasterGain * ev.Gain, null);
            return true;
        }

        /// <summary>Stop the clip bound to a logical event (for looping events such as
        /// the low-health heartbeat). Not rate limited — a stop must never be dropped.</summary>
        public bool FireStop(string logicalEvent)
        {
            if (string.IsNullOrEmpty(logicalEvent))
                return false;

            HapbeatEventSetting ev = Settings.GetEvent(logicalEvent);
            if (ev == null)
                return false;

            SendStop(ev.EventId, null);
            return true;
        }

        private bool PassesRateLimit(string logicalEvent, int minIntervalMs)
        {
            if (minIntervalMs <= 0)
                return true;

            long nowMs = _stopwatch.ElapsedMilliseconds;
            lock (_fireLock)
            {
                long last;
                if (_lastFireMs.TryGetValue(logicalEvent, out last) && nowMs - last < minIntervalMs)
                    return false;
                _lastFireMs[logicalEvent] = nowMs;
                return true;
            }
        }

        #endregion

        #region Sending

        /// <summary>Send a PLAY command. <paramref name="target"/> is the device-addressing
        /// target string ("" = broadcast); the settings' player/group override is applied on
        /// top via <see cref="ResolveTarget(string)"/>.</summary>
        public CommandSendResult SendPlay(string eventId, long targetTimeUs, float gain, string target = null)
        {
            target = ResolveTarget(target);
            byte[] payload = HapbeatProtocol.BuildPlayPayload(eventId, targetTimeUs, gain, target);
            return SendCommandPacket(HapbeatProtocol.CMD_PLAY, payload, target);
        }

        /// <summary>Send a STOP command. See <see cref="SendPlay"/> for the routing this shares.</summary>
        public CommandSendResult SendStop(string eventId, string target = null)
        {
            target = ResolveTarget(target);
            byte[] payload = HapbeatProtocol.BuildStopPayload(eventId, target);
            return SendCommandPacket(HapbeatProtocol.CMD_STOP, payload, target);
        }

        /// <summary>Send a STOP_ALL command. See <see cref="SendPlay"/> for the routing this shares.</summary>
        public CommandSendResult SendStopAll(string target = null)
        {
            target = ResolveTarget(target);
            byte[] payload = HapbeatProtocol.BuildStopAllPayload(target);
            return SendCommandPacket(HapbeatProtocol.CMD_STOP_ALL, payload, target);
        }

        /// <summary>
        /// Send a CONNECT_STATUS so the device can show connection state on display/LED.
        /// Always broadcast (discovery/presence, not an addressed playback command).
        /// </summary>
        public void SendConnectStatus(bool connected)
        {
            HapbeatModSettings settings = Settings;
            // Legacy display-only field: the active override group when set, else 0.
            // Mirrors HapbeatManager.ConnectStatusGroupByte.
            byte group = settings.Group >= 1 ? (byte)settings.Group : (byte)0;
            byte[] payload = HapbeatProtocol.BuildConnectStatusPayload(
                connected, group, settings.AppName, GetHostName());
            SendPacket(HapbeatProtocol.CMD_CONNECT_STATUS, payload);
        }

        /// <summary>
        /// Send a PING for keep-alive and time synchronization. Always broadcast —
        /// this is what teaches the client which devices exist in the first place.
        /// </summary>
        /// <returns>The sequence number of the ping packet.</returns>
        public ushort SendPing()
        {
            long timestampUs = GetLocalTimestampUs();
            byte[] payload = HapbeatProtocol.BuildPingPayload(timestampUs);
            ushort seq = GetNextSequenceNumber();
            byte[] packet = HapbeatProtocol.BuildPacket(HapbeatProtocol.CMD_PING, seq, payload);

            _pendingPings[seq] = timestampUs;
            SendRaw(packet);
            return seq;
        }

        /// <summary>Current local timestamp in microseconds (high-resolution timer).</summary>
        public long GetLocalTimestampUs()
        {
            return _stopwatch.ElapsedTicks * 1000000L / Stopwatch.Frequency;
        }

        #endregion

        #region Addressing

        /// <summary>
        /// Device-addressing target/address match, mirroring firmware's
        /// <c>addressMatch()</c> (hapbeat-device-firmware/src/address_match.cpp) and
        /// the contracts pseudocode (device-addressing.md §4.3). Firmware is the
        /// authority where the two differ, since it decides what actually plays:
        /// <list type="bullet">
        /// <item>An empty/null <paramref name="target"/> matches every address.</item>
        /// <item>Both strings are split on <c>/</c> and compared segment-by-segment.</item>
        /// <item>A segment that is entirely <c>*</c> matches any single address segment
        /// (a partial wildcard such as <c>pos_*</c> is compared literally).</item>
        /// <item>Fewer target segments than address segments = front-match (OK).</item>
        /// <item>More target segments than address segments = mismatch.</item>
        /// <item>A single trailing <c>/</c> on the target is ignored, matching
        /// firmware's pointer walk.</item>
        /// </list>
        /// </summary>
        public static bool AddressMatches(string target, string deviceAddress)
        {
            if (string.IsNullOrEmpty(target))
                return true;

            string[] targetSegments = target.Split('/');
            string[] addressSegments = (deviceAddress ?? string.Empty).Split('/');

            // A single trailing '/' terminates the target rather than adding an empty
            // segment: firmware's loop advances past the separator and then exits on
            // `while (*tp)`, so "player_1/" behaves exactly like "player_1". Only the
            // last empty segment is dropped, and only once ("a//" really does compare
            // an empty segment in firmware, and still mismatches).
            int targetCount = targetSegments.Length;
            if (targetCount > 1 && targetSegments[targetCount - 1].Length == 0)
                targetCount--;

            for (int i = 0; i < targetCount; i++)
            {
                if (i >= addressSegments.Length)
                    return false; // target longer than address = mismatch

                if (targetSegments[i] != "*" && targetSegments[i] != addressSegments[i])
                    return false;
            }

            return true; // front-match or exact match
        }

        /// <summary>
        /// Clamp an override value to the valid device-addressing range (1..99).
        /// Anything outside that range (including the disabled sentinel -1) is
        /// normalized to -1 ("disabled").
        /// </summary>
        public static int NormalizeOverride(int value)
        {
            return (value >= 1 && value <= 99) ? value : -1;
        }

        /// <summary>
        /// Resolve a target string against forced player/group overrides. Both overrides
        /// disabled (&lt; 1) returns <paramref name="target"/> completely unchanged
        /// (including null).
        /// <para>
        /// Grammar: <c>[prefix/] player_{N} / {position} [/group_{M}]</c>
        /// — see hapbeat-contracts/specs/device-addressing.md §2.
        /// </para>
        /// </summary>
        public static string ResolveTarget(string target, int overridePlayer, int overrideGroup)
        {
            if (overridePlayer < 1 && overrideGroup < 1)
                return target; // both disabled: full passthrough

            List<string> segs = new List<string>((target ?? string.Empty).Split('/'));
            segs.RemoveAll(string.IsNullOrEmpty);

            if (overridePlayer >= 1)
            {
                string playerSeg = "player_" + overridePlayer;
                int i = segs.FindIndex(s => s.StartsWith("player_", StringComparison.Ordinal));
                if (i >= 0)
                {
                    segs[i] = playerSeg;
                }
                else
                {
                    int j = segs.FindIndex(s => s.StartsWith("pos_", StringComparison.Ordinal));
                    if (j > 0)
                        segs[j - 1] = playerSeg; // replace the placeholder segment (e.g. "*") right before position
                    else
                        segs.Insert(0, playerSeg); // j == 0 (position at front) or j == -1 (no position segment)
                }
            }

            if (overrideGroup >= 1)
            {
                string groupSeg = "group_" + overrideGroup;
                int k = segs.FindIndex(s => s.StartsWith("group_", StringComparison.Ordinal));
                if (k >= 0)
                {
                    segs[k] = groupSeg;
                }
                else
                {
                    // Firmware/spec matching is positional (device-addressing.md §2):
                    // the i-th target segment is compared against the i-th address
                    // segment only, with "*" consuming exactly one slot. group_ must
                    // therefore land in its grammar slot (immediately after {position});
                    // a naive Add() at the end lands it in whatever slot happens to be
                    // next, so firmware never matches.
                    int posIdx = segs.FindIndex(s => s.StartsWith("pos_", StringComparison.Ordinal));
                    if (posIdx >= 0)
                    {
                        segs.Insert(posIdx + 1, groupSeg);
                    }
                    else
                    {
                        // No explicit position segment. Locate the player slot: an
                        // explicit player_ segment, or a leading bare "*" acting as
                        // the player wildcard.
                        int playerIdx = segs.FindIndex(s => s.StartsWith("player_", StringComparison.Ordinal));
                        if (playerIdx < 0 && segs.Count > 0 && segs[0] == "*")
                            playerIdx = 0; // leading wildcard occupies the player slot

                        if (playerIdx >= 0)
                        {
                            // Position slot is the segment right after the player slot.
                            // Only pad a "*" placeholder when that slot is actually
                            // empty — if the target already occupies it, reuse it so
                            // group_ stays in the 3rd slot instead of being pushed to a
                            // 4th (which would make the target longer than the device
                            // address and break the positional match entirely).
                            int posSlot = playerIdx + 1;
                            if (posSlot >= segs.Count)
                                segs.Insert(posSlot, "*"); // no position segment yet — pad it
                            segs.Insert(posSlot + 1, groupSeg);
                        }
                        else
                        {
                            // Everything present (if anything) is a free prefix with no
                            // player/position slot. Append player and position
                            // placeholders, then group, so group stays after position
                            // and the prefix is preserved ahead of it.
                            segs.Add("*");
                            segs.Add("*");
                            segs.Add(groupSeg);
                        }
                    }
                }
            }

            return string.Join("/", segs.ToArray());
        }

        /// <summary>Instance wrapper around <see cref="ResolveTarget(string, int, int)"/>
        /// using the overrides from <see cref="Settings"/>.</summary>
        private string ResolveTarget(string target)
        {
            HapbeatModSettings settings = Settings;
            return ResolveTarget(target,
                NormalizeOverride(settings.Player), NormalizeOverride(settings.Group));
        }

        /// <summary>
        /// Last known device-addressing address string for <paramref name="ip"/>, as
        /// reported in its most recent parsed PONG. Null if unknown — callers must fail
        /// open (treat as matching) rather than as a mismatch.
        /// </summary>
        public string GetKnownDeviceAddress(IPAddress ip)
        {
            string address;
            return _deviceAddresses.TryGetValue(ip, out address) ? address : null;
        }

        #endregion

        #region Private send plumbing

        private void SendPacket(byte commandType, byte[] payload)
        {
            ushort seq = GetNextSequenceNumber();
            byte[] packet = HapbeatProtocol.BuildPacket(commandType, seq, payload);
            SendRaw(packet);
        }

        private void SendRaw(byte[] data)
        {
            if (!IsConnected || _udpClient == null)
                return;

            try
            {
                _udpClient.Send(data, data.Length, _targetEndPoint);
            }
            catch (SocketException ex)
            {
                Write("Send failed: " + ex.Message);
                HandleDisconnection();
            }
            catch (ObjectDisposedException)
            {
                HandleDisconnection();
            }
        }

        private CommandSendResult SendCommandPacket(byte commandType, byte[] payload, string resolvedTarget)
        {
            ushort seq = GetNextSequenceNumber();
            byte[] packet = HapbeatProtocol.BuildPacket(commandType, seq, payload);
            return SendCommandRaw(packet, resolvedTarget);
        }

        /// <summary>
        /// Routes a single PLAY/STOP/STOP_ALL packet to unicast or broadcast:
        /// <list type="bullet">
        /// <item>Not in broadcast mode -> plain send.</item>
        /// <item>No device has ever PONGed this session -> broadcast (fail open).</item>
        /// <item>A known device with no reported address -> unicast anyway (fail open).</item>
        /// <item>A known device whose reported address matches the target -> unicast.</item>
        /// <item>At least one send above went out -> done, no broadcast (avoids the
        /// double delivery a known-and-matching device would otherwise get).</item>
        /// <item>Nothing was unicast -> broadcast. Firmware re-applies
        /// <c>addressMatch()</c> to every command it receives, so a broadcast can never
        /// actuate a device the target didn't address; skipping would only risk silently
        /// losing a command when our cached address is stale — and for STOP/STOP_ALL that
        /// means a looping event never stops.</item>
        /// </list>
        /// </summary>
        private CommandSendResult SendCommandRaw(byte[] data, string resolvedTarget)
        {
            if (!IsConnected || _udpClient == null)
                return CommandSendResult.Broadcast;

            if (!IsBroadcast)
            {
                SendRaw(data);
                return CommandSendResult.Broadcast;
            }

            int port = _targetEndPoint != null ? _targetEndPoint.Port : 0;
            long nowUs = GetLocalTimestampUs();
            long ttlUs = (long)(_knownDeviceTtlSeconds * 1000000f);
            bool sentAny = false;

            foreach (var kv in _knownDeviceIps)
            {
                if (nowUs - kv.Value > ttlUs)
                {
                    // Device stopped answering PINGs (powered off, left the network).
                    // Skip it so we stop aiming datagrams at a dead host and so the set
                    // can empty out again and let the broadcast fallback take over.
                    // Skipped rather than removed: the entry is revived by the device's
                    // next PONG, and leaving the collection untouched keeps this loop
                    // free of any race with the receive thread writing into it.
                    continue;
                }

                string knownAddress = GetKnownDeviceAddress(kv.Key);
                // Fail open: unknown address => keep (send). Known address => must match.
                if (knownAddress != null && !AddressMatches(resolvedTarget, knownAddress))
                    continue;

                sentAny = true;
                try
                {
                    _udpClient.Send(data, data.Length, new IPEndPoint(kv.Key, port));
                }
                catch (SocketException ex)
                {
                    // A single unreachable/offline target shouldn't block the rest.
                    Write("Command unicast send to " + kv.Key + " failed: " + ex.Message);
                }
                catch (ObjectDisposedException)
                {
                    HandleDisconnection();
                    return CommandSendResult.Unicast; // best-effort; some sends may already be out
                }
            }

            if (!sentAny)
            {
                SendRaw(data);
                return CommandSendResult.Broadcast;
            }

            return CommandSendResult.Unicast;
        }

        /// <summary>
        /// Ask Windows to stop reporting ICMP "port unreachable" from a previous unicast
        /// send as an error on this socket. Without it, sending a command to a device
        /// that is powered off or rebooting makes the *next* <c>Receive()</c> throw
        /// <c>SocketException</c> (WSAECONNRESET / 10054) even though the socket is
        /// healthy — which used to kill the receive thread outright and, with it, every
        /// subsequent PONG (root-caused in hapbeat-helper f06fa04).
        /// Best-effort: the ioctl doesn't exist off Windows, and <see cref="ReceiveLoop"/>
        /// treats the error as non-fatal regardless.
        /// </summary>
        private static void SuppressUdpConnReset(UdpClient client)
        {
            try
            {
                client.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch
            {
                // Unsupported platform / runtime — ReceiveLoop is the safety net.
            }
        }

        /// <summary>
        /// Whether a receive-side <see cref="SocketException"/> describes ICMP feedback
        /// about one previously-sent datagram (a dead unicast destination) rather than a
        /// broken socket. These must not tear down the receive thread: nothing restarts
        /// it, so one powered-off device would otherwise disable haptics for the rest of
        /// the session.
        /// </summary>
        private static bool IsRecoverableReceiveError(SocketError error)
        {
            return error == SocketError.ConnectionReset      // WSAECONNRESET (10054) — ICMP port unreachable
                || error == SocketError.ConnectionRefused    // same class, reported differently by some stacks
                || error == SocketError.HostUnreachable
                || error == SocketError.NetworkUnreachable
                || error == SocketError.NetworkReset
                || error == SocketError.MessageSize;         // oversized datagram: drop it, keep the socket
        }

        private ushort GetNextSequenceNumber()
        {
            lock (_seqLock)
            {
                return _sequenceNumber++;
            }
        }

        #endregion

        #region Background threads

        private void KeepAliveLoop()
        {
            while (_isRunning)
            {
                Thread.Sleep(KeepAliveIntervalMs);
                if (!_isRunning || !IsConnected)
                    continue;

                try
                {
                    SendPing();
                    SendConnectStatus(true);
                }
                catch (Exception ex)
                {
                    Write("Keep-alive failed: " + ex.Message);
                }
            }
        }

        private void ReceiveLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (_udpClient == null || _udpClient.Client == null)
                        break;

                    // Poll so shutdown is graceful (Receive itself would block).
                    if (_udpClient.Client.Poll(100000, SelectMode.SelectRead)) // 100 ms
                    {
                        if (!_isRunning)
                            break;

                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = _udpClient.Receive(ref remoteEp);

                        if (data != null && data.Length >= HapbeatProtocol.HEADER_SIZE)
                            ProcessReceivedPacket(data, remoteEp);
                    }
                }
                catch (SocketException ex)
                {
                    if (!_isRunning)
                        break;

                    if (IsRecoverableReceiveError(ex.SocketErrorCode))
                    {
                        // Per-datagram ICMP feedback, not a dead socket. Breaking here
                        // would silently end PONG reception for the whole session. Log
                        // once per connection so a genuinely misconfigured LAN is still
                        // visible without spamming one line per stale destination.
                        if (!_loggedRecoverableReceiveError)
                        {
                            _loggedRecoverableReceiveError = true;
                            Write("Ignoring recoverable receive error (" + ex.SocketErrorCode +
                                  "); a device is likely powered off or rebooting. Receive loop continues.");
                        }
                        continue;
                    }

                    HandleDisconnection();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        Write("Receive error: " + ex.Message);
                }
            }
        }

        private void ProcessReceivedPacket(byte[] data, IPEndPoint sender)
        {
            try
            {
                byte commandType;
                ushort seq;
                byte[] payload;
                HapbeatProtocol.ParsePacket(data, out commandType, out seq, out payload);

                switch (commandType)
                {
                    case HapbeatProtocol.CMD_PONG:
                        HandlePong(seq, payload, sender);
                        break;

                    case HapbeatProtocol.CMD_ERROR:
                        HandleError(payload);
                        break;

                    default:
                        Write("Unknown response command: 0x" + commandType.ToString("X2"));
                        break;
                }
            }
            catch (Exception ex)
            {
                Write("Failed to parse packet: " + ex.Message);
            }
        }

        private void HandlePong(ushort seq, byte[] payload, IPEndPoint sender)
        {
            long timestamp, serverTime;
            string deviceName, address, firmwareVersion;
            int volumeLevel, volumeWiper;
            HapbeatProtocol.ParsePongExtended(payload, out timestamp, out serverTime,
                out deviceName, out address, out firmwareVersion, out volumeLevel, out volumeWiper);

            long nowUs = GetLocalTimestampUs();

            // Recorded regardless of whether this PONG reported an address —
            // SendCommandRaw needs the full set of live devices, so an address-unknown
            // device still gets commands via unicast. The timestamp is what lets
            // SendCommandRaw expire a device that stopped answering PINGs.
            _knownDeviceIps[sender.Address] = nowUs;
            if (!string.IsNullOrEmpty(address))
                _deviceAddresses[sender.Address] = address;

            long sentTimeUs;
            long rttUs = _pendingPings.TryRemove(seq, out sentTimeUs)
                ? nowUs - sentTimeUs
                : nowUs - timestamp; // fallback: timestamp echoed in the PONG payload

            var pongHandler = OnPong;
            if (pongHandler != null) pongHandler(rttUs, serverTime);

            var pongFromHandler = OnPongFrom;
            if (pongFromHandler != null)
                pongFromHandler(new IPEndPoint(sender.Address, sender.Port), rttUs);
        }

        private void HandleError(byte[] payload)
        {
            ushort errorCode;
            string message;
            HapbeatProtocol.ParseError(payload, out errorCode, out message);
            var handler = OnError;
            if (handler != null) handler(errorCode, message);
        }

        private void HandleDisconnection()
        {
            if (!IsConnected)
                return;

            IsConnected = false;
            IsBroadcast = false;
            InvokeConnectionStateChanged(false);
        }

        private void InvokeConnectionStateChanged(bool connected)
        {
            var handler = OnConnectionStateChanged;
            if (handler != null) handler(connected);
        }

        private void Write(string message)
        {
            var log = Log;
            if (log != null) log("[Hapbeat] " + message);
        }

        private static string GetHostName()
        {
            try { return Environment.MachineName; }
            catch { return ""; }
        }

        #endregion
    }
}
