package com.hapbeat.demoremote.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.asComposeImageBitmap
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.ApplicationScope
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPlacement
import androidx.compose.ui.window.WindowPosition
import androidx.compose.ui.window.rememberWindowState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.KeyValueStore
import com.hapbeat.demoremote.mirror.MirrorFrames
import com.hapbeat.demoremote.mirror.MirrorLayout
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import org.jetbrains.skia.Bitmap
import org.jetbrains.skia.ColorAlphaType
import org.jetbrains.skia.ColorType
import org.jetbrains.skia.ImageInfo
import java.awt.GraphicsDevice
import java.awt.GraphicsEnvironment
import java.awt.Point
import java.awt.Toolkit
import java.awt.image.BufferedImage

/** One connected display as the 表示先 list shows it. */
data class Monitor(val id: String, val label: String, val primary: Boolean)

object MonitorChoice {
    /** The saved display while it is connected, else the first one that is not the primary, else the first. */
    fun resolve(monitors: List<Monitor>, savedId: String?): Monitor? =
        monitors.firstOrNull { it.id == savedId } ?: monitors.firstOrNull { !it.primary } ?: monitors.firstOrNull()

    /** [index] from 0, in the order of `GraphicsEnvironment`. */
    fun label(index: Int, width: Int, height: Int): String = "ディスプレイ ${index + 1}（$width×$height）"
}

/** What the desktop adds around the shared screens: the frames of the one mirror stream and the full-screen output. */
class DesktopState(private val store: KeyValueStore, val frames: MirrorFrames, private val scope: CoroutineScope, private val defaultApkDir: String) {
    private var installModel: InstallModel? = null

    /** The install screen's state; it outlives the screen, so an install keeps running while another screen is open. */
    fun installModel(vm: RemoteViewModel): InstallModel =
        installModel ?: InstallModel(vm, store, scope, defaultApkDir).also { installModel = it }

    var fullScreen by mutableStateOf(false)
        private set
    /** Fill the display (centre crop) instead of fitting with black bars. */
    var fill by mutableStateOf(store.getBoolean(KEY_FILL, false))
        private set
    var monitors by mutableStateOf(listMonitors())
        private set
    private var savedMonitorId by mutableStateOf(store.getString(KEY_MONITOR))

    val monitor: Monitor? get() = MonitorChoice.resolve(monitors, savedMonitorId)

    fun refreshMonitors() {
        val now = listMonitors()
        if (now != monitors) monitors = now
    }

    fun chooseMonitor(id: String) {
        savedMonitorId = id
        store.write { putString(KEY_MONITOR, id) }
    }

    fun chooseFill(value: Boolean) {
        fill = value
        store.write { putBoolean(KEY_FILL, value) }
    }

    /** Turning it on turns the mirror on as well: one stream serves the preview and this output. */
    fun changeFullScreen(vm: RemoteViewModel, on: Boolean) {
        fullScreen = on
        if (on && !vm.mirrorEnabled) vm.changeMirrorEnabled(true)
    }

    fun device(): GraphicsDevice? {
        val id = monitor?.id ?: return null
        return GraphicsEnvironment.getLocalGraphicsEnvironment().screenDevices.firstOrNull { it.iDstring == id }
    }

    private fun listMonitors(): List<Monitor> {
        val environment = GraphicsEnvironment.getLocalGraphicsEnvironment()
        if (java.awt.GraphicsEnvironment.isHeadless()) return emptyList()
        val primary = environment.defaultScreenDevice
        return environment.screenDevices.mapIndexed { i, device ->
            Monitor(device.iDstring, MonitorChoice.label(i, device.displayMode.width, device.displayMode.height), device == primary)
        }
    }

    private companion object {
        const val KEY_MONITOR = "desktop_fullscreen_monitor"
        const val KEY_FILL = "desktop_fullscreen_fill"
    }
}

val LocalDesktopState = staticCompositionLocalOf<DesktopState> { error("main provides the desktop state") }

/** Draws the newest frame on one remembered Bitmap, at each new frame ([MirrorFrames.revision] read while drawing). */
@Composable
fun MirrorVideo(frames: MirrorFrames, fill: Boolean, modifier: Modifier) {
    val bitmap = remember { Bitmap() }
    DisposableEffect(bitmap) { onDispose { bitmap.close() } }
    Canvas(modifier) {
        frames.revision
        val frameSize = frames.read { width, height, bytes ->
            bitmap.installPixels(ImageInfo(width, height, ColorType.BGRA_8888, ColorAlphaType.OPAQUE), bytes, width * 4)
            width to height
        } ?: return@Canvas
        if (size == Size.Zero) return@Canvas
        val place = MirrorLayout.place(frameSize.first, frameSize.second, size.width.toInt(), size.height.toInt(), fill)
        drawImage(
            bitmap.asComposeImageBitmap(),
            srcOffset = IntOffset(place.srcX, place.srcY), srcSize = IntSize(place.srcWidth, place.srcHeight),
            dstOffset = IntOffset(place.dstX, place.dstY), dstSize = IntSize(place.dstWidth, place.dstHeight),
            filterQuality = FilterQuality.Medium,
        )
    }
}

