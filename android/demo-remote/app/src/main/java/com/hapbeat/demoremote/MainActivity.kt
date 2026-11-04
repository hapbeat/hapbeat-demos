package com.hapbeat.demoremote

import android.app.Application
import android.content.Intent
import android.hardware.usb.UsbManager
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
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
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.viewModelScope
import com.hapbeat.demoremote.ui.AppTheme
import com.hapbeat.demoremote.ui.RemoteApp

/** Keeps the shared [RemoteViewModel] across configuration changes. */
class RemoteViewModelHolder(app: Application) : AndroidViewModel(app) {
    val remote = RemoteViewModel(AndroidPlatform(app), viewModelScope)

    override fun onCleared() = remote.close()
}

class MainActivity : ComponentActivity() {
    private val holder: RemoteViewModelHolder by viewModels()
    private val viewModel: RemoteViewModel get() = holder.remote

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
                LaunchedEffect(viewModel.keepScreenOn) {
                    if (viewModel.keepScreenOn) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                }
                // targetSdk 35 draws edge-to-edge: keep content clear of the status / navigation bars.
                Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    Box(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
                        RemoteApp(viewModel)
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
