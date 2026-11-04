package com.hapbeat.demoremote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.RemoteViewModel

/** Restores the Quest's Wi-Fi adb over a USB cable, in the way of this platform ([RemoteViewModel.usbTexts]). */
@Composable
fun UsbAdbDialog(vm: RemoteViewModel) {
    val (status, error) = vm.usbAdbStatus
    val texts = vm.usbTexts
    AlertDialog(
        onDismissRequest = vm::closeUsbDialog,
        title = { Text("Quest と USB でつなぐ") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    texts.dialogIntro,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                texts.dialogSteps.forEach { Text(it, style = MaterialTheme.typography.bodyMedium) }
                // Fixed one-line USB status: what the phone currently sees on the cable.
                Text(
                    "USB の状態: ${vm.usbStatusText}",
                    Modifier.fillMaxWidth(),
                    style = MaterialTheme.typography.bodySmall,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                // Fixed small line: the USB devices of the last run (VID:PID / interfaces); the log has the full text.
                Text(
                    vm.usbDeviceDetail.ifEmpty { " " },
                    Modifier.fillMaxWidth(),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                // Fixed three-line area: progress and errors change the text only.
                Text(
                    status,
                    Modifier.fillMaxWidth().height(60.dp),
                    style = MaterialTheme.typography.bodySmall,
                    color = if (error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        },
        confirmButton = {
            // Fixed width (fits the longer label); disabled while a run is going.
            TextButton(onClick = vm::enableWifiAdbOverUsb, enabled = !vm.usbAdbRunning, modifier = Modifier.width(texts.startButtonWidthDp.dp)) {
                Text(if (vm.usbAttempted) texts.retryLabel else texts.startLabel, maxLines = 1)
            }
        },
        dismissButton = {
            TextButton(onClick = vm::closeUsbDialog) { Text("閉じる") }
        },
    )
}
