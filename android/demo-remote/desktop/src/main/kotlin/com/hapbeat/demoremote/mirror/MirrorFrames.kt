package com.hapbeat.demoremote.mirror

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue

/**
 * The newest decoded frame (BGRA), shared by every view of the one mirror stream. The decoder fills [buffer] and hands it to
 * [publish]; a view reads [revision] to redraw at each frame and copies the pixels inside [read].
 */
class MirrorFrames {
    private class Frame(val width: Int, val height: Int, val bytes: ByteArray)

    private val lock = Any()
    private var latest: Frame? = null
    private var spare: ByteArray? = null

    var revision by mutableIntStateOf(0)
        private set

    /** A buffer of [size] bytes the views are not reading. */
    fun buffer(size: Int): ByteArray = synchronized(lock) {
        spare?.takeIf { it.size == size }?.also { spare = null } ?: ByteArray(size)
    }

    fun publish(width: Int, height: Int, bytes: ByteArray) {
        synchronized(lock) {
            spare = latest?.bytes
            latest = Frame(width, height, bytes)
        }
        revision++
    }

    /** [block] with the newest frame under the lock, or null when there is none. */
    fun <T> read(block: (width: Int, height: Int, bytes: ByteArray) -> T): T? = synchronized(lock) {
        latest?.let { block(it.width, it.height, it.bytes) }
    }

    fun clear() {
        synchronized(lock) { latest = null }
        revision++
    }
}

/** Pixel rectangles of one draw of a frame: which part of the frame goes where in the view. */
data class MirrorPlacement(
    val srcX: Int, val srcY: Int, val srcWidth: Int, val srcHeight: Int,
    val dstX: Int, val dstY: Int, val dstWidth: Int, val dstHeight: Int,
)

object MirrorLayout {
    /** Fit: the whole frame with black bars. [fill]: no bars, the frame's centre cropped to the view's shape. */
    fun place(frameWidth: Int, frameHeight: Int, viewWidth: Int, viewHeight: Int, fill: Boolean): MirrorPlacement {
        // Compared as frame W / frame H > view W / view H, without rounding.
        val frameWider = frameWidth.toLong() * viewHeight > viewWidth.toLong() * frameHeight
        if (fill) {
            val srcWidth = if (frameWider) (frameHeight.toLong() * viewWidth / viewHeight).toInt() else frameWidth
            val srcHeight = if (frameWider) frameHeight else (frameWidth.toLong() * viewHeight / viewWidth).toInt()
            return MirrorPlacement((frameWidth - srcWidth) / 2, (frameHeight - srcHeight) / 2, srcWidth, srcHeight, 0, 0, viewWidth, viewHeight)
        }
        val dstWidth = if (frameWider) viewWidth else (viewHeight.toLong() * frameWidth / frameHeight).toInt()
        val dstHeight = if (frameWider) (viewWidth.toLong() * frameHeight / frameWidth).toInt() else viewHeight
        return MirrorPlacement(0, 0, frameWidth, frameHeight, (viewWidth - dstWidth) / 2, (viewHeight - dstHeight) / 2, dstWidth, dstHeight)
    }
}
