package com.hapbeat.demoremote.desktop

import androidx.compose.ui.ImageComposeScene
import androidx.compose.ui.unit.Density
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.DesktopPlatform
import com.hapbeat.demoremote.QuestState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DesktopUsbBridge
import com.hapbeat.demoremote.net.LocalIpv4
import com.hapbeat.demoremote.net.PlatformNetwork
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.RemoteApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import org.jetbrains.skia.EncodedImageFormat
import java.io.File
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
        vm.quests.add(QuestState("192.168.0.37", "HMD #1", model = "Quest 3S", adb = AdbState.CONNECTED, respondedLastRound = true, battery = 80, lastDemoId = "demo_hub"))
        vm.selectQuest("192.168.0.37")
        listOf(1400 to 860, 900 to 700).forEach { (width, height) ->
            val scene = ImageComposeScene(width, height, Density(1f)) { AppTheme { RemoteApp(vm) } }
            scene.render(0)
            val image = scene.render(1_000_000_000)
            val data = image.encodeToData(EncodedImageFormat.PNG) ?: error("PNG encoding failed")
            File(dir, "main-${width}x$height.png").writeBytes(data.bytes)
            scene.close()
            println("rendered main-${width}x$height.png")
        }
        scope.cancel()
        0
    }
}
