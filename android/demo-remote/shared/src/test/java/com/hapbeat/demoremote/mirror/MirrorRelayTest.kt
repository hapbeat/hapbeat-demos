package com.hapbeat.demoremote.mirror

import com.hapbeat.demoremote.data.MirrorSettings
import dadb.AdbShellResponse
import dadb.AdbShellStream
import dadb.AdbStream
import dadb.Dadb
import dadb.SyncResult
import okio.Buffer
import okio.BufferedSink
import okio.BufferedSource
import okio.Pipe
import okio.Source
import okio.buffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.net.ServerSocket
import java.net.SocketTimeoutException
import java.util.concurrent.CopyOnWriteArrayList

class MirrorRelayTest {
    private val token = "ab".repeat(16)

    private fun frame(n: Long, key: Boolean = false, size: Int = 4) =
        ScrcpyPacket.Media(false, key, n, ByteArray(size) { n.toByte() })

    private val config = ScrcpyPacket.Media(true, false, 0, byteArrayOf(7, 7, 7))

    private fun join(server: MirrorRelayServer, timeoutMs: Int = 5_000): Pair<java.net.Socket, ScrcpyVideoReader> {
        val socket = MirrorRelayClient.connect(server.info)
        socket.soTimeout = timeoutMs
        val reader = ScrcpyVideoReader(socket.getInputStream())
        assertEquals(ScrcpyVideoReader.CODEC_H264, reader.readHeader())
        return socket to reader
    }

    private fun waitUntil(what: String, timeoutMs: Long = 5_000, condition: () -> Boolean) {
        val deadline = System.nanoTime() + timeoutMs * 1_000_000
        while (!condition()) {
            if (System.nanoTime() > deadline) throw AssertionError("timed out: $what")
            Thread.sleep(10)
        }
    }

    // ---- the note --------------------------------------------------------------------------------

    @Test
    fun noteIsReadBackFromTheCommandsOutput() {
        assertEquals(
            RelayInfo("192.168.0.20", 50123, token),
            RelayNote.parse("192.168.0.20 50123 $token\n\n1000\n1010\n"),
        )
    }

    @Test
    fun noteThatStoppedBeingRefreshedMeansNoRelay() {
        assertNull(RelayNote.parse("192.168.0.20 50123 $token\n\n1000\n1026\n"))
    }

    @Test
    fun noNoteMeansNoRelay() {
        assertNull(RelayNote.parse("\n1010\n"))
        assertNull(RelayNote.parse(""))
    }

    @Test
    fun malformedNotesAreRefused() {
        listOf(
            "example.com 50123 $token", "192.168.0.999 50123 $token", "192.168.0.20 70000 $token",
            "192.168.0.20 50123 short", "192.168.0.20 50123 ${token.uppercase()}", "192.168.0.20 50123",
        ).forEach { assertNull(it, RelayNote.parse("$it\n\n1000\n1000\n")) }
    }

    // ---- the wire --------------------------------------------------------------------------------

    @Test
    fun whatIsEncodedReadsBackTheSame() {
        val bytes = ByteArrayOutputStream()
        bytes.write(RelayWire.streamHeader())
        bytes.write(RelayWire.encode(ScrcpyPacket.Session(1832, 1920)))
        bytes.write(RelayWire.encode(config))
        bytes.write(RelayWire.encode(frame(123456, key = true)))
        bytes.write(RelayWire.encode(frame((1L shl 60) + 7)))
        val reader = ScrcpyVideoReader(ByteArrayInputStream(bytes.toByteArray()))
        assertEquals(ScrcpyVideoReader.CODEC_H264, reader.readHeader())
        assertEquals(ScrcpyPacket.Session(1832, 1920), reader.readPacket())
        val readConfig = reader.readPacket() as ScrcpyPacket.Media
        assertTrue(readConfig.config)
        assertFalse(readConfig.keyFrame)
        assertArrayEquals(config.data, readConfig.data)
        val key = reader.readPacket() as ScrcpyPacket.Media
        assertTrue(key.keyFrame)
        assertFalse(key.config)
        assertEquals(123456L, key.ptsUs)
        val plain = reader.readPacket() as ScrcpyPacket.Media
        assertFalse(plain.keyFrame)
        assertEquals((1L shl 60) + 7, plain.ptsUs)
    }

