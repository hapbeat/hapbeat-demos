package com.hapbeat.demoremote

import android.content.Intent
import android.hardware.usb.UsbManager
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.HubSettingsScreen
import com.hapbeat.demoremote.ui.LicenseScreen
import com.hapbeat.demoremote.ui.PresetEditScreen
import com.hapbeat.demoremote.ui.PresetImportDialog
import com.hapbeat.demoremote.ui.RemoteScreen
import com.hapbeat.demoremote.ui.SettingsScreen
import com.hapbeat.demoremote.ui.UsbAdbDialog

enum class Screen { REMOTE, SETTINGS, LICENSES, PRESET, HUB_SETTINGS }

class MainActivity : ComponentActivity() {
    private val viewModel: RemoteViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // UDP socket, discovery and mirror run only while the app is visible (ON_START..ON_STOP).
        lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) = viewModel.onForeground()
            override fun onStop(owner: LifecycleOwner) = viewModel.onBackground()
        })
        if (savedInstanceState == null) handleIntent(intent)
        setContent {
            AppTheme {
                var screen by rememberSaveable { mutableStateOf(Screen.REMOTE) }
                var editingPreset by rememberSaveable { mutableStateOf<Int?>(null) }
                LaunchedEffect(viewModel.keepScreenOn) {
                    if (viewModel.keepScreenOn) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                }
                BackHandler(enabled = screen != Screen.REMOTE) {
                    screen = if (screen == Screen.LICENSES) Screen.SETTINGS else Screen.REMOTE
                }
                // targetSdk 35 draws edge-to-edge: keep content clear of the status / navigation bars.
                Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    Box(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
                        when (screen) {
                            Screen.REMOTE -> RemoteScreen(
                                viewModel,
                                onOpenSettings = { screen = Screen.SETTINGS },
                                onEditPreset = { editingPreset = it; screen = Screen.PRESET },
                                onOpenHubSettings = { screen = Screen.HUB_SETTINGS },
                            )
                            Screen.PRESET -> PresetEditScreen(viewModel, editingPreset ?: 1, onDone = { screen = Screen.REMOTE })
                            Screen.HUB_SETTINGS -> HubSettingsScreen(viewModel, onDone = { screen = Screen.REMOTE })
                            Screen.SETTINGS -> SettingsScreen(
                                viewModel,
                                onBack = { screen = Screen.REMOTE },
                                onOpenLicenses = { screen = Screen.LICENSES },
                            )
                            Screen.LICENSES -> LicenseScreen(onBack = { screen = Screen.SETTINGS })
                        }
                        if (viewModel.usbDialogOpen) UsbAdbDialog(viewModel)
                        PresetImportDialog(viewModel)
                    }
                }
            }
        }
    }

    /** singleTop: a Quest plugged in or a preset link opened while the app is open arrives here instead of a second activity. */
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    private fun handleIntent(intent: Intent?) {
        when (intent?.action) {
            UsbManager.ACTION_USB_DEVICE_ATTACHED -> viewModel.onUsbDeviceAttached()
            // hapbeat-remote://preset?d=<token> from the showcase page; only shows the confirmation dialog.
            Intent.ACTION_VIEW -> intent.dataString?.let(viewModel::importFromLink)
        }
    }
}
