package com.provix.feature.browser

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.DriveFileMove
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.DriveFileRenameOutline
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.OpenInFull
import androidx.compose.material.icons.filled.OpenInNew
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.StarBorder
import androidx.compose.material.icons.filled.Unarchive
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import com.provix.core.localization.LocalizationManager
import com.provix.core.model.FileEntry
import com.provix.core.ui.theme.LocalProvixPalette
import com.provix.core.ui.theme.ProvixPalette
import java.text.DateFormat
import java.util.Date

data class FileContextTarget(
    val paneId: Int,
    val entry: FileEntry,
)

private val ARCHIVE_EXTENSIONS = setOf("zip", "rar", "7z", "tar", "gz", "bz2", "xz")

fun FileEntry.isArchiveFile(): Boolean {
    if (isDirectory) return false
    val lower = name.lowercase()
    return lower.endsWith(".tar.gz") || lower.endsWith(".tar.bz2") ||
        extension.lowercase() in ARCHIVE_EXTENSIONS
}

@Composable
fun FileContextMenuDialog(
    target: FileContextTarget,
    loc: LocalizationManager,
    dualPane: Boolean,
    isBookmarked: Boolean,
    onDismiss: () -> Unit,
    onOpen: () -> Unit,
    onPreview: () -> Unit,
    onOpenInOtherPane: () -> Unit,
    onCopyToOtherPane: () -> Unit,
    onMoveToOtherPane: () -> Unit,
    onRename: () -> Unit,
    onDelete: () -> Unit,
    onToggleBookmark: () -> Unit,
    onExtract: () -> Unit,
    onShowProperties: () -> Unit,
) {
    val p = LocalProvixPalette.current
    val haptic = LocalHapticFeedback.current
    val entry = target.entry
    val visual = remember(entry.path, entry.isDirectory) { entry.visual(p.folderTint, p.accent) }

    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(18.dp),
            color = p.surfaceElevated,
            tonalElevation = 6.dp,
        ) {
            Column(modifier = Modifier.padding(vertical = 8.dp)) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .clip(RoundedCornerShape(11.dp))
                            .background(visual.tint.copy(alpha = 0.16f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(visual.icon, contentDescription = null, tint = visual.tint, modifier = Modifier.size(22.dp))
                    }
                    Text(
                        text = entry.name,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                        color = p.textPrimary,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                HorizontalDivider(color = p.splitter, modifier = Modifier.padding(vertical = 4.dp))

                ContextMenuItem(loc["UI_TreeOpen"], if (entry.isDirectory) Icons.Default.FolderOpen else Icons.Default.OpenInFull, p) {
                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                    onOpen(); onDismiss()
                }

                if (!entry.isDirectory) {
                    ContextMenuItem(loc["UI_Preview"], Icons.Default.Visibility, p) { onPreview(); onDismiss() }
                }

                if (dualPane) {
                    ContextMenuItem(loc["UI_OpenInOtherPane"], Icons.Default.OpenInNew, p) { onOpenInOtherPane(); onDismiss() }
                    ContextMenuItem(loc["UI_CopyToOtherPane"], Icons.Default.ContentCopy, p) { onCopyToOtherPane(); onDismiss() }
                    ContextMenuItem(loc["UI_MoveToOtherPane"], Icons.AutoMirrored.Filled.DriveFileMove, p) { onMoveToOtherPane(); onDismiss() }
                }

                if (entry.isDirectory) {
                    val bookmarkLabel = if (isBookmarked) loc["UI_RemoveBookmark"] else loc["UI_BookmarkAddCurrent"]
                    val bookmarkIcon = if (isBookmarked) Icons.Default.Star else Icons.Default.StarBorder
                    ContextMenuItem(bookmarkLabel, bookmarkIcon, p) { onToggleBookmark(); onDismiss() }
                }

                if (entry.isArchiveFile()) {
                    ContextMenuItem(loc["UI_ExtractHere"], Icons.Default.Unarchive, p) { onExtract(); onDismiss() }
                }

                ContextMenuItem(loc["UI_Rename"], Icons.Default.DriveFileRenameOutline, p) { onRename(); onDismiss() }
                ContextMenuItem(loc["UI_Properties"], Icons.Default.Info, p) { onShowProperties(); onDismiss() }

                HorizontalDivider(color = p.splitter, modifier = Modifier.padding(vertical = 4.dp))

                ContextMenuItem(loc["UI_Delete"], Icons.Default.Delete, p, destructive = true) { onDelete(); onDismiss() }
            }
        }
    }
}

@Composable
private fun ContextMenuItem(
    label: String,
    icon: ImageVector,
    palette: ProvixPalette,
    destructive: Boolean = false,
    onClick: () -> Unit,
) {
    val color = if (destructive) palette.error else palette.textPrimary
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(20.dp))
        Text(label, color = color, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
fun RenameFileDialog(
    entry: FileEntry,
    loc: LocalizationManager,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var name by remember(entry.path) { mutableStateOf(entry.name) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(loc["UI_Rename"]) },
        text = {
            OutlinedTextField(
                value = name,
                onValueChange = { name = it },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val trimmed = name.trim()
                    if (trimmed.isNotEmpty() && trimmed != entry.name) {
                        onConfirm(trimmed)
                    }
                    onDismiss()
                },
            ) { Text(loc["UI_Save"]) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(loc["UI_Cancel"]) }
        },
    )
}

@Composable
fun FilePropertiesDialog(
    entry: FileEntry,
    loc: LocalizationManager,
    onDismiss: () -> Unit,
) {
    val p = LocalProvixPalette.current
    val dateText = remember(entry.lastModified) {
        DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT)
            .format(Date(entry.lastModified))
    }
    val sizeText = remember(entry.size) {
        when {
            entry.isDirectory -> "—"
            entry.size < 1024 -> "${entry.size} B"
            entry.size < 1024 * 1024 -> "${entry.size / 1024} KB"
            else -> String.format("%.1f MB", entry.size / (1024.0 * 1024.0))
        }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(loc["UI_Properties"]) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                PropertyRow(loc["UI_NewItem"], entry.name, p)
                PropertyRow(loc["UI_PropertiesPath"], entry.path, p)
                PropertyRow(loc["UI_PropertiesSize"], sizeText, p)
                PropertyRow(loc["UI_PropertiesModified"], dateText, p)
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(loc["UI_Close"]) }
        },
    )
}

@Composable
private fun PropertyRow(label: String, value: String, palette: ProvixPalette) {
    Column {
        Text(
            label.uppercase(),
            style = MaterialTheme.typography.labelSmall,
            color = palette.textSecondary,
            fontWeight = FontWeight.SemiBold,
        )
        Text(value, style = MaterialTheme.typography.bodyMedium, color = palette.textPrimary)
    }
}
