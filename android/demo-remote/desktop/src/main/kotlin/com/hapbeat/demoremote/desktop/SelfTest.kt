package com.hapbeat.demoremote.desktop

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.window.application
import androidx.compose.ui.Modifier
import androidx.compose.ui.ImageComposeScene
import androidx.compose.ui.unit.Density
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.DesktopPlatform
import com.hapbeat.demoremote.QuestState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DesktopUsbBridge
import com.hapbeat.demoremote.adb.QuestAdb
import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.mirror.FfmpegVideoSink
import com.hapbeat.demoremote.mirror.MirrorFrames
import com.hapbeat.demoremote.mirror.MirrorRoute
import com.hapbeat.demoremote.mirror.MirrorSession
import com.hapbeat.demoremote.mirror.RelayRole
import com.hapbeat.demoremote.net.LocalIpv4
import com.hapbeat.demoremote.net.PlatformNetwork
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.DesktopState
import com.hapbeat.demoremote.ui.FULL_SCREEN_TITLE
import com.hapbeat.demoremote.ui.MirrorFullScreenWindow
import com.hapbeat.demoremote.ui.LocalDesktopState
import com.hapbeat.demoremote.ui.LocalRemotePlatform
import com.hapbeat.demoremote.ui.MirrorVideo
import com.hapbeat.demoremote.ui.PlatformExtraScreen
import com.hapbeat.demoremote.ui.RemoteApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import dadb.Dadb
import org.jetbrains.skia.Bitmap
import org.jetbrains.skia.ColorAlphaType
import org.jetbrains.skia.ColorType
import org.jetbrains.skia.EncodedImageFormat
import org.jetbrains.skia.Image
import org.jetbrains.skia.ImageInfo
import java.awt.GraphicsEnvironment
import java.awt.Robot
import java.io.File
import javax.imageio.ImageIO
import java.net.InetAddress
import java.nio.file.Files

/** Checks that need no window and send nothing on Demo Switch. */
object SelfTest {
    private class FixedNetwork : PlatformNetwork {
        override fun start(onChanged: () -> Unit) = Unit
        override fun stop() = Unit
        override fun localIpv4() = LocalIpv4(InetAddress.getByName("192.168.0.10"), 24)
        override fun gatewayIpv4() = "192.168.0.1"
    }

    /** The main screen offscreen with a made-up state, at two window sizes, as PNG files in [dir]. No network, no adb. */
    fun render(dir: File): Int = runBlocking(Dispatchers.Main) {
        dir.mkdirs()
        val temp = Files.createTempDirectory("demo-remote-render").toFile()
        val store = PropertiesStore(File(temp, "settings.properties"))
        store.write { putBoolean("auth_chosen", true); putBoolean("allow_unsigned", true); putBoolean("mirror_enabled", true) }
        val platform = DesktopPlatform(BuildInfo.VERSION, temp, store, FixedNetwork(), DesktopUsbBridge(), null, File(temp, "none"))
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
        val vm = RemoteViewModel(platform, scope)
        store.write { putString("desktop_apk_dir", temp.path) }
        val desktop = DesktopState(store, MirrorFrames(), scope, temp.path)
        vm.quests.add(QuestState("192.168.0.37", "HMD #1", model = "Quest 3S", adb = AdbState.CONNECTED, respondedLastRound = true, battery = 80, lastDemoId = "demo_hub"))
        vm.selectQuest("192.168.0.37")
        listOf(1400 to 860, 900 to 700).forEach { (width, height) ->
            val scene = ImageComposeScene(width, height, Density(1f)) {
                AppTheme { CompositionLocalProvider(LocalDesktopState provides desktop) { RemoteApp(vm) } }
            }
            scene.render(0)
            val image = scene.render(1_000_000_000)
            val data = image.encodeToData(EncodedImageFormat.PNG) ?: error("PNG encoding failed")
            File(dir, "main-${width}x$height.png").writeBytes(data.bytes)
            scene.close()
            println("rendered main-${width}x$height.png")
        }
        val scene = ImageComposeScene(1400, 860, Density(1f)) {
            AppTheme {
                CompositionLocalProvider(LocalRemotePlatform provides platform, LocalDesktopState provides desktop) {
                    PlatformExtraScreen(vm, onBack = {})
                }
            }
        }
        scene.render(0)
        val image = scene.render(1_000_000_000)
        File(dir, "install-1400x860.png").writeBytes((image.encodeToData(EncodedImageFormat.PNG) ?: error("PNG encoding failed")).bytes)
        scene.close()
        renderVideo(dir, "video-fit-1920x1080.png", fill = false)
        renderVideo(dir, "video-fill-1920x1080.png", fill = true)
        scope.cancel()
        0
    }