    @Test
    fun aRelayedStreamAsksTheQuestForFrequentKeyFrames() {
        assertTrue(ScrcpyLaunch.serverCommand("0000abcd", MirrorSettings(), null, 1).contains(" video_codec_options=i-frame-interval:int=1"))
        assertFalse(ScrcpyLaunch.serverCommand("0000abcd", MirrorSettings(), null).contains("video_codec_options"))
    }

    // ---- the server ------------------------------------------------------------------------------

    @Test
    fun aPhoneJoiningLateSeesTheLastKeyFrameThenMovesFromTheNext() = MirrorRelayServer("127.0.0.1").use { server ->
        server.onPacket(ScrcpyPacket.Session(800, 600))
        server.onPacket(config)
        server.onPacket(frame(1, key = true))
        server.onPacket(frame(2))
        val (socket, reader) = join(server)
        socket.use {
            server.onPacket(frame(3)) // cannot be decoded without the keyframe before it
            server.onPacket(frame(4, key = true))
            server.onPacket(frame(5))
            assertEquals(ScrcpyPacket.Session(800, 600), reader.readPacket())
            assertTrue((reader.readPacket() as ScrcpyPacket.Media).config)
            assertEquals(1L, (reader.readPacket() as ScrcpyPacket.Media).ptsUs)
            assertEquals(4L, (reader.readPacket() as ScrcpyPacket.Media).ptsUs)
            assertEquals(5L, (reader.readPacket() as ScrcpyPacket.Media).ptsUs)
        }
    }

    @Test
    fun aRestartedEncoderStartsPhonesOver() = MirrorRelayServer("127.0.0.1").use { server ->
        server.onPacket(ScrcpyPacket.Session(800, 600))
        server.onPacket(config)
        val (socket, reader) = join(server)
        socket.use {
            server.onPacket(frame(1, key = true))
            server.onPacket(ScrcpyPacket.Session(400, 300))
            server.onPacket(config)
            server.onPacket(frame(2)) // from before the new keyframe
            server.onPacket(frame(3, key = true))
            // Frame 1 may or may not have left before the restart.
            val seen = ArrayList<ScrcpyPacket>()
            while ((seen.lastOrNull() as? ScrcpyPacket.Media)?.ptsUs != 3L) seen.add(reader.readPacket())
            assertEquals(ScrcpyPacket.Session(400, 300), seen.filterIsInstance<ScrcpyPacket.Session>().last())
            assertFalse(seen.filterIsInstance<ScrcpyPacket.Media>().any { it.ptsUs == 2L })
        }
    }

    @Test
    fun aWrongTokenGetsNothing() = MirrorRelayServer("127.0.0.1").use { server ->
        server.onPacket(ScrcpyPacket.Session(800, 600))
        val socket = MirrorRelayClient.connect(server.info.copy(token = "f".repeat(32)))
        socket.use {
            socket.soTimeout = 5_000
            assertEquals(-1, socket.getInputStream().read())
        }
        assertFalse(MirrorRelayClient.reachable(server.info.copy(token = "f".repeat(32))))
        assertTrue(MirrorRelayClient.reachable(server.info))
    }

    @Test
    fun aPhoneThatStopsReadingHoldsUpNeitherThePcNorTheOtherPhone() = MirrorRelayServer("127.0.0.1").use { server ->
        val (stalled, _) = join(server)
        val (socket, reader) = join(server)
        stalled.use {
            socket.use {
                val received = CopyOnWriteArrayList<Long>()
                val readerThread = Thread {
                    try {
                        while (true) received.add((reader.readPacket() as ScrcpyPacket.Media).ptsUs)
                    } catch (_: IOException) {
                        // closed at the end of the test
                    }
                }.apply { isDaemon = true; start() }
                val started = System.nanoTime()
                for (n in 1L..80L) {
                    server.onPacket(frame(n, key = n % 5 == 1L, size = 256 * 1024))
                    waitUntil("the reading phone to get frame $n") { received.isNotEmpty() && received.last() == n }
                }
                assertTrue("onPacket waited for a phone", (System.nanoTime() - started) / 1_000_000 < 20_000)
                assertEquals((1L..80L).toList(), received.toList())
                socket.close()
                readerThread.join(2_000)
            }
        }
    }

