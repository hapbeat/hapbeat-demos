package com.hapbeat.demoremote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.toComposeImageBitmap
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.RemoteViewModel
import java.io.InputStream

// The desktop side of the functions the shared screens call; the Android build defines the same names and signatures.

/** Every screen has its own 戻る button; a PC has no system back. */
@Composable
fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit) = Unit

fun decodeImageBitmap(input: InputStream): ImageBitmap = org.jetbrains.skia.Image.makeFromEncoded(input.readBytes()).toComposeImageBitmap()

/** The preview: the same frames as the full-screen output, never a second stream. */
@Composable
fun PlatformMirrorVideo(vm: RemoteViewModel, modifier: Modifier) {
    if (vm.mirrorVideoSize != null) MirrorVideo(LocalDesktopState.current.frames, fill = false, modifier) else Box(modifier)
}

@Composable
fun PlatformMirrorHeaderExtras(vm: RemoteViewModel) = MirrorFullScreenControls(vm)

/** The free half of the row under 「Hub の設定」. */
@Composable
fun PlatformMainActionSlot(vm: RemoteViewModel, onOpen: () -> Unit, modifier: Modifier) = Spacer(modifier)

@Composable
fun PlatformExtraScreen(vm: RemoteViewModel, onBack: () -> Unit) = Unit

/** No camera on a PC: the showcase's preset link (or the QR's text) is pasted instead; it goes to the same confirmation dialog. */
@Composable
fun PlatformQrImportButton(vm: RemoteViewModel, modifier: Modifier) {
    var open by remember { mutableStateOf(false) }
    TextButton(onClick = { open = true }, modifier = modifier) { Text("リンクから Hub に取り込み") }
    if (open) {
        var text by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { open = false },
            title = { Text("プリセットのリンク") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("ショーケースのプリセットのリンクを貼り付けてください", style = MaterialTheme.typography.bodySmall)
                    OutlinedTextField(value = text, onValueChange = { text = it }, label = { Text("リンク") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                }
            },
            confirmButton = {
                TextButton(onClick = { open = false; vm.importFromQr(text.trim()) }, enabled = text.isNotBlank()) { Text("取り込み") }
            },
            dismissButton = { TextButton(onClick = { open = false }) { Text("キャンセル") } },
        )
    }
}
