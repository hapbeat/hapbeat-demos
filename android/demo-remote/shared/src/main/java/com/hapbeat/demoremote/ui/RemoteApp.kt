package com.hapbeat.demoremote.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import com.hapbeat.demoremote.RemotePlatform
import com.hapbeat.demoremote.RemoteViewModel

enum class Screen { REMOTE, SETTINGS, LICENSES, PRESET, HUB_SETTINGS, DEMO_SETTINGS, PLATFORM_EXTRA }

val LocalRemotePlatform = staticCompositionLocalOf<RemotePlatform> { error("RemoteApp provides the platform") }

/** The app's screens and dialogs, with one navigation on both platforms. */
@Composable
fun RemoteApp(vm: RemoteViewModel) {
    CompositionLocalProvider(LocalRemotePlatform provides vm.platform) {
        var screen by rememberSaveable { mutableStateOf(Screen.REMOTE) }
        var editingPreset by rememberSaveable { mutableStateOf<Int?>(null) }
        var demoSettingsId by rememberSaveable { mutableStateOf<String?>(null) }
        // The main screen's tab stays when another screen is opened and closed.
        var remoteTab by rememberSaveable { mutableIntStateOf(0) }
        PlatformBackHandler(enabled = screen != Screen.REMOTE) {
            screen = if (screen == Screen.LICENSES) Screen.SETTINGS else Screen.REMOTE
        }
        when (screen) {
            Screen.REMOTE -> RemoteScreen(
                vm,
                onOpenSettings = { screen = Screen.SETTINGS },
                onEditPreset = { editingPreset = it; screen = Screen.PRESET },
                onOpenHubSettings = { screen = Screen.HUB_SETTINGS },
                tab = remoteTab,
                onTab = { remoteTab = it },
                onOpenDemoSettings = { demoSettingsId = it; screen = Screen.DEMO_SETTINGS },
                onOpenPlatformExtra = { screen = Screen.PLATFORM_EXTRA },
            )
            Screen.PRESET -> PresetEditScreen(vm, editingPreset ?: 1, onDone = { screen = Screen.REMOTE })
            Screen.HUB_SETTINGS -> HubSettingsScreen(vm, onDone = { screen = Screen.REMOTE })
            Screen.DEMO_SETTINGS -> DemoSettingsScreen(vm, demoSettingsId ?: "", onDone = { screen = Screen.REMOTE })
            Screen.SETTINGS -> SettingsScreen(
                vm,
                onBack = { screen = Screen.REMOTE },
                onOpenLicenses = { screen = Screen.LICENSES },
            )
            Screen.LICENSES -> LicenseScreen(onBack = { screen = Screen.SETTINGS })
            Screen.PLATFORM_EXTRA -> PlatformExtraScreen(vm, onBack = { screen = Screen.REMOTE })
        }
        if (vm.usbDialogOpen) UsbAdbDialog(vm)
        PresetImportDialog(vm)
    }
}