    @Test
    fun afterFallingBehindAPhoneResumesAtAKeyFrame() = MirrorRelayServer("127.0.0.1").use { server ->
        val (socket, reader) = join(server, timeoutMs = 10_000)
        socket.use {
            // Far more than the queue and the socket buffers hold, while the phone reads nothing.
            for (n in 1L..200L) server.onPacket(frame(n, key = n % 10 == 1L, size = 512 * 1024))
            val received = CopyOnWriteArrayList<ScrcpyPacket.Media>()
            Thread {
                try {
                    while (true) received.add(reader.readPacket() as ScrcpyPacket.Media)
                } catch (_: IOException) {
                    // closed at the end of the test
                }
            }.apply { isDaemon = true; start() }
            for (n in 201L..260L) {
                server.onPacket(frame(n, key = n % 10 == 1L, size = 16 * 1024))
                Thread.sleep(5)
            }
            waitUntil("the last frame") { received.lastOrNull()?.ptsUs == 260L }
            val gaps = received.zipWithNext().filter { (before, after) -> after.ptsUs != before.ptsUs + 1 }
            assertTrue("nothing was dropped", gaps.isNotEmpty())
            gaps.forEach { (before, after) -> assertTrue("frame ${after.ptsUs} after ${before.ptsUs} is not a keyframe", after.keyFrame) }
        }
    }

    @Test
    fun closingEndsThePhones() {
        val server = MirrorRelayServer("127.0.0.1")
        val (socket, reader) = join(server)
        socket.use {
            server.close()
            try {
                reader.readPacket()
                throw AssertionError("still reading")
            } catch (_: EOFException) {
                // expected
            } catch (_: IOException) {
                // a reset says the same
            }
        }
        assertFalse(MirrorRelayClient.reachable(server.info))
    }

    @Test
    fun thePcCountsThePhonesAndAFifthStaysOnItsOwnStream() {
        val counts = CopyOnWriteArrayList<Int>()
        MirrorRelayServer("127.0.0.1") { counts.add(it) }.use { server ->
            assertTrue(MirrorRelayClient.reachable(server.info))
            val phones = List(4) { join(server).first }
            waitUntil("four phones") { counts.lastOrNull() == 4 }
            assertEquals("a probe was counted", listOf(1, 2, 3, 4), counts.toList())
            assertFalse(MirrorRelayClient.reachable(server.info))
            MirrorRelayClient.connect(server.info).use { fifth ->
                fifth.soTimeout = 5_000
                assertEquals(-1, fifth.getInputStream().read())
            }
            phones.first().close() // noticed with no video flowing
            waitUntil("the phone that left") { counts.lastOrNull() == 3 }
            assertTrue(MirrorRelayClient.reachable(server.info))
            phones.drop(1).forEach { it.close() }
        }
    }

    // ---- a session ---------------------------------------------------------------------------------

    private class FakeSink : VideoSink {
        val sizes = CopyOnWriteArrayList<Pair<Int, Int>>()
        val frames = CopyOnWriteArrayList<Long>()
        override fun configure(width: Int, height: Int) { sizes.add(width to height) }
        override fun queue(packet: ScrcpyPacket.Media): Boolean {
            if (!packet.config) frames.add(packet.ptsUs)
            return true
        }
        override fun drain() {}
        override fun release() {}
    }

    private class FakeListener : MirrorSession.Listener {
        val routes = CopyOnWriteArrayList<MirrorRoute>()
        val errors = CopyOnWriteArrayList<String>()
        val clients = CopyOnWriteArrayList<Int?>()
        override fun onVideoSize(width: Int, height: Int) {}
        override fun onError(message: String) { errors.add(message) }
        override fun onRoute(route: MirrorRoute) { routes.add(route) }
        override fun onRelayClients(count: Int?) { clients.add(count) }
    }

