package com.hapbeat.demoremote.desktop

import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import com.hapbeat.demoremote.DesktopPlatform
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DesktopUsbBridge
import com.hapbeat.demoremote.mirror.FfmpegVideoSink
import com.hapbeat.demoremote.mirror.MirrorFrames
import com.hapbeat.demoremote.net.DesktopNetwork
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.DesktopState
import com.hapbeat.demoremote.ui.LocalDesktopState
import com.hapbeat.demoremote.ui.MirrorFullScreenWindow
import com.hapbeat.demoremote.ui.RemoteApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import com.sun.jna.platform.win32.Kernel32
import com.sun.jna.platform.win32.WinBase
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
        "--selftest-mirror" -> exitProcess(SelfTest.mirror(rest[1], rest[2].toInt(), File(rest[3])))
    }
    application {
        val scope = remember { CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate) }
        val store = remember {
            dataDir.mkdirs()
            PropertiesStore(File(dataDir, "settings.properties"))
        }
        val desktop = remember { DesktopState(store, MirrorFrames()) }
        val vm = remember {
            val platform = DesktopPlatform(
                versionName = BuildInfo.VERSION,
                dataDir = dataDir,
                settings = store,
                network = DesktopNetwork(),
                usb = DesktopUsbBridge(),
                mirrorSinkFactory = { FfmpegVideoSink(desktop.frames) },
                existingAdbKeyDir = File(System.getProperty("user.home"), ".android"),
            )
            RemoteViewModel(platform, scope)
        }
        DisposableEffect(Unit) {
            vm.onForeground()
            onDispose { }
        }
        val state = rememberWindowState(size = DpSize(1400.dp, 860.dp))
        var mainWindow by remember { mutableStateOf<java.awt.Window?>(null) }
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
            onPreviewKeyEvent = {
                if (it.type == KeyEventType.KeyDown && it.key == Key.F11) {
                    desktop.changeFullScreen(vm, !desktop.fullScreen)
                    true
                } else false
            },
        ) {
            mainWindow = window
            LaunchedEffect(Unit) { window.minimumSize = Dimension(900, 640) }
            // Nothing to show on the audience's display once the mirror is turned off.
            LaunchedEffect(vm.mirrorEnabled) { if (!vm.mirrorEnabled) desktop.changeFullScreen(vm, false) }
            // ES_CONTINUOUS applies to this thread (the UI thread), which lives as long as the app.
            LaunchedEffect(vm.keepScreenOn) { KeepAwake.set(vm.keepScreenOn) }
            AppTheme { CompositionLocalProvider(LocalDesktopState provides desktop) { RemoteApp(vm) } }
        }
        if (desktop.fullScreen) AppTheme { MirrorFullScreenWindow(vm, desktop) { mainWindow } }
    }
}

/** 「画面を常時 ON にする」: Windows keeps the display on while this thread asks for it. */
private object KeepAwake {
    fun set(on: Boolean) {
        val flags = if (on) WinBase.ES_CONTINUOUS or WinBase.ES_DISPLAY_REQUIRED else WinBase.ES_CONTINUOUS
        runCatching { Kernel32.INSTANCE.SetThreadExecutionState(flags) }
    }
}
