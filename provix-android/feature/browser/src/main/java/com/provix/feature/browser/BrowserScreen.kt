package com.provix.feature.browser

import android.content.res.Configuration
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.FolderOff
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.VerticalDivider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.provix.core.localization.LocalizationManager
import com.provix.core.model.FileEntry
import com.provix.core.model.PaneTab
import com.provix.core.ui.theme.LocalProvixPalette
import com.provix.feature.preview.PreviewPanel
import kotlinx.coroutines.withTimeoutOrNull
import java.text.DateFormat
import java.util.Date

@Composable
fun BrowserScreen(
    viewModel: BrowserViewModel,
    loc: LocalizationManager,
    onOpenSettings: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val p = LocalProvixPalette.current
    val uiState by viewModel.uiState.collectAsState()
    val leftPane by viewModel.leftPane.collectAsState()
    val rightPane by viewModel.rightPane.collectAsState()
    val activePaneId by viewModel.activePaneId.collectAsState()
    val bookmarks by viewModel.bookmarks.collectAsState()
    var searchQuery by remember { mutableStateOf("") }
    var contextTarget by remember { mutableStateOf<FileContextTarget?>(null) }
    var renameTarget by remember { mutableStateOf<FileContextTarget?>(null) }
    var propertiesTarget by remember { mutableStateOf<FileEntry?>(null) }

    val configuration = LocalConfiguration.current
    val isLandscape = configuration.orientation == Configuration.ORIENTATION_LANDSCAPE
    val isWide = configuration.screenWidthDp >= 600
    val showDualSideBySide = uiState.dualPane && (isLandscape || isWide)
    val showPreviewBeside = isLandscape && uiState.preview != null

    fun openFileContextMenu(paneId: Int, entry: FileEntry) {
        viewModel.setActivePane(paneId)
        viewModel.selectItem(paneId, entry.path)
        contextTarget = FileContextTarget(paneId, entry)
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(p.background),
    ) {
        ProvixTitleBar(
            loc = loc,
            onOpenSettings = onOpenSettings,
            onSwapPanes = { viewModel.openInOtherPane(activePaneId) },
            dualPane = uiState.dualPane,
        )

        SearchBar(
            query = searchQuery,
            placeholder = loc["UI_SearchFiles"],
            grepLabel = loc["UI_SearchContent"],
            onQueryChange = {
                searchQuery = it
                viewModel.searchByName(it)
            },
            onClear = {
                searchQuery = ""
                viewModel.searchByName("")
            },
            onGrep = { viewModel.searchContent(searchQuery) },
        )

        if (bookmarks.isNotEmpty()) {
            BookmarkStrip(
                bookmarks = bookmarks,
                onBookmark = { viewModel.navigate(activePaneId, it) },
            )
        }

        if (!showDualSideBySide && uiState.dualPane) {
            PaneSwitcher(
                activePaneId = activePaneId,
                leftLabel = loc["UI_CompareLeft"],
                rightLabel = loc["UI_CompareRight"],
                onSelect = viewModel::setActivePane,
            )
        }

        Row(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
        ) {
            Row(
                modifier = Modifier
                    .weight(if (showPreviewBeside) 0.62f else 1f)
                    .fillMaxHeight(),
            ) {
                if (showDualSideBySide) {
                    DirectoryPane(
                        paneId = 0,
                        pane = leftPane,
                        entries = uiState.leftEntries,
                        isLoading = uiState.leftLoading,
                        isActive = activePaneId == 0,
                        loc = loc,
                        onActivate = { viewModel.setActivePane(0) },
                        onNavigate = { viewModel.navigate(0, it) },
                        onBack = { viewModel.goBack(0) },
                        onForward = { viewModel.goForward(0) },
                        onRefresh = { viewModel.refresh(0) },
                        onOpen = { viewModel.previewFile(it) },
                        onFileContextMenu = { openFileContextMenu(0, it) },
                        onAddTab = { viewModel.addTab(0) },
                        onCloseTab = { viewModel.closeTab(0, it) },
                        onSelectTab = { viewModel.selectTab(0, it) },
                        modifier = Modifier.weight(1f),
                    )
                    VerticalDivider(
                        modifier = Modifier
                            .fillMaxHeight()
                            .width(1.dp),
                        color = p.splitter,
                    )
                    DirectoryPane(
                        paneId = 1,
                        pane = rightPane,
                        entries = uiState.rightEntries,
                        isLoading = uiState.rightLoading,
                        isActive = activePaneId == 1,
                        loc = loc,
                        onActivate = { viewModel.setActivePane(1) },
                        onNavigate = { viewModel.navigate(1, it) },
                        onBack = { viewModel.goBack(1) },
                        onForward = { viewModel.goForward(1) },
                        onRefresh = { viewModel.refresh(1) },
                        onOpen = { viewModel.previewFile(it) },
                        onFileContextMenu = { openFileContextMenu(1, it) },
                        onAddTab = { viewModel.addTab(1) },
                        onCloseTab = { viewModel.closeTab(1, it) },
                        onSelectTab = { viewModel.selectTab(1, it) },
                        modifier = Modifier.weight(1f),
                    )
                } else {
                    val pane = if (activePaneId == 0) leftPane else rightPane
                    val entries = if (activePaneId == 0) uiState.leftEntries else uiState.rightEntries
                    val loading = if (activePaneId == 0) uiState.leftLoading else uiState.rightLoading
                    val paneId = activePaneId
                    DirectoryPane(
                        paneId = paneId,
                        pane = pane,
                        entries = entries,
                        isLoading = loading,
                        isActive = true,
                        loc = loc,
                        onActivate = { },
                        onNavigate = { viewModel.navigate(paneId, it) },
                        onBack = { viewModel.goBack(paneId) },
                        onForward = { viewModel.goForward(paneId) },
                        onRefresh = { viewModel.refresh(paneId) },
                        onOpen = { viewModel.previewFile(it) },
                        onFileContextMenu = { openFileContextMenu(paneId, it) },
                        onAddTab = { viewModel.addTab(paneId) },
                        onCloseTab = { viewModel.closeTab(paneId, it) },
                        onSelectTab = { viewModel.selectTab(paneId, it) },
                        modifier = Modifier.fillMaxSize(),
                    )
                }
            }

            if (showPreviewBeside) {
                VerticalDivider(
                    modifier = Modifier
                        .fillMaxHeight()
                        .width(1.dp),
                    color = p.splitter,
                )
                PreviewPanel(
                    state = uiState.preview!!,
                    onClose = viewModel::clearPreview,
                    modifier = Modifier
                        .weight(0.38f)
                        .fillMaxHeight()
                        .background(p.paneSurface),
                )
            }
        }

        if (!showPreviewBeside) {
            uiState.preview?.let { preview ->
                HorizontalDivider(color = p.splitter)
                PreviewPanel(
                    state = preview,
                    onClose = viewModel::clearPreview,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(if (isLandscape) 180.dp else 220.dp)
                        .background(p.paneSurface),
                )
            }
        }

        uiState.error?.let { error ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(p.error.copy(alpha = 0.14f))
                    .padding(horizontal = 14.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Box(
                    modifier = Modifier
                        .size(6.dp)
                        .clip(CircleShape)
                        .background(p.error),
                )
                Text(
                    text = error,
                    color = p.error,
                    style = MaterialTheme.typography.bodySmall,
                )
            }
        }

        StatusBar(
            path = if (activePaneId == 0) leftPane.currentPath else rightPane.currentPath,
            itemCount = if (activePaneId == 0) uiState.leftEntries.size else uiState.rightEntries.size,
            itemsLabel = loc["UI_ItemCount"],
        )
    }

    contextTarget?.let { target ->
        val isBookmarked = bookmarks.any { it.path == target.entry.path }
        FileContextMenuDialog(
            target = target,
            loc = loc,
            dualPane = uiState.dualPane,
            isBookmarked = isBookmarked,
            onDismiss = { contextTarget = null },
            onOpen = {
                if (target.entry.isDirectory) {
                    viewModel.navigate(target.paneId, target.entry.path)
                } else {
                    viewModel.previewFile(target.entry)
                }
            },
            onPreview = { viewModel.previewFile(target.entry) },
            onOpenInOtherPane = { viewModel.openEntryInOtherPane(target.paneId, target.entry) },
            onCopyToOtherPane = { viewModel.copyItem(target.paneId, target.entry.path) },
            onMoveToOtherPane = { viewModel.moveItem(target.paneId, target.entry.path) },
            onRename = { renameTarget = target },
            onDelete = { viewModel.deleteItem(target.paneId, target.entry.path) },
            onToggleBookmark = {
                if (isBookmarked) {
                    viewModel.removeBookmark(target.entry.path)
                } else {
                    viewModel.addBookmark(target.entry.path, target.entry.name)
                }
            },
            onExtract = { viewModel.extractArchive(target.paneId, target.entry.path) },
            onShowProperties = { propertiesTarget = target.entry },
        )
    }

    renameTarget?.let { target ->
        RenameFileDialog(
            entry = target.entry,
            loc = loc,
            onDismiss = { renameTarget = null },
            onConfirm = { newName -> viewModel.rename(target.entry.path, newName, target.paneId) },
        )
    }

    propertiesTarget?.let { entry ->
        FilePropertiesDialog(
            entry = entry,
            loc = loc,
            onDismiss = { propertiesTarget = null },
        )
    }
}

