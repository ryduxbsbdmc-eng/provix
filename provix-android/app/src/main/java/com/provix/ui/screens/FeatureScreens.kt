package com.provix.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.provix.core.localization.LocalizationManager
import com.provix.core.ui.theme.LocalProvixPalette
import com.provix.feature.archive.ArchiveRepository
import kotlinx.coroutines.launch

@Composable
fun ArchiveScreen(loc: LocalizationManager) {
    val p = LocalProvixPalette.current
    val archive = remember { ArchiveRepository() }
    var path by remember { mutableStateOf("") }
    var entries by remember { mutableStateOf(listOf<ArchiveRow>()) }
    var loading by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(p.background)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(RoundedCornerShape(11.dp))
                    .background(p.accentSoft),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Default.Archive, contentDescription = null, tint = p.accent, modifier = Modifier.size(22.dp))
            }
            Text(
                text = loc["UI_ExtractArchive"],
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
                color = p.textPrimary,
            )
        }

        OutlinedTextField(
            value = path,
            onValueChange = { path = it },
            modifier = Modifier.fillMaxWidth(),
            label = { Text(loc["UI_ArchivePasswordHint"]) },
            singleLine = true,
            shape = RoundedCornerShape(12.dp),
            colors = OutlinedTextFieldDefaults.colors(
                focusedBorderColor = p.accent,
                unfocusedBorderColor = p.border,
                focusedLabelColor = p.accent,
                cursorColor = p.accent,
                focusedTextColor = p.textPrimary,
                unfocusedTextColor = p.textPrimary,
            ),
        )

        Button(
            onClick = {
                scope.launch {
                    loading = true
                    archive.listEntries(path).onSuccess { list ->
                        entries = list.map { ArchiveRow(it.name, it.size) }
                    }
                    loading = false
                }
            },
            shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(containerColor = p.accent, contentColor = Color.White),
        ) {
            Text(loc["UI_ArchivePasswordUnlock"], fontWeight = FontWeight.SemiBold)
        }

        if (entries.isEmpty() && !loading) {
            Column(
                modifier = Modifier.fillMaxWidth().padding(top = 40.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Box(
                    modifier = Modifier
                        .size(64.dp)
                        .clip(RoundedCornerShape(18.dp))
                        .background(p.iconSurface),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Default.Inventory2, contentDescription = null, tint = p.textSecondary, modifier = Modifier.size(30.dp))
                }
                Spacer(Modifier.height(12.dp))
                Text(loc["UI_ArchivePasswordTitle"], color = p.textSecondary, style = MaterialTheme.typography.bodyMedium)
            }
        } else {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                items(entries) { row ->
                    Surface(shape = RoundedCornerShape(10.dp), color = p.paneSurface, modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Text(
                                text = row.name,
                                modifier = Modifier.weight(1f),
                                color = p.textPrimary,
                                style = MaterialTheme.typography.bodyMedium,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                            Text(
                                text = "${row.size} B",
                                color = p.textSecondary,
                                style = MaterialTheme.typography.labelMedium,
                            )
                        }
                    }
                }
            }
        }
    }
}

private data class ArchiveRow(val name: String, val size: Long)
