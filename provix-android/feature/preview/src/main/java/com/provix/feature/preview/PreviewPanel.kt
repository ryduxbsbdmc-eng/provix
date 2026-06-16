package com.provix.feature.preview

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.MediaItem
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.ui.PlayerView
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import java.io.File

private val PreviewBg = Color(0xFF1A1A1A)
private val PreviewText = Color(0xFFF0F0F0)
private val PreviewSecondary = Color(0xFFB0B0B0)
private val DividerColor = Color(0x18FFFFFF)
private val imageExtensions = setOf("jpg", "jpeg", "png", "gif", "webp", "bmp")
private val videoExtensions = setOf("mp4", "webm", "mkv", "avi", "mov")

@Composable
fun PreviewPanel(
    state: PreviewState,
    onClose: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(PreviewBg)
            .padding(8.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = androidx.compose.foundation.layout.Arrangement.SpaceBetween,
        ) {
            Text(state.name, style = MaterialTheme.typography.titleMedium, color = PreviewText)
            IconButton(onClick = onClose) {
                Icon(Icons.Default.Close, contentDescription = "Close", tint = PreviewSecondary)
            }
        }
        HorizontalDivider(color = DividerColor, modifier = Modifier.padding(bottom = 8.dp))
        when {
            state.extension.lowercase() in imageExtensions -> {
                AsyncImage(
                    model = File(state.path),
                    contentDescription = state.name,
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 240.dp),
                    contentScale = ContentScale.Fit,
                )
            }
            state.extension.lowercase() in videoExtensions -> {
                VideoPreview(path = state.path)
            }
            state.textContent != null -> {
                Text(
                    text = state.textContent,
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 240.dp)
                        .verticalScroll(rememberScrollState()),
                    style = MaterialTheme.typography.bodySmall,
                    color = PreviewText,
                )
            }
            else -> {
                Text(
                    "Preview not available for .${state.extension}",
                    color = PreviewSecondary,
                )
            }
        }
    }
}

@Composable
private fun VideoPreview(path: String) {
    val context = LocalContext.current
    val player = remember(path) {
        ExoPlayer.Builder(context).build().apply {
            setMediaItem(MediaItem.fromUri(android.net.Uri.fromFile(File(path))))
            prepare()
        }
    }
    AndroidView(
        factory = { ctx ->
            PlayerView(ctx).apply {
                this.player = player
            }
        },
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(max = 240.dp),
    )
}
