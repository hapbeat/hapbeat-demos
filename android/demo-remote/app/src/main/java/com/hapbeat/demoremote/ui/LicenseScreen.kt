package com.hapbeat.demoremote.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun LicenseScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val text = remember {
        listOf("licenses/NOTICES.txt", "licenses/APACHE-2.0.txt").joinToString("\n\n") { path ->
            context.assets.open(path).bufferedReader(Charsets.UTF_8).use { it.readText() }
        }
    }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
            TextButton(onClick = onBack) { Text("← 戻る") }
            Text("ライセンス", style = MaterialTheme.typography.titleLarge)
            Text(text, fontFamily = FontFamily.Monospace, fontSize = 11.sp)
        }
    }
}
