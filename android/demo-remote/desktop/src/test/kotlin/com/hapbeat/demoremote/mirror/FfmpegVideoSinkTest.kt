package com.hapbeat.demoremote.mirror

import org.bytedeco.ffmpeg.avutil.AVDictionary
import org.bytedeco.ffmpeg.global.avcodec
import org.bytedeco.ffmpeg.global.avutil
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class FfmpegVideoSinkTest {
    /** A solid red picture through FFmpeg's own H.264 encoder, then through the sink the way a mirror session uses it. */
    @Test
    fun decodesEncodedFramesToBgra() {
        val width = 320
        val height = 240
        val packets = encodeSolidRed(width, height, frameCount = 12)
        assertTrue("encoder produced packets", packets.isNotEmpty())

        val frames = MirrorFrames()
        val sink = FfmpegVideoSink(frames)
        sink.configure(width, height)
        packets.forEachIndexed { i, bytes ->
            assertTrue(sink.queue(ScrcpyPacket.Media(config = false, keyFrame = i == 0, ptsUs = i * 33_333L, data = bytes)))
            sink.drain()
        }
        assertEquals(0L, sink.sendErrors)
        assertTrue("decoded ${sink.framesDecoded}", sink.framesDecoded > 0)
        val center = frames.read { w, h, bytes ->
            assertEquals(width, w)
            assertEquals(height, h)
            val offset = (h / 2 * w + w / 2) * 4
            listOf(bytes[offset], bytes[offset + 1], bytes[offset + 2]).map { it.toInt() and 0xff }
        }!!
        val (blue, green, red) = center
        assertTrue("BGR $center", red > 180 && green < 60 && blue < 60)
        sink.release()
        sink.release()
    }

    @Test
    fun theLicenceIsTheLgplBuild() {
        println("FFmpeg licence: ${FfmpegVideoSink.licenseText()}")
        assertTrue(FfmpegVideoSink.licenseText(), FfmpegVideoSink.licenseText().startsWith("LGPL"))
    }

    @Test
    fun configPacketIsPrependedToTheNextMediaPacketOnly() {
        val merger = PacketMerger()
        val config = ScrcpyPacket.Media(config = true, keyFrame = false, ptsUs = 0, data = byteArrayOf(1, 2))
        val key = ScrcpyPacket.Media(config = false, keyFrame = true, ptsUs = 1, data = byteArrayOf(3))
        val next = ScrcpyPacket.Media(config = false, keyFrame = false, ptsUs = 2, data = byteArrayOf(4))
        assertNull(merger.merge(config))
        assertArrayEquals(byteArrayOf(1, 2, 3), merger.merge(key))
        assertArrayEquals(byteArrayOf(4), merger.merge(next))
    }

    private fun encodeSolidRed(width: Int, height: Int, frameCount: Int): List<ByteArray> {
        val codec = avcodec.avcodec_find_encoder_by_name("libopenh264")
        val c = avcodec.avcodec_alloc_context3(codec)
        c.width(width)
        c.height(height)
        c.pix_fmt(avutil.AV_PIX_FMT_YUV420P)
        c.time_base().num(1)
        c.time_base().den(30)
        c.bit_rate(2_000_000)
        assertEquals(0, avcodec.avcodec_open2(c, codec, null as AVDictionary?))
        val frame = avutil.av_frame_alloc()
        frame.format(avutil.AV_PIX_FMT_YUV420P)
        frame.width(width)
        frame.height(height)
        assertEquals(0, avutil.av_frame_get_buffer(frame, 0))
        val packet = avcodec.av_packet_alloc()
        val out = mutableListOf<ByteArray>()
        fun receive() {
            while (avcodec.avcodec_receive_packet(c, packet) == 0) {
                val bytes = ByteArray(packet.size())
                packet.data().position(0).get(bytes)
                out += bytes
                avcodec.av_packet_unref(packet)
            }
        }
        repeat(frameCount) { i ->
            avutil.av_frame_make_writable(frame)
            fill(frame.data(0), frame.linesize(0), height, 81)
            fill(frame.data(1), frame.linesize(1), height / 2, 90)
            fill(frame.data(2), frame.linesize(2), height / 2, 240)
            frame.pts(i.toLong())
            assertEquals(0, avcodec.avcodec_send_frame(c, frame))
            receive()
        }
        avcodec.avcodec_send_frame(c, null)
        receive()
        avcodec.avcodec_free_context(c)
        return out
    }

    private fun fill(plane: org.bytedeco.javacpp.BytePointer, stride: Int, rows: Int, value: Int) {
        val row = ByteArray(stride) { value.toByte() }
        for (y in 0 until rows) plane.position((y * stride).toLong()).put(row, 0, stride)
        plane.position(0)
    }
}
