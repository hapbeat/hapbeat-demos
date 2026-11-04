package com.hapbeat.demoremote.mirror

import org.bytedeco.ffmpeg.avcodec.AVCodecContext
import org.bytedeco.ffmpeg.avcodec.AVPacket
import org.bytedeco.ffmpeg.avutil.AVDictionary
import org.bytedeco.ffmpeg.avutil.AVFrame
import org.bytedeco.ffmpeg.global.avcodec
import org.bytedeco.ffmpeg.global.avutil
import org.bytedeco.ffmpeg.global.swscale
import org.bytedeco.ffmpeg.swscale.SwsContext
import org.bytedeco.ffmpeg.swscale.SwsFilter
import org.bytedeco.javacpp.BytePointer
import org.bytedeco.javacpp.DoublePointer
import org.bytedeco.javacpp.IntPointer
import org.bytedeco.javacpp.PointerPointer
import java.io.IOException

/**
 * Holds a config packet (SPS/PPS) and prepends it to the next media packet, as the scrcpy client does: the decoder gets
 * them as one packet.
 */
class PacketMerger {
    private var config: ByteArray? = null

    /** The bytes to decode for [packet]; null for a config packet, which is kept for the next one. */
    fun merge(packet: ScrcpyPacket.Media): ByteArray? {
        if (packet.config) {
            config = packet.data
            return null
        }
        val pending = config ?: return packet.data
        config = null
        return pending + packet.data
    }
}

/**
 * H.264 to BGRA with FFmpeg (avcodec + swscale directly), on the mirror thread. Only the newest frame is kept in [frames]:
 * a slow screen drops frames instead of queueing them. Native code is touched only from [configure] on, so the app starts
 * without loading FFmpeg.
 */
class FfmpegVideoSink(private val frames: MirrorFrames) : VideoSink {
    private var context: AVCodecContext? = null
    private var packet: AVPacket? = null
    private var frame: AVFrame? = null
    private var scaler: SwsContext? = null
    private var scalerKey = Triple(0, 0, -1)
    private var output: BytePointer? = null
    private var outputSize = 0
    private val merger = PacketMerger()
    // swscale reads four plane pointers and strides from each array.
    private val sourcePlanes by lazy { PointerPointer<BytePointer>(PLANES.toLong()) }
    private val outputPlanes by lazy { PointerPointer<BytePointer>(PLANES.toLong()).also { for (i in 0 until PLANES) it.put(i.toLong(), null as BytePointer?) } }
    private val outputStrides by lazy { IntPointer(PLANES.toLong()).also { for (i in 0 until PLANES) it.put(i.toLong(), 0) } }

    @Volatile var packetsReceived = 0L
        private set
    @Volatile var sendErrors = 0L
        private set
    @Volatile var framesDecoded = 0L
        private set
    @Volatile var decodeNanos = 0L
        private set

    override fun configure(width: Int, height: Int) {
        release()
        frames.clear()
        try {
            avutil.av_log_set_level(avutil.AV_LOG_ERROR)
            val codec = avcodec.avcodec_find_decoder(avcodec.AV_CODEC_ID_H264) ?: throw IOException("H.264 デコーダーがありません")
            val c = avcodec.avcodec_alloc_context3(codec) ?: throw IOException("FFmpeg の初期化に失敗しました")
            c.flags(c.flags() or avcodec.AV_CODEC_FLAG_LOW_DELAY)
            // Slice threads add no frame of delay; frame threads would.
            c.thread_type(FF_THREAD_SLICE)
            c.thread_count(4)
            val opened = avcodec.avcodec_open2(c, codec, null as AVDictionary?)
            if (opened < 0) throw IOException("H.264 デコーダーを開けません（$opened）")
            context = c
            packet = avcodec.av_packet_alloc()
            frame = avutil.av_frame_alloc()
        } catch (e: LinkageError) {
            throw IOException("FFmpeg を読み込めません（${e.javaClass.simpleName}: ${e.message}）", e)
        }
    }

    /** Decoding is synchronous here, so it is never congested: a slow decoder backs up the TCP stream instead. */
    override fun queue(packet: ScrcpyPacket.Media): Boolean {
        val c = context ?: return true // no session yet: nothing to decode into
        val pkt = this.packet ?: return true
        packetsReceived++
        val data = merger.merge(packet) ?: return true
        val started = System.nanoTime()
        if (avcodec.av_new_packet(pkt, data.size) < 0) throw IOException("FFmpeg のメモリ確保に失敗しました")
        pkt.data().position(0).put(data, 0, data.size)
        pkt.pts(packet.ptsUs)
        pkt.dts(packet.ptsUs)
        if (packet.keyFrame) pkt.flags(pkt.flags() or avcodec.AV_PKT_FLAG_KEY)
        // A broken packet is skipped: the stream recovers at the next keyframe.
        if (avcodec.avcodec_send_packet(c, pkt) < 0) sendErrors++
        avcodec.av_packet_unref(pkt)
        decodeNanos += System.nanoTime() - started
        return true
    }

    override fun drain() {
        val c = context ?: return
        val f = frame ?: return
        while (true) {
            val started = System.nanoTime()
            if (avcodec.avcodec_receive_frame(c, f) < 0) return
            publish(f)
            avutil.av_frame_unref(f)
            decodeNanos += System.nanoTime() - started
            framesDecoded++
        }
    }

    private fun publish(f: AVFrame) {
        val width = f.width()
        val height = f.height()
        val key = Triple(width, height, f.format())
        if (scaler == null || key != scalerKey) {
            scaler?.let { swscale.sws_freeContext(it) }
            scaler = swscale.sws_getContext(
                width, height, f.format(), width, height, avutil.AV_PIX_FMT_BGRA, swscale.SWS_FAST_BILINEAR,
                null as SwsFilter?, null as SwsFilter?, null as DoublePointer?,
            ) ?: throw IOException("swscale を開けません")
            scalerKey = key
        }
        val rowBytes = width * 4
        val size = rowBytes * height
        if (output == null || outputSize != size) {
            output?.let { avutil.av_free(it) }
            output = BytePointer(avutil.av_malloc(size.toLong()))
            outputSize = size
        }
        val out = output!!
        for (i in 0 until PLANES) sourcePlanes.put(i.toLong(), f.data(i))
        outputPlanes.put(0L, out)
        outputStrides.put(0L, rowBytes)
        swscale.sws_scale(scaler, sourcePlanes, f.linesize(), 0, height, outputPlanes, outputStrides)
        val bytes = frames.buffer(size)
        out.position(0).get(bytes, 0, size)
        frames.publish(width, height, bytes)
    }

    override fun release() {
        context?.let { avcodec.avcodec_free_context(it) }
        context = null
        packet?.let { avcodec.av_packet_free(it) }
        packet = null
        frame?.let { avutil.av_frame_free(it) }
        frame = null
        scaler?.let { swscale.sws_freeContext(it) }
        scaler = null
        output?.let { avutil.av_free(it) }
        output = null
        outputSize = 0
    }

    companion object {
        /** FFmpeg's FF_THREAD_SLICE (not exposed by the preset). */
        private const val FF_THREAD_SLICE = 2
        private const val PLANES = 4

        /** FFmpeg's own statement of its build's licence ("LGPL version 3 or later", ...). */
        fun licenseText(): String = avcodec.avcodec_license().getString()
    }
}
