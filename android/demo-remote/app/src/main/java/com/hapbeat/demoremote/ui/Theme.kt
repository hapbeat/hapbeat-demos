package com.hapbeat.demoremote.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

val StatusYellow = Color(0xFFC79A00)
val StatusGreen = Color(0xFF2E7D32)
val StatusRed = Color(0xFFC62828)
val StatusGray = Color(0xFF757575)

@Composable
fun AppTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme(), content = content)
}

/** Minimum touch height for every action button (56dp). */
val ButtonMinHeight = 56.dp

/** Grids of buttons read better as tiles than as rows of capsules. */
private val ButtonShape = RoundedCornerShape(6.dp)

@Composable
fun ActionButton(text: String, enabled: Boolean, modifier: Modifier = Modifier, outlined: Boolean = false, onClick: () -> Unit) {
    val m = modifier.heightIn(min = ButtonMinHeight)
    if (outlined) {
        OutlinedButton(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape) { Text(text, textAlign = TextAlign.Center) }
    } else {
        Button(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape) { Text(text, textAlign = TextAlign.Center) }
    }
}

/** Lays out [items] in rows of [columns] equally wide cells. */
@Composable
fun <T> ButtonGrid(items: List<T>, columns: Int, cell: @Composable (T, Modifier) -> Unit) {
    items.chunked(columns).forEach { row ->
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            row.forEach { cell(it, Modifier.weight(1f)) }
            repeat(columns - row.size) { androidx.compose.foundation.layout.Spacer(Modifier.weight(1f)) }
        }
    }
}
