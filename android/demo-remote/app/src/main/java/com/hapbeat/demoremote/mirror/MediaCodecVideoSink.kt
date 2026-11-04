package com.hapbeat.demoremote.mirror

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.view.Surface

/** Renders the H.264 stream to [surface] with MediaCodec. */
class MediaCodecVideoSink(private val surface: Surface) : VideoSink {
    private var codec: MediaCodec? = null
    private val info = MediaCodec.BufferInfo()

    override fun configure(width: Int, height: Int) {
        codec?.let { it.stop(); it.release() }
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        codec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC).apply {
            configure(format, surface, null, 0)
            start()
        }
    }

    /** Returns false if no input buffer became free within 50 ms (decoder is congested). */
    override fun queue(packet: ScrcpyPacket.Media): Boolean {
        val c = codec ?: return true // no session yet: nothing to decode into
        val index = c.dequeueInputBuffer(INPUT_TIMEOUT_US)
        if (index < 0) return false
        val buffer = c.getInputBuffer(index) ?: return false
        buffer.clear()
        buffer.put(packet.data)
        var flags = 0
        if (packet.config) flags = flags or MediaCodec.BUFFER_FLAG_CODEC_CONFIG
        if (packet.keyFrame) flags = flags or MediaCodec.BUFFER_FLAG_KEY_FRAME
        c.queueInputBuffer(index, 0, packet.data.size, if (packet.config) 0 else packet.ptsUs, flags)
        return true
    }

    override fun drain() {
        val c = codec ?: return
        while (true) {
            val index = c.dequeueOutputBuffer(info, 0)
            if (index >= 0) c.releaseOutputBuffer(index, true)
            else if (index == MediaCodec.INFO_TRY_AGAIN_LATER) break
            // INFO_OUTPUT_FORMAT_CHANGED / INFO_OUTPUT_BUFFERS_CHANGED: keep draining
        }
    }

    override fun release() {
        codec?.let {
            try {
                it.stop()
            } catch (_: IllegalStateException) {
                // Already in error/released state; release() below still frees it.
            }
            it.release()
        }
        codec = null
    }

    private companion object {
        const val INPUT_TIMEOUT_US = 50_000L
    }
}
