package com.hapbeat.demoremote.ui

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.view.SurfaceHolder
import android.view.SurfaceView
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Spacer
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.mirror.MediaCodecVideoSink
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import java.io.InputStream

// The Android side of the functions the shared screens call; the desktop build defines the same names and signatures.

@Composable
fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit) = BackHandler(enabled = enabled, onBack = onBack)

fun decodeImageBitmap(input: InputStream): ImageBitmap = BitmapFactory.decodeStream(input).asImageBitmap()

@Composable
fun PlatformMirrorVideo(vm: RemoteViewModel, modifier: Modifier) {
    AndroidView(
        factory = { context ->
            SurfaceView(context).apply {
                holder.addCallback(object : SurfaceHolder.Callback {
                    override fun surfaceCreated(holder: SurfaceHolder) = vm.onMirrorSinkAvailable { MediaCodecVideoSink(holder.surface) }
                    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) = Unit
                    override fun surfaceDestroyed(holder: SurfaceHolder) = vm.onMirrorSinkDestroyed()
                })
            }
        },
        modifier = modifier,
    )
}

@Composable
fun PlatformMirrorHeaderExtras(vm: RemoteViewModel) = Unit

/** The free half of the row under 「Hub の設定」. */
@Composable
fun PlatformMainActionSlot(vm: RemoteViewModel, onOpen: () -> Unit, modifier: Modifier) = Spacer(modifier)

@Composable
fun PlatformExtraScreen(vm: RemoteViewModel, onBack: () -> Unit) = Unit

/**
 * Reads a showcase QR with the in-app scanner (zxing-android-embedded). The camera permission is asked
 * right before the first scan; the result goes to the import confirmation dialog.
 */
@Composable
fun PlatformQrImportButton(vm: RemoteViewModel, modifier: Modifier) {
    val context = LocalContext.current
    val scan = rememberLauncherForActivityResult(ScanContract()) { result ->
        result.contents?.let(vm::importFromQr)
    }
    val scanOptions = {
        ScanOptions()
            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            .setPrompt("ショーケースのプリセットの QR を枠に入れてください")
            .setBeepEnabled(false)
            .setOrientationLocked(false)
    }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) scan.launch(scanOptions()) else vm.onQrCameraDenied()
    }
    TextButton(
        onClick = {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                scan.launch(scanOptions())
            } else {
                permission.launch(Manifest.permission.CAMERA)
            }
        },
        modifier = modifier,
    ) { Text("QR から Hub に取り込む") }
}
