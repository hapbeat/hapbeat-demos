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
import com.hapbeat.demoremote.adb.UsbLinkJudge

/** Restores the Quest's Wi-Fi adb with this phone as the USB host (instead of a PC). Starts by itself on plug-in. */
@Composable
fun UsbAdbDialog(vm: RemoteViewModel) {
    val (status, error) = vm.usbAdbStatus
    AlertDialog(
        onDismissRequest = vm::closeUsbDialog,
        title = { Text("Quest と USB でつなぐ") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    "Quest を再起動すると、スマホからの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                listOf(
                    "1. スマホと Quest を USB-C ケーブル（データ対応）でつなぐ",
                    "2. 自動で始まります。初めてのスマホでは HMD を被り、「USB デバッグを許可」で「常に許可」にチェックして許可する",
                    "3. 「完了」と出たらケーブルを外す",
                ).forEach { Text(it, style = MaterialTheme.typography.bodyMedium) }
                // Fixed one-line USB status: what the phone currently sees on the cable.
                Text(
                    "USB の状態: ${UsbLinkJudge.statusText(vm.usbLink)}",
                    Modifier.fillMaxWidth(),
                    style = MaterialTheme.typography.bodySmall,
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
            TextButton(onClick = vm::enableWifiAdbOverUsb, enabled = !vm.usbAdbRunning, modifier = Modifier.width(120.dp)) {
                Text(if (vm.usbAttempted) "もう一度試す" else "始める", maxLines = 1)
            }
        },
        dismissButton = {
            TextButton(onClick = vm::closeUsbDialog) { Text("閉じる") }
        },
    )
}