    private class FakeStream(
        pipe: Pipe = Pipe(4 * 1024 * 1024),
        private val output: BufferedSink = pipe.sink.buffer(),
        override val source: BufferedSource = pipe.source.buffer(),
    ) : AdbStream {
        override val sink: BufferedSink = Buffer()
        @Volatile var closed = false

        fun send(bytes: ByteArray) {
            output.write(bytes)
            output.flush()
        }

        override fun close() {
            closed = true
            runCatching { output.close() }
        }
    }

    /** A Quest as far as a session can see: the note, and a scrcpy-server that sends what [streams] get. */
    private class FakeQuest {
        @Volatile var note: String = ""
        @Volatile var failing = false
        val serverCommands = CopyOnWriteArrayList<String>()
        val streams = CopyOnWriteArrayList<FakeStream>()
        val written = CopyOnWriteArrayList<String>()

        fun connect(): Dadb = object : Dadb {
            override fun open(destination: String): AdbStream = FakeStream().also {
                it.send(RelayWire.streamHeader())
                streams.add(it)
            }

            override fun supportsFeature(feature: String) = false
            override fun close() {
                streams.forEach { it.close() }
            }

            override fun shell(command: String): AdbShellResponse {
                if (failing) throw IOException("adb connection lost")
                if (command == RelayNote.READ_COMMAND) return AdbShellResponse(note + "\n", "", 0)
                written.add(command)
                return AdbShellResponse("Physical size: 3664x1920\n", "", 0)
            }

            override fun openShell(command: String): AdbShellStream {
                serverCommands.add(command)
                return AdbShellStream(FakeStream())
            }

            override fun push(source: Source, remotePath: String, mode: Int, lastModifiedMs: Long): SyncResult = SyncResult.Success
        }

        fun noteOf(info: RelayInfo) = "${info.host} ${info.port} ${info.token}\n\n1000\n1000"
    }

    private val fast = RelayTiming(watchMs = 50, retryMs = 50)

    private fun session(quest: FakeQuest, sink: FakeSink, listener: FakeListener, role: RelayRole = RelayRole.Join) = MirrorSession(
        quest::connect, { ByteArrayInputStream(ByteArray(4)) }, MirrorSettings(), sink, listener, role, fast,
    )

    private fun FakeStream.sendVideo(firstFrame: Long) {
        send(RelayWire.encode(ScrcpyPacket.Session(640, 480)))
        send(RelayWire.encode(config))
        send(RelayWire.encode(frame(firstFrame, key = true)))
    }

