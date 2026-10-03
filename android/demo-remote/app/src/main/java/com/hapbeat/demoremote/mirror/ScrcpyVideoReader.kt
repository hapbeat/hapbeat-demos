package com.hapbeat.demoremote.mirror

import java.io.EOFException
import java.io.IOException
import java.io.InputStream

sealed interface ScrcpyPacket {
    /** Stream (re)configuration: the decoder must be (re)created with this size. */
    data class Session(val width: Int, val height: Int) : ScrcpyPacket

    class Media(val config: Boolean, val keyFrame: Boolean, val ptsUs: Long, val data: ByteArray) : ScrcpyPacket
}

/**
 * Reads the scrcpy v4.1 video socket (tunnel_forward=true, send_device_meta=false).
 * All integers are big-endian: dummy byte, codec id (u32), then 12-byte packet headers.
 */
class ScrcpyVideoReader(private val input: InputStream) {
    private val header = ByteArray(12)

    /** Consumes the dummy byte and returns the codec id. */
    fun readHeader(): Int {
        if (input.read() < 0) throw EOFException("stream closed before dummy byte")
        val codec = ByteArray(4)
        readFully(codec)
        return u32(codec, 0).toInt()
    }

    fun readPacket(): ScrcpyPacket {
        readFully(header)
        if (header[0].toInt() and 0x80 != 0) {
            return ScrcpyPacket.Session(width = u32(header, 4).toInt(), height = u32(header, 8).toInt())
        }
        val ptsAndFlags = u64(header, 0)
        val size = u32(header, 8)
        if (size <= 0 || size > MAX_PACKET_SIZE) throw IOException("invalid packet size $size")
        val data = ByteArray(size.toInt())
        readFully(data)
        return ScrcpyPacket.Media(
            config = ptsAndFlags and FLAG_CONFIG != 0L,
            keyFrame = ptsAndFlags and FLAG_KEY_FRAME != 0L,
            ptsUs = ptsAndFlags and PTS_MASK,
            data = data,
        )
    }

    private fun readFully(buffer: ByteArray) {
        var off = 0
        while (off < buffer.size) {
            val n = input.read(buffer, off, buffer.size - off)
            if (n < 0) throw EOFException("stream closed")
            off += n
        }
    }

    companion object {
        const val CODEC_H264 = 0x68323634
        private const val FLAG_CONFIG = 1L shl 62
        private const val FLAG_KEY_FRAME = 1L shl 61
        private const val PTS_MASK = (1L shl 61) - 1
        private const val MAX_PACKET_SIZE = 16L * 1024 * 1024

        private fun u32(b: ByteArray, off: Int): Long =
            ((b[off].toLong() and 0xff) shl 24) or ((b[off + 1].toLong() and 0xff) shl 16) or
                ((b[off + 2].toLong() and 0xff) shl 8) or (b[off + 3].toLong() and 0xff)

        private fun u64(b: ByteArray, off: Int): Long = (u32(b, off) shl 32) or u32(b, off + 4)
    }
}