@Composable
private fun ProvixTitleBar(
    loc: LocalizationManager,
    onOpenSettings: () -> Unit,
    onSwapPanes: () -> Unit,
    dualPane: Boolean,
) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(p.titleBar)
            .padding(horizontal = 12.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(30.dp)
                .clip(RoundedCornerShape(9.dp))
                .background(p.accent),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = "P",
                color = Color.White,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
            )
        }
        Text(
            text = loc["UI_AppTitle"],
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
            color = p.textPrimary,
            modifier = Modifier.padding(start = 10.dp),
        )
        Spacer(Modifier.weight(1f))
        if (dualPane) {
            ChromeIconButton(onClick = onSwapPanes, contentDescription = loc["UI_OpenInOtherPane"]) {
                Icon(Icons.Default.SwapHoriz, contentDescription = null, tint = p.textPrimary)
            }
        }
        ChromeIconButton(onClick = onOpenSettings, contentDescription = loc["UI_Settings"]) {
            Icon(Icons.Default.Settings, contentDescription = null, tint = p.textPrimary)
        }
    }
}

@Composable
private fun ChromeIconButton(
    onClick: () -> Unit,
    contentDescription: String? = null,
    content: @Composable () -> Unit,
) {
    val p = LocalProvixPalette.current
    Box(
        modifier = Modifier
            .size(38.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(p.buttonChrome)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        content()
    }
}

@Composable
private fun SearchBar(
    query: String,
    placeholder: String,
    grepLabel: String,
    onQueryChange: (String) -> Unit,
    onClear: () -> Unit,
    onGrep: () -> Unit,
) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        TextField(
            value = query,
            onValueChange = onQueryChange,
            modifier = Modifier
                .weight(1f)
                .height(48.dp)
                .clip(RoundedCornerShape(12.dp)),
            placeholder = { Text(placeholder, color = p.textSecondary, style = MaterialTheme.typography.bodyMedium) },
            leadingIcon = {
                Icon(Icons.Default.Search, contentDescription = null, tint = p.textSecondary, modifier = Modifier.size(20.dp))
            },
            trailingIcon = if (query.isNotEmpty()) {
                {
                    Box(
                        modifier = Modifier
                            .size(28.dp)
                            .clip(CircleShape)
                            .clickable(onClick = onClear),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(Icons.Default.Clear, contentDescription = null, tint = p.textSecondary, modifier = Modifier.size(18.dp))
                    }
                }
            } else null,
            singleLine = true,
            textStyle = MaterialTheme.typography.bodyMedium,
            colors = TextFieldDefaults.colors(
                focusedContainerColor = p.addressBar,
                unfocusedContainerColor = p.addressBar,
                focusedIndicatorColor = Color.Transparent,
                unfocusedIndicatorColor = Color.Transparent,
                disabledIndicatorColor = Color.Transparent,
                cursorColor = p.accent,
                focusedTextColor = p.textPrimary,
                unfocusedTextColor = p.textPrimary,
            ),
        )
        Surface(
            onClick = onGrep,
            shape = RoundedCornerShape(12.dp),
            color = p.accentSoft,
            modifier = Modifier.height(48.dp),
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = grepLabel,
                    color = p.accent,
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                )
            }
        }
    }
}

