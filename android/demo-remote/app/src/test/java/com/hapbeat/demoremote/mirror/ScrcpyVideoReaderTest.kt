package com.hapbeat.demoremote.mirror

import com.hapbeat.demoremote.data.MirrorSettings
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.io.EOFException

class ScrcpyVideoReaderTest {
    private fun stream(build: DataOutputStream.() -> Unit): ByteArrayInputStream {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).build() // DataOutputStream writes big-endian
        return ByteArrayInputStream(bytes.toByteArray())
    }

    @Test
    fun parsesHeaderSessionConfigAndKeyframe() {
        val input = stream {
            writeByte(0) // dummy byte
            writeInt(ScrcpyVideoReader.CODEC_H264)
            // session packet: MSB of byte0 set, width/height in bytes 4..11
            writeInt(0x80000000.toInt()); writeInt(1832); writeInt(960)
            // config packet
            writeLong(1L shl 62); writeInt(3); write(byteArrayOf(1, 2, 3))
            // keyframe with PTS 123456
            writeLong((1L shl 61) or 123456L); writeInt(2); write(byteArrayOf(9, 8))
            // plain frame with a large PTS
            writeLong((1L shl 60) + 7); writeInt(1); write(byteArrayOf(5))
        }
        val reader = ScrcpyVideoReader(input)
        assertEquals(0x68323634, reader.readHeader())
        assertEquals(ScrcpyPacket.Session(1832, 960), reader.readPacket())

        val config = reader.readPacket() as ScrcpyPacket.Media
        assertTrue(config.config); assertFalse(config.keyFrame); assertEquals(0L, config.ptsUs)
        assertArrayEquals(byteArrayOf(1, 2, 3), config.data)

        val key = reader.readPacket() as ScrcpyPacket.Media
        assertFalse(key.config); assertTrue(key.keyFrame); assertEquals(123456L, key.ptsUs)
        assertArrayEquals(byteArrayOf(9, 8), key.data)

        val frame = reader.readPacket() as ScrcpyPacket.Media
        assertFalse(frame.config); assertFalse(frame.keyFrame); assertEquals((1L shl 60) + 7, frame.ptsUs)
        assertEquals(1, frame.data.size)
    }

    @Test(expected = EOFException::class)
    fun truncatedPayloadThrows() {
        val input = stream { writeLong(0); writeInt(10); write(byteArrayOf(1, 2)) }
        ScrcpyVideoReader(input).readPacket()
    }

    @Test
    fun wmSizePrefersOverride() {
        assertEquals(3664 to 1920, ScrcpyLaunch.parseWmSize("Physical size: 3664x1920\n"))
        assertEquals(1280 to 720, ScrcpyLaunch.parseWmSize("Physical size: 3664x1920\nOverride size: 1280x720\n"))
        assertNull(ScrcpyLaunch.parseWmSize("error"))
    }

    @Test
    fun cropAndCommand() {
        assertEquals("1832:1920:0:0", ScrcpyLaunch.cropFor(3664, 1920, bothEyes = false))
        assertNull(ScrcpyLaunch.cropFor(3664, 1920, bothEyes = true))
        assertNull(ScrcpyLaunch.cropFor(1080, 1920, bothEyes = false))
        val scid = ScrcpyLaunch.newScid()
        assertTrue(Regex("^[0-9a-f]{8}$").matches(scid))
        assertTrue(scid.toLong(16) <= Int.MAX_VALUE)
        val cmd = ScrcpyLaunch.serverCommand("0000abcd", MirrorSettings(), "1832:1920:0:0")
        assertEquals(
            "CLASSPATH=/data/local/tmp/hapbeat-remote-scrcpy.jar app_process / com.genymobile.scrcpy.Server 4.1 " +
                "scid=0000abcd log_level=warn tunnel_forward=true audio=false control=false cleanup=true " +
                "video_codec=h264 max_size=1024 video_bit_rate=8000000 max_fps=30 " +
                "send_device_meta=false stay_awake=false power_on=false crop=1832:1920:0:0",
            cmd,
        )
    }
}