    @Test
    fun aPhoneAloneTakesItsVideoFromTheQuest() {
        val quest = FakeQuest()
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the server's stream") { quest.streams.isNotEmpty() }
            quest.streams.first().sendVideo(1)
            waitUntil("the frame") { sink.frames.contains(1L) }
            assertEquals(listOf(MirrorRoute.DIRECT), listener.routes.toList())
            assertEquals(1, quest.serverCommands.size)
            assertFalse("a phone asks for nothing extra", quest.serverCommands.first().contains("video_codec_options"))
            assertTrue(listener.errors.isEmpty())
        } finally {
            session.stop()
        }
    }

    @Test
    fun withAPcRelayingThePhoneStartsNothingOnTheQuest() = MirrorRelayServer("127.0.0.1").use { server ->
        server.onPacket(ScrcpyPacket.Session(640, 480))
        server.onPacket(config)
        val quest = FakeQuest().apply { note = noteOf(server.info) }
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the phone's route") { listener.routes.isNotEmpty() }
            assertEquals(MirrorRoute.VIA_PC, listener.routes.first())
            waitUntil("the frames") {
                server.onPacket(frame(sink.frames.size + 1L, key = true))
                sink.frames.isNotEmpty()
            }
            assertTrue("the Quest got a second server", quest.serverCommands.isEmpty())
            assertEquals(640 to 480, sink.sizes.first())
        } finally {
            session.stop()
        }
    }

    @Test
    fun whenAPcStartsThePhoneDropsItsOwnStreamAndWhenItLeavesTakesItBack() {
        val quest = FakeQuest()
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the phone's own stream") { quest.streams.isNotEmpty() }
            val own = quest.streams.first()
            own.sendVideo(1)
            waitUntil("the frame") { sink.frames.contains(1L) }

            val server = MirrorRelayServer("127.0.0.1")
            server.onPacket(ScrcpyPacket.Session(640, 480))
            server.onPacket(config)
            quest.note = quest.noteOf(server.info)
            waitUntil("the switch to the PC") { listener.routes.contains(MirrorRoute.VIA_PC) }
            assertTrue("its own stream still runs", own.closed)
            val firstServers = quest.serverCommands.size

            quest.note = ""
            server.close()
            waitUntil("the way back") { listener.routes.last() == MirrorRoute.DIRECT }
            assertEquals(firstServers + 1, quest.serverCommands.size)
            assertTrue(listener.errors.isEmpty())
        } finally {
            session.stop()
        }
    }

    @Test
    fun aPcThatCannotBeReachedStillLeavesThePhoneAPicture() {
        val closedPort = ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")).use { it.localPort }
        val quest = FakeQuest().apply { note = noteOf(RelayInfo("127.0.0.1", closedPort, "a".repeat(32))) }
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the phone's route") { listener.routes.isNotEmpty() }
            assertEquals(MirrorRoute.DIRECT_PC_UNREACHABLE, listener.routes.first())
            waitUntil("the server's stream") { quest.streams.isNotEmpty() }
            quest.streams.first().sendVideo(1)
            waitUntil("the frame") { sink.frames.contains(1L) }
        } finally {
            session.stop()
        }
    }

    @Test
    fun aPhoneThatCannotPlayThePcsVideoTakesItsOwn() = MirrorRelayServer("127.0.0.1").use { server ->
        server.onPacket(ScrcpyPacket.Session(4096, 4096))
        server.onPacket(config)
        val quest = FakeQuest().apply { note = noteOf(server.info) }
        val sink = object : VideoSink {
            val frames = CopyOnWriteArrayList<Long>()
            override fun configure(width: Int, height: Int) {
                if (width > 1024) throw IllegalStateException("no decoder for this size")
            }
            override fun queue(packet: ScrcpyPacket.Media): Boolean {
                if (!packet.config) frames.add(packet.ptsUs)
                return true
            }
            override fun drain() {}
            override fun release() {}
        }
        val listener = FakeListener()
        val session = MirrorSession(quest::connect, { ByteArrayInputStream(ByteArray(4)) }, MirrorSettings(), sink, listener, RelayRole.Join, fast)
        session.start()
        try {
            waitUntil("the phone's own stream") { quest.streams.isNotEmpty() }
            quest.streams.first().sendVideo(1)
            waitUntil("its frame") { sink.frames.contains(1L) }
            assertEquals(listOf(MirrorRoute.VIA_PC, MirrorRoute.DIRECT_PC_TOO_HEAVY), listener.routes.toList())
            Thread.sleep(200) // several looks later
            assertEquals("it went back to the PC", MirrorRoute.DIRECT_PC_TOO_HEAVY, listener.routes.last())
            assertTrue(listener.errors.isEmpty())
        } finally {
            session.stop()
        }
    }

    @Test
    fun anAdbFailureAfterThePcsVideoIsNotFinal() {
        val server = MirrorRelayServer("127.0.0.1")
        server.onPacket(ScrcpyPacket.Session(640, 480))
        val quest = FakeQuest().apply { note = noteOf(server.info) }
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the route via the PC") { listener.routes.contains(MirrorRoute.VIA_PC) }
            quest.failing = true
            quest.note = ""
            server.close()
            Thread.sleep(300) // several failed looks
            assertTrue("it gave up: ${listener.errors}", listener.errors.isEmpty())
            quest.failing = false
            waitUntil("the phone's own stream") { quest.streams.isNotEmpty() }
            quest.streams.last().sendVideo(1)
            waitUntil("the way back") { listener.routes.last() == MirrorRoute.DIRECT }
            assertTrue(listener.errors.isEmpty())
        } finally {
            session.stop()
        }
    }

    @Test
    fun aFirstAdbFailureIsStillReported() {
        val quest = FakeQuest().apply { failing = true }
        val listener = FakeListener()
        val session = session(quest, FakeSink(), listener)
        session.start()
        try {
            waitUntil("the error") { listener.errors.isNotEmpty() }
        } finally {
            session.stop()
        }
    }

    @Test
    fun theRouteLineFollowsAPcThatStartsBehindAFirewallAndLeaves() {
        val closedPort = ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")).use { it.localPort }
        val quest = FakeQuest()
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener)
        session.start()
        try {
            waitUntil("the phone's own stream") { quest.streams.isNotEmpty() }
            quest.streams.first().sendVideo(1)
            waitUntil("the route") { listener.routes.lastOrNull() == MirrorRoute.DIRECT }
            quest.note = quest.noteOf(RelayInfo("127.0.0.1", closedPort, "a".repeat(32)))
            waitUntil("the warning") { listener.routes.last() == MirrorRoute.DIRECT_PC_UNREACHABLE }
            quest.note = ""
            waitUntil("the warning to go") { listener.routes.last() == MirrorRoute.DIRECT }
            assertEquals("the picture was interrupted", 1, quest.serverCommands.size)
        } finally {
            session.stop()
        }
    }

    @Test
    fun everyServerGetsItsOwnJar() {
        assertTrue(ScrcpyLaunch.serverCommand("0000abcd", MirrorSettings(), null).startsWith("CLASSPATH=${ScrcpyLaunch.remoteJar("0000abcd")} "))
        assertFalse(ScrcpyLaunch.remoteJar("0000abcd") == ScrcpyLaunch.remoteJar("0000abce"))
    }

    @Test
    fun aPcOffersItsVideoAndCleansUpItsNote() {
        val quest = FakeQuest()
        val sink = FakeSink()
        val listener = FakeListener()
        val session = session(quest, sink, listener, RelayRole.Host("127.0.0.1", quest::connect))
        session.start()
        var relay: RelayInfo? = null
        try {
            waitUntil("the server's stream") { quest.streams.isNotEmpty() }
            assertTrue(quest.serverCommands.first().contains("video_codec_options=i-frame-interval:int=1"))
            quest.streams.first().sendVideo(1)
            waitUntil("the frame on the PC") { sink.frames.contains(1L) }
            waitUntil("the note") { quest.written.any { it.contains(RelayNote.PATH) && it.contains("umask 077") } }
            val written = quest.written.first { it.contains("umask 077") }
            val noted = Regex("""'(.*)'""").find(written)!!.groupValues[1]
            relay = RelayNote.parse("$noted\n\n1000\n1000")
            assertNotNull(relay)

            val socket = MirrorRelayClient.connect(relay!!)
            socket.use {
                socket.soTimeout = 5_000
                val reader = ScrcpyVideoReader(socket.getInputStream())
                assertEquals(ScrcpyVideoReader.CODEC_H264, reader.readHeader())
                assertEquals(ScrcpyPacket.Session(640, 480), reader.readPacket())
                assertTrue((reader.readPacket() as ScrcpyPacket.Media).config)
                assertEquals(1L, (reader.readPacket() as ScrcpyPacket.Media).ptsUs)
                waitUntil("the phone to be counted") { listener.clients.contains(1) }
                quest.streams.first().send(RelayWire.encode(frame(2, key = true)))
                assertEquals(2L, (reader.readPacket() as ScrcpyPacket.Media).ptsUs)
                assertEquals(listOf(1L, 2L), sink.frames.toList())
            }
        } catch (e: SocketTimeoutException) {
            throw AssertionError("the relay sent nothing", e)
        } finally {
            session.stop()
        }
        waitUntil("the note's removal") { quest.written.any { it == RelayNote.removeCommand(relay!!) } }
        assertFalse(MirrorRelayClient.reachable(relay!!))
    }
}