@Composable
private fun BookmarkStrip(
    bookmarks: List<com.provix.core.model.Bookmark>,
    onBookmark: (String) -> Unit,
) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 2.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        bookmarks.take(8).forEach { bookmark ->
            Surface(
                onClick = { onBookmark(bookmark.path) },
                shape = RoundedCornerShape(20.dp),
                color = p.iconSurface,
            ) {
                Text(
                    text = bookmark.label,
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 7.dp),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.labelMedium,
                    color = p.textSecondary,
                )
            }
        }
    }
}

@Composable
private fun PaneSwitcher(
    activePaneId: Int,
    leftLabel: String,
    rightLabel: String,
    onSelect: (Int) -> Unit,
) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 4.dp)
            .clip(RoundedCornerShape(12.dp))
            .background(p.iconSurface)
            .padding(3.dp),
        horizontalArrangement = Arrangement.spacedBy(3.dp),
    ) {
        listOf(0 to leftLabel, 1 to rightLabel).forEach { (id, label) ->
            val selected = activePaneId == id
            val bg by animateColorAsState(
                if (selected) p.accent else Color.Transparent,
                tween(200),
                label = "paneTab",
            )
            Surface(
                onClick = { onSelect(id) },
                shape = RoundedCornerShape(9.dp),
                color = bg,
                modifier = Modifier.weight(1f),
            ) {
                Text(
                    text = label,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 9.dp),
                    textAlign = TextAlign.Center,
                    color = if (selected) Color.White else p.textSecondary,
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.Medium,
                )
            }
        }
    }
}

