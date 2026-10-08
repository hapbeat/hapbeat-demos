package com.hapbeat.demoremote.ui

import android.content.res.AssetManager
import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
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

/** Demo button: thumbnail (16:9) above the name; name only when the demo has no thumbnail. */
@Composable
fun DemoTile(app: DemoApp, enabled: Boolean, modifier: Modifier = Modifier, outlined: Boolean = false, onClick: () -> Unit) {
    val m = modifier.height(TileHeight)
    val padding = PaddingValues(4.dp)
    val content: @Composable ColumnScope.() -> Unit = { DemoTileContent(app, enabled) }
    if (outlined) {
        OutlinedButton(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape, contentPadding = padding) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp), content = content)
        }
    } else {
        Button(onClick = onClick, enabled = enabled, modifier = m, shape = ButtonShape, contentPadding = padding) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp), content = content)
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
