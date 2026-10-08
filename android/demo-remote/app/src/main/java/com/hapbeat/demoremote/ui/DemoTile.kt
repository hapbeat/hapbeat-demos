package com.hapbeat.demoremote.ui

import android.content.res.AssetManager
import android.graphics.BitmapFactory
import android.os.SystemClock
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.PointerInputScope
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.DemoApp

/** Decoded demo thumbnails (`assets/thumbs/`), decoded once per path. */
object DemoThumbnails {
    private val cache = HashMap<String, ImageBitmap>()

    fun get(assets: AssetManager, path: String): ImageBitmap = synchronized(cache) {
        cache.getOrPut(path) { assets.open(path).use { BitmapFactory.decodeStream(it) }.asImageBitmap() }
    }
}

/** Fixed height of a demo tile, with or without a thumbnail, so grids line up. */
private val TileHeight = 92.dp

/** Disabled-content alpha of Material 3, applied to the thumbnail too. */
private const val DISABLED_ALPHA = 0.38f

/** How long a tile with a long-press action must be held. */
private const val LONG_PRESS_MS = 1000

/**
 * Demo button: thumbnail (16:9) above the name; name only when the demo has no thumbnail. With [onLongPress], holding
 * the tile for [LONG_PRESS_MS] calls it instead of [onClick]; a bar along the bottom fills while it is held and goes
 * away when the finger lifts or moves.
 */
@Composable
fun DemoTile(
    app: DemoApp, enabled: Boolean, modifier: Modifier = Modifier, outlined: Boolean = false, onLongPress: (() -> Unit)? = null,
    onClick: () -> Unit,
) {
    val padding = PaddingValues(4.dp)
    val content: @Composable ColumnScope.() -> Unit = { DemoTileContent(app, enabled) }
    val progress = remember { Animatable(0f) }
    var holding by remember { mutableStateOf(false) }
    val longPress by rememberUpdatedState(onLongPress)
    LaunchedEffect(holding) {
        if (holding) progress.animateTo(1f, tween(LONG_PRESS_MS, easing = LinearEasing)) else progress.snapTo(0f)
    }
    val hold = if (onLongPress != null && enabled) {
        Modifier.pointerInput(Unit) { detectHold(LONG_PRESS_MS.toLong(), onHold = { holding = it }) { longPress?.invoke() } }
    } else Modifier
    Box(modifier.height(TileHeight).then(hold)) {
        val m = Modifier.fillMaxSize()
        if (outlined) {
            OutlinedButton(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape, contentPadding = padding) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp), content = content)
            }
        } else {
            Button(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape, contentPadding = padding) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp), content = content)
            }
        }
        if (progress.value > 0f) {
            Box(
                Modifier.align(Alignment.BottomStart).padding(horizontal = 8.dp, vertical = 3.dp)
                    .fillMaxWidth(progress.value).height(4.dp)
                    .background(MaterialTheme.colorScheme.tertiary, RoundedCornerShape(2.dp)),
            )
        }
    }
}

/**
 * Calls [onHold] true when a finger goes down and false when the hold ends, and [onLongPress] once the finger has stayed
 * down within the touch slop for [holdMs]. Watched on the Initial pass without consuming, so the button underneath still
 * gets its tap (and a scroll still scrolls); after a long press the rest of the gesture is consumed so the release does
 * not also click the button.
 */
private suspend fun PointerInputScope.detectHold(holdMs: Long, onHold: (Boolean) -> Unit, onLongPress: () -> Unit) {
    awaitEachGesture {
        val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
        val deadline = SystemClock.uptimeMillis() + holdMs
        var fired = false
        onHold(true)
        try {
            while (true) {
                val left = deadline - SystemClock.uptimeMillis()
                val event = if (left > 0) withTimeoutOrNull(left) { awaitPointerEvent(PointerEventPass.Initial) } else null
                if (event == null) {
                    fired = true
                    break
                }
                val change = event.changes.firstOrNull { it.id == down.id } ?: break
                // Lifted (a tap), or moved past the slop (a scroll / slide off): no long press.
                if (!change.pressed || (change.position - down.position).getDistance() > viewConfiguration.touchSlop) break
            }
        } finally {
            onHold(false)
        }
        if (fired) {
            onLongPress()
            do {
                val event = awaitPointerEvent(PointerEventPass.Initial)
                event.changes.forEach { it.consume() }
            } while (event.changes.any { it.pressed })
        }
    }
}

@Composable
private fun ColumnScope.DemoTileContent(app: DemoApp, enabled: Boolean) {
    val path = app.thumbnail
    if (path != null) {
        val assets = LocalContext.current.assets
        val bitmap = remember(path) { DemoThumbnails.get(assets, path) }
        Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
            Image(
                bitmap = bitmap,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .aspectRatio(16f / 9f, matchHeightConstraintsFirst = true)
                    .clip(RoundedCornerShape(4.dp))
                    .alpha(if (enabled) 1f else DISABLED_ALPHA),
            )
        }
    }
    Text(app.label, textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.labelMedium)
}