@Composable
private fun StatusBar(path: String, itemCount: Int, itemsLabel: String) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(p.statusBar)
            .padding(horizontal = 14.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = path,
            modifier = Modifier.weight(1f),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            style = MaterialTheme.typography.bodySmall,
            color = p.textSecondary,
        )
        Surface(
            shape = RoundedCornerShape(20.dp),
            color = p.iconSurface,
        ) {
            Text(
                text = "$itemCount $itemsLabel",
                modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp),
                style = MaterialTheme.typography.labelMedium,
                color = p.textSecondary,
            )
        }
    }
}

@Composable
private fun DirectoryPane(
    paneId: Int,
    pane: PaneState,
    entries: List<FileEntry>,
    isLoading: Boolean,
    isActive: Boolean,
    loc: LocalizationManager,
    onActivate: () -> Unit,
    onNavigate: (String) -> Unit,
    onBack: () -> Unit,
    onForward: () -> Unit,
    onRefresh: () -> Unit,
    onOpen: (FileEntry) -> Unit,
    onFileContextMenu: (FileEntry) -> Unit,
    onAddTab: () -> Unit,
    onCloseTab: (String) -> Unit,
    onSelectTab: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val p = LocalProvixPalette.current
    val borderColor by animateColorAsState(
        if (isActive) p.paneActiveBorder else p.paneInactiveBorder,
        tween(200),
        label = "paneBorder",
    )

    Column(
        modifier = modifier
            .padding(6.dp)
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, borderColor, RoundedCornerShape(14.dp))
            .background(p.paneSurface)
            .clickable(onClick = onActivate),
    ) {
        PaneTabRow(
            tabs = pane.tabs,
            activeIndex = pane.activeTabIndex,
            onSelectTab = onSelectTab,
            onCloseTab = onCloseTab,
            onAddTab = onAddTab,
        )

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 8.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            NavButton(onClick = onBack, enabled = pane.backStack.isNotEmpty()) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = null, tint = p.textPrimary, modifier = Modifier.size(18.dp))
            }
            NavButton(onClick = onForward, enabled = pane.forwardStack.isNotEmpty()) {
                Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null, tint = p.textPrimary, modifier = Modifier.size(18.dp))
            }
            NavButton(onClick = onRefresh) {
                Icon(Icons.Default.Refresh, contentDescription = null, tint = p.textPrimary, modifier = Modifier.size(18.dp))
            }
            Text(
                text = pane.currentPath,
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(10.dp))
                    .background(p.addressBar)
                    .padding(horizontal = 12.dp, vertical = 9.dp),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                style = MaterialTheme.typography.bodySmall,
                color = p.textPrimary,
            )
        }

        HorizontalDivider(color = p.splitter)

        Box(modifier = Modifier.weight(1f)) {
            Crossfade(targetState = isLoading, animationSpec = tween(220), label = "paneContent") { loading ->
                when {
                    loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(color = p.accent, strokeWidth = 3.dp)
                    }
                    entries.isEmpty() -> EmptyFolder(loc)
                    else -> LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = androidx.compose.foundation.layout.PaddingValues(vertical = 4.dp),
                    ) {
                        items(entries, key = { it.path }) { entry ->
                            FileRow(
                                entry = entry,
                                selected = entry.path in pane.selectedPaths,
                                onClick = {
                                    if (entry.isDirectory) onNavigate(entry.path) else onOpen(entry)
                                },
                                onContextMenu = { onFileContextMenu(entry) },
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun EmptyFolder(loc: LocalizationManager) {
    val p = LocalProvixPalette.current
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Box(
            modifier = Modifier
                .size(64.dp)
                .clip(RoundedCornerShape(18.dp))
                .background(p.iconSurface),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                Icons.Default.FolderOff,
                contentDescription = null,
                tint = p.textSecondary,
                modifier = Modifier.size(30.dp),
            )
        }
        Spacer(Modifier.height(14.dp))
        Text(
            text = loc["UI_FolderEmpty"],
            style = MaterialTheme.typography.titleSmall,
            color = p.textPrimary,
            fontWeight = FontWeight.Medium,
        )
        Spacer(Modifier.height(4.dp))
        Text(
            text = loc["UI_FolderEmptyHint"],
            style = MaterialTheme.typography.bodySmall,
            color = p.textSecondary,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun NavButton(
    onClick: () -> Unit,
    enabled: Boolean = true,
    content: @Composable () -> Unit,
) {
    val p = LocalProvixPalette.current
    Box(
        modifier = Modifier
            .size(34.dp)
            .clip(RoundedCornerShape(9.dp))
            .background(if (enabled) p.buttonChrome else Color.Transparent)
            .clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        content()
    }
}

@Composable
private fun PaneTabRow(
    tabs: List<PaneTab>,
    activeIndex: Int,
    onSelectTab: (Int) -> Unit,
    onCloseTab: (String) -> Unit,
    onAddTab: () -> Unit,
) {
    val p = LocalProvixPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(p.statusBar)
            .padding(horizontal = 6.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.spacedBy(5.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        tabs.forEachIndexed { index, tab ->
            val selected = index == activeIndex
            Surface(
                onClick = { onSelectTab(index) },
                shape = RoundedCornerShape(9.dp),
                color = if (selected) p.accentSoft else Color.Transparent,
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.padding(
                        start = 12.dp,
                        end = if (tabs.size > 1) 4.dp else 12.dp,
                        top = 6.dp,
                        bottom = 6.dp,
                    ),
                ) {
                    Text(
                        text = tab.title,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        color = if (selected) p.accent else p.textSecondary,
                        style = MaterialTheme.typography.labelMedium,
                        fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                    )
                    if (tabs.size > 1) {
                        Box(
                            modifier = Modifier
                                .padding(start = 4.dp)
                                .size(22.dp)
                                .clip(CircleShape)
                                .clickable { onCloseTab(tab.id) },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                Icons.Default.Close,
                                contentDescription = null,
                                tint = if (selected) p.accent else p.textSecondary,
                                modifier = Modifier.size(13.dp),
                            )
                        }
                    }
                }
            }
        }
        Box(
            modifier = Modifier
                .size(30.dp)
                .clip(RoundedCornerShape(9.dp))
                .background(p.buttonChrome)
                .clickable(onClick = onAddTab),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Default.Add, contentDescription = null, tint = p.textSecondary, modifier = Modifier.size(18.dp))
        }
    }
}

@Composable
private fun FileRow(
    entry: FileEntry,
    selected: Boolean,
    onClick: () -> Unit,
    onContextMenu: () -> Unit,
) {
    val p = LocalProvixPalette.current
    val visual = remember(entry.path, entry.isDirectory) { entry.visual(p.folderTint, p.accent) }
    val bg by animateColorAsState(
        if (selected) p.selectionFill else Color.Transparent,
        tween(180),
        label = "rowBg",
    )
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 8.dp, vertical = 2.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(bg)
            .pointerInput(entry.path, onClick, onContextMenu) {
                detectTapGestures(
                    onTap = { onClick() },
                    onPress = {
                        val releasedEarly = withTimeoutOrNull(2000L) {
                            tryAwaitRelease()
                            true
                        }
                        if (releasedEarly == null) {
                            onContextMenu()
                            tryAwaitRelease()
                        }
                    },
                )
            }
            .padding(horizontal = 10.dp, vertical = 8.dp),
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
            Icon(
                imageVector = visual.icon,
                contentDescription = null,
                tint = visual.tint,
                modifier = Modifier.size(22.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                entry.name,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                color = p.textPrimary,
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium,
            )
            Text(
                text = rowMeta(entry),
                style = MaterialTheme.typography.bodySmall,
                color = p.textSecondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (entry.isDirectory) {
            Icon(
                Icons.Default.ChevronRight,
                contentDescription = null,
                tint = p.textSecondary.copy(alpha = 0.7f),
                modifier = Modifier.size(20.dp),
            )
        }
    }
}

private val shortDateFormat: DateFormat = DateFormat.getDateInstance(DateFormat.MEDIUM)

private fun rowMeta(entry: FileEntry): String {
    val date = if (entry.lastModified > 0) shortDateFormat.format(Date(entry.lastModified)) else ""
    return if (entry.isDirectory) {
        date
    } else {
        listOf(formatSize(entry.size), date).filter { it.isNotEmpty() }.joinToString("  ·  ")
    }
}

private fun formatSize(bytes: Long): String = when {
    bytes < 1024 -> "$bytes B"
    bytes < 1024 * 1024 -> "${bytes / 1024} KB"
    else -> String.format("%.1f MB", bytes / (1024.0 * 1024.0))
}
