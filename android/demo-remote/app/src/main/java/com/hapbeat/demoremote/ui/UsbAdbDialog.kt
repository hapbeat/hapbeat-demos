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

/** Re-enables the Quest's Wi-Fi adb with this phone as the USB host (instead of a PC). */
@Composable
fun UsbAdbDialog(vm: RemoteViewModel) {
    val (status, error) = vm.usbAdbStatus
    AlertDialog(
        onDismissRequest = vm::closeUsbDialog,
        title = { Text("USB で Wi-Fi adb を有効化") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(
                    "1. スマホと Quest を USB-C ケーブルで直結",
                    "2. スマホ側の通知で「USB の制御: このデバイス」を選ぶ（Quest 側がホストだと見えません）",
                    "3. 「有効化」を押す",
                    "4. 初回はヘッドセット内で「常に許可」にチェックして許可",
                ).forEach { Text(it, style = MaterialTheme.typography.bodyMedium) }
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
            // Fixed width so the label swap does not move the buttons.
            TextButton(onClick = vm::enableWifiAdbOverUsb, enabled = !vm.usbAdbRunning, modifier = Modifier.width(96.dp)) {
                Text(if (vm.usbAdbRunning) "実行中" else "有効化")
            }
        },
        dismissButton = {
            TextButton(onClick = vm::closeUsbDialog) { Text("閉じる") }
        },
    )
}
