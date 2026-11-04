package com.hapbeat.demoremote.desktop

import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import com.hapbeat.demoremote.DesktopPlatform
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DesktopUsbBridge
import com.hapbeat.demoremote.net.DesktopNetwork
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.RemoteApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import java.awt.Dimension
import java.io.File
import kotlin.system.exitProcess

/** Outside AppData: the host's packaging may redirect AppData, and it survives installs and upgrades. */
private fun defaultDataDir() = File(System.getProperty("user.home"), ".hapbeat-demo-remote")

fun windowTitle(): String = buildString {
    append("Demo Remote ${BuildInfo.VERSION}（${BuildInfo.BRANCH} ${BuildInfo.COMMIT}）")
    if (BuildInfo.DIRTY) append("　未コミットの変更を含むビルド")
}

fun main(args: Array<String>) {
    val rest = args.toMutableList()
    val dataDir = rest.firstOrNull { it.startsWith("--data-dir=") }?.also { rest.remove(it) }?.removePrefix("--data-dir=")?.let { File(it) } ?: defaultDataDir()
    when (rest.firstOrNull()) {
        "--selftest-render" -> exitProcess(SelfTest.render(File(rest[1])))
    }
    application {
        val scope = remember { CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate) }
        val vm = remember {
            dataDir.mkdirs()
            val platform = DesktopPlatform(
                versionName = BuildInfo.VERSION,
                dataDir = dataDir,
                settings = PropertiesStore(File(dataDir, "settings.properties")),
                network = DesktopNetwork(),
                usb = DesktopUsbBridge(),
                mirrorSinkFactory = null,
                existingAdbKeyDir = File(System.getProperty("user.home"), ".android"),
            )
            RemoteViewModel(platform, scope)
        }
        DisposableEffect(Unit) {
            vm.onForeground()
            onDispose { }
        }
        val state = rememberWindowState(size = DpSize(1400.dp, 860.dp))
        Window(
            onCloseRequest = {
                vm.onBackground()
                vm.close()
                scope.cancel()
                exitApplication()
            },
            title = windowTitle(),
            state = state,
            icon = painterResource("icon.png"),
        ) {
            LaunchedEffect(Unit) { window.minimumSize = Dimension(900, 640) }
            AppTheme { RemoteApp(vm) }
        }
    }
}