    /**
     * The mirror path only: streams from the Quest at [ip] for [seconds], then writes the last frame to [out] and the
     * numbers to `<out>.txt` (a GUI exe shows no console). Sends nothing on Demo Switch and sets no property.
     * [relay] "host": also offers the video like the app does; "join": takes it from a relaying PC like a phone does.
     */
    fun mirror(ip: String, seconds: Int, out: File, relay: String? = null): Int {
        out.absoluteFile.parentFile?.mkdirs()
        val temp = Files.createTempDirectory("demo-remote-mirror").toFile()
        val frames = MirrorFrames()
        val sink = FfmpegVideoSink(frames)
        val platform = DesktopPlatform(
            BuildInfo.VERSION, temp, PropertiesStore(File(temp, "settings.properties")), FixedNetwork(), DesktopUsbBridge(),
            null, File(System.getProperty("user.home"), ".android"),
        )
        var videoSize = "-"
        var error = ""
        var route = "-"
        var relayClients = "-"
        val role = when (relay) {
            "host" -> RelayRole.Host(ip) { Dadb.create(ip, QuestAdb.PORT, platform.loadAdbKeyPair(), QuestAdb.CONNECT_TIMEOUT_MS, 5_000) }
            "join" -> RelayRole.Join
            else -> RelayRole.None
        }
        val session = MirrorSession(
            { Dadb.create(ip, QuestAdb.PORT, platform.loadAdbKeyPair(), QuestAdb.CONNECT_TIMEOUT_MS, 0) },
            { platform.openAsset("scrcpy-server.jar") }, MirrorSettings(1920, 16_000_000, 30), sink,
            object : MirrorSession.Listener {
                override fun onVideoSize(width: Int, height: Int) { videoSize = "${width}x$height" }
                override fun onError(message: String) { error = message }
                override fun onRoute(route_: MirrorRoute) { route = route_.name }
                override fun onRelayClients(count: Int?) { relayClients = if (relayClients == "-") "$count" else "$relayClients,$count" }
            },
            role,
        )
        // A sleeping headset's display produces no frames at all: the report tells that apart from a decoder problem.
        val wakefulness = runCatching {
            Dadb.create(ip, QuestAdb.PORT, platform.loadAdbKeyPair(), QuestAdb.CONNECT_TIMEOUT_MS, 0).use { adb ->
                adb.shell("dumpsys power | grep -m1 mWakefulness=").output.trim()
            }
        }.getOrElse { it.javaClass.simpleName }
        session.start()
        val deadline = System.nanoTime() + seconds * 1_000_000_000L
        while (System.nanoTime() < deadline && error.isEmpty()) Thread.sleep(100)
        val decoded = sink.framesDecoded
        val averageMs = if (decoded == 0L) 0.0 else sink.decodeNanos / 1_000_000.0 / decoded
        // The stream opened without an error: FFmpeg's natives loaded, frames or not.
        val license = if (videoSize != "-" && error.isEmpty()) FfmpegVideoSink.licenseText() else "-"
        session.stop()
        val written = frames.read { width, height, bytes ->
            Bitmap().use { bitmap ->
                bitmap.installPixels(ImageInfo(width, height, ColorType.BGRA_8888, ColorAlphaType.OPAQUE), bytes, width * 4)
                val data = Image.makeFromBitmap(bitmap).use { it.encodeToData(EncodedImageFormat.PNG) }
                out.writeBytes(data?.bytes ?: ByteArray(0))
            }
            "${width}x$height"
        } ?: "-"
        val report = listOf(
            "wakefulness $wakefulness", "packets ${sink.packetsReceived}", "send_errors ${sink.sendErrors}", "frames $decoded", "video_size $videoSize", "last_frame $written", "average_decode_ms %.2f".format(averageMs),
            "route $route", "relay_clients $relayClients",
            "ffmpeg_license $license", "java ${System.getProperty("java.version")} ${System.getProperty("java.home")}",
            "error ${error.ifEmpty { "-" }}",
        ).joinToString("\n")
        File(out.path + ".txt").writeText(report + "\n")
        println(report)
        return if (decoded > 0) 0 else 1
    }