/** 全画面表示, 表示先 and 全体 / 画面いっぱい under the mirror's heading; fixed widths, so changing labels move nothing. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MirrorFullScreenControls(vm: RemoteViewModel) {
    val state = LocalDesktopState.current
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("全画面表示", style = MaterialTheme.typography.bodyMedium)
            Switch(checked = state.fullScreen, onCheckedChange = { state.changeFullScreen(vm, it) })
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("表示先", style = MaterialTheme.typography.bodyMedium)
            var open by remember { mutableStateOf(false) }
            Box {
                OutlinedButton(onClick = { state.refreshMonitors(); open = true }, modifier = Modifier.width(232.dp)) {
                    Text(state.monitor?.label ?: "ディスプレイなし", maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                    state.monitors.forEach { monitor ->
                        DropdownMenuItem(text = { Text(monitor.label) }, onClick = { state.chooseMonitor(monitor.id); open = false })
                    }
                }
            }
            FilterChip(selected = !state.fill, onClick = { state.chooseFill(false) }, label = { Text("全体") })
            FilterChip(selected = state.fill, onClick = { state.chooseFill(true) }, label = { Text("画面いっぱい") })
        }
    }
}

private val HiddenCursor = PointerIcon(
    Toolkit.getDefaultToolkit().createCustomCursor(BufferedImage(16, 16, BufferedImage.TYPE_INT_ARGB), Point(0, 0), "hidden"),
)

/**
 * The audience's output: an undecorated window exactly over the chosen display, showing the same frames as the preview.
 * Esc or a double click closes it; the main window stays usable.
 */
@Composable
fun ApplicationScope.MirrorFullScreenWindow(vm: RemoteViewModel, state: DesktopState, mainWindow: () -> java.awt.Window?) {
    val windowState = rememberWindowState(placement = WindowPlacement.Floating, position = WindowPosition(0.dp, 0.dp))
    var onTop by remember { mutableStateOf(false) }
    Window(
        onCloseRequest = { state.changeFullScreen(vm, false) },
        state = windowState,
        title = "Demo Remote 全画面",
        undecorated = true,
        resizable = false,
        // Over the taskbar of the audience's display; never over the main window when both share a display.
        alwaysOnTop = onTop,
        onKeyEvent = {
            if (it.type == KeyEventType.KeyDown && it.key == Key.Escape) { state.changeFullScreen(vm, false); true } else false
        },
    ) {
        val monitorId = state.monitor?.id
        LaunchedEffect(monitorId) {
            state.device()?.let { window.bounds = it.defaultConfiguration.bounds }
        }
        // A display plugged or unplugged, or the main window moved to another one: followed while this is open.
        LaunchedEffect(Unit) {
            var first = true
            while (true) {
                state.refreshMonitors()
                val main = mainWindow()
                onTop = main != null && main.graphicsConfiguration.device != state.device()
                // Opening this must not take the focus from the operator's window.
                if (first && onTop) main?.toFront()
                first = false
                delay(1_000)
            }
        }
        var moves by remember { mutableIntStateOf(0) }
        var cursorHidden by remember { mutableStateOf(false) }
        LaunchedEffect(moves) {
            cursorHidden = false
            delay(CURSOR_HIDE_MS)
            cursorHidden = true
        }
        Box(
            Modifier.fillMaxSize().background(Color.Black)
                .pointerHoverIcon(if (cursorHidden) HiddenCursor else PointerIcon.Default)
                .pointerInput(Unit) { awaitPointerEventScope { while (true) { awaitPointerEvent(); moves++ } } }
                .pointerInput(Unit) { detectTapGestures(onDoubleTap = { state.changeFullScreen(vm, false) }) },
        ) {
            if (vm.mirrorEnabled && vm.mirrorVideoSize != null) MirrorVideo(state.frames, state.fill, Modifier.fillMaxSize())
            if (vm.mirrorStatus.startsWith("ミラーエラー")) {
                Text(
                    vm.mirrorStatus,
                    Modifier.align(Alignment.BottomEnd).padding(8.dp),
                    color = Color.White.copy(alpha = 0.7f),
                    style = MaterialTheme.typography.labelSmall,
                    maxLines = 1,
                )
            }
        }
    }
}

private const val CURSOR_HIDE_MS = 2_000L