    /**
     * The real full-screen window on display [monitorIndex] with a made-up frame for [seconds]: writes what it finds to [out]
     * and a capture of that display to `<out>.png`. No network, no adb. Passes when the window covers exactly that display
     * and the frame's white centre mark is on screen.
     */
    fun fullScreen(monitorIndex: Int, seconds: Int, out: File): Int {
        out.absoluteFile.parentFile?.mkdirs()
        val temp = Files.createTempDirectory("demo-remote-fullscreen").toFile()
        val store = PropertiesStore(File(temp, "settings.properties"))
        store.write { putBoolean("auth_chosen", true); putBoolean("allow_unsigned", true); putBoolean("mirror_enabled", true) }
        val device = GraphicsEnvironment.getLocalGraphicsEnvironment().screenDevices[monitorIndex]
        var code = 1
        application(exitProcessOnExit = false) {
            val scope = remember { CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate) }
            val frames = remember { MirrorFrames().also { it.publish(TEST_WIDTH, TEST_HEIGHT, testFrame()) } }
            val desktop = remember { DesktopState(store, frames, scope, temp.path).also { it.chooseMonitor(device.iDstring) } }
            val vm = remember { RemoteViewModel(DesktopPlatform(BuildInfo.VERSION, temp, store, FixedNetwork(), DesktopUsbBridge(), null, File(temp, "none")), scope) }
            LaunchedEffect(Unit) { desktop.changeFullScreen(vm, true) }
            LaunchedEffect(Unit) {
                delay(seconds * 1_000L)
                val window = java.awt.Window.getWindows().first { (it as? java.awt.Frame)?.title == FULL_SCREEN_TITLE }
                val expected = device.defaultConfiguration.bounds
                val capture = Robot().createScreenCapture(expected)
                ImageIO.write(capture, "png", File(out.path + ".png"))
                val centre = capture.getRGB(capture.width / 2, capture.height / 2) and 0xffffff
                val covers = window.bounds == expected && window.graphicsConfiguration.device == device
                code = if (covers && centre == 0xffffff) 0 else 1
                out.writeText(
                    listOf(
                        "display $monitorIndex ${device.iDstring} mode ${device.displayMode.width}x${device.displayMode.height}",
                        "scale ${device.defaultConfiguration.defaultTransform.scaleX}", "expected $expected", "window ${window.bounds}",
                        "window_on_display ${window.graphicsConfiguration.device == device}", "centre_rgb %06x".format(centre), "result ${if (code == 0) "pass" else "FAIL"}",
                    ).joinToString("\n", postfix = "\n"),
                )
                scope.cancel()
                exitApplication()
            }
            if (desktop.fullScreen) AppTheme { MirrorFullScreenWindow(vm, desktop) { null } }
        }
        return code
    }

    private const val TEST_WIDTH = 1832
    private const val TEST_HEIGHT = 1920

    /** A made-up frame the size of the left eye: blue left, red right, white centre mark. */
    private fun testFrame(): ByteArray {
        val width = TEST_WIDTH
        val height = TEST_HEIGHT
        val bytes = ByteArray(width * height * 4)
        for (y in 0 until height) for (x in 0 until width) {
            val offset = (y * width + x) * 4
            val mark = kotlin.math.abs(x - width / 2) < 40 && kotlin.math.abs(y - height / 2) < 40
            bytes[offset] = (if (mark || x < width / 2) 255 else 0).toByte() // B
            bytes[offset + 1] = (if (mark) 255 else y * 255 / height).toByte() // G
            bytes[offset + 2] = (if (mark || x >= width / 2) 255 else 0).toByte() // R
            bytes[offset + 3] = 255.toByte()
        }
        return bytes
    }

    /** [testFrame] through the same drawing as the mirror. */
    private fun renderVideo(dir: File, name: String, fill: Boolean) {
        val frames = MirrorFrames()
        frames.publish(TEST_WIDTH, TEST_HEIGHT, testFrame())
        val scene = ImageComposeScene(1920, 1080, Density(1f)) { MirrorVideo(frames, fill, Modifier.fillMaxSize()) }
        scene.render(0)
        File(dir, name).writeBytes((scene.render(1_000_000_000).encodeToData(EncodedImageFormat.PNG) ?: error("PNG encoding failed")).bytes)
        scene.close()
    }
}
