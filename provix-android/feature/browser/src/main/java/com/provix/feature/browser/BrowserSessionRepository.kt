package com.provix.feature.browser

import com.provix.core.model.Bookmark
import com.provix.core.model.PaneTab
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.util.UUID

private const val DEFAULT_ROOT = "/storage/emulated/0"

data class PaneState(
    val id: Int,
    val tabs: List<PaneTab> = listOf(PaneTab(UUID.randomUUID().toString(), DEFAULT_ROOT, "Home")),
    val activeTabIndex: Int = 0,
    val backStack: List<String> = emptyList(),
    val forwardStack: List<String> = emptyList(),
    val selectedPaths: Set<String> = emptySet(),
) {
    val activeTab: PaneTab get() = tabs[activeTabIndex]
    val currentPath: String get() = activeTab.path
}

class BrowserSessionRepository {
    private val _bookmarks = MutableStateFlow<List<Bookmark>>(emptyList())
    val bookmarks: StateFlow<List<Bookmark>> = _bookmarks.asStateFlow()

    private val _leftPane = MutableStateFlow(PaneState(id = 0))
    val leftPane: StateFlow<PaneState> = _leftPane.asStateFlow()

    private val _rightPane = MutableStateFlow(PaneState(id = 1))
    val rightPane: StateFlow<PaneState> = _rightPane.asStateFlow()

    private val _activePaneId = MutableStateFlow(0)
    val activePaneId: StateFlow<Int> = _activePaneId.asStateFlow()

    fun currentPath(paneId: Int): String = paneMutable(paneId).value.currentPath

    fun selectedPaths(paneId: Int): Set<String> = paneMutable(paneId).value.selectedPaths

    fun setActivePane(id: Int) {
        _activePaneId.value = id
    }

    fun navigate(paneId: Int, path: String) {
        paneMutable(paneId).update { pane ->
            val current = pane.currentPath
            if (current == path) return@update pane
            pane.copy(
                tabs = pane.tabs.mapIndexed { index, tab ->
                    if (index == pane.activeTabIndex) tab.copy(path = path, title = tabTitle(path)) else tab
                },
                backStack = pane.backStack + current,
                forwardStack = emptyList(),
                selectedPaths = emptySet(),
            )
        }
    }

    fun goBack(paneId: Int) {
        paneMutable(paneId).update { pane ->
            if (pane.backStack.isEmpty()) return@update pane
            val previous = pane.backStack.last()
            pane.copy(
                tabs = pane.tabs.mapIndexed { index, tab ->
                    if (index == pane.activeTabIndex) tab.copy(path = previous, title = tabTitle(previous)) else tab
                },
                backStack = pane.backStack.dropLast(1),
                forwardStack = listOf(pane.currentPath) + pane.forwardStack,
                selectedPaths = emptySet(),
            )
        }
    }

    fun goForward(paneId: Int) {
        paneMutable(paneId).update { pane ->
            if (pane.forwardStack.isEmpty()) return@update pane
            val next = pane.forwardStack.first()
            pane.copy(
                tabs = pane.tabs.mapIndexed { index, tab ->
                    if (index == pane.activeTabIndex) tab.copy(path = next, title = tabTitle(next)) else tab
                },
                forwardStack = pane.forwardStack.drop(1),
                backStack = pane.backStack + pane.currentPath,
                selectedPaths = emptySet(),
            )
        }
    }

    fun addTab(paneId: Int, path: String = DEFAULT_ROOT) {
        paneMutable(paneId).update { pane ->
            val newTab = PaneTab(UUID.randomUUID().toString(), path, tabTitle(path))
            pane.copy(tabs = pane.tabs + newTab, activeTabIndex = pane.tabs.size)
        }
    }

    fun closeTab(paneId: Int, tabId: String) {
        paneMutable(paneId).update { pane ->
            if (pane.tabs.size <= 1) return@update pane
            val index = pane.tabs.indexOfFirst { it.id == tabId }
            if (index < 0) return@update pane
            val newTabs = pane.tabs.filterNot { it.id == tabId }
            val newIndex = (pane.activeTabIndex - if (index < pane.activeTabIndex) 1 else 0).coerceIn(0, newTabs.lastIndex)
            pane.copy(tabs = newTabs, activeTabIndex = newIndex)
        }
    }

    fun selectTab(paneId: Int, index: Int) {
        paneMutable(paneId).update { it.copy(activeTabIndex = index.coerceIn(0, it.tabs.lastIndex)) }
    }

    fun toggleSelection(paneId: Int, path: String) {
        paneMutable(paneId).update { pane ->
            val selected = pane.selectedPaths.toMutableSet()
            if (!selected.add(path)) selected.remove(path)
            pane.copy(selectedPaths = selected)
        }
    }

    fun setSelection(paneId: Int, paths: Set<String>) {
        paneMutable(paneId).update { it.copy(selectedPaths = paths) }
    }

    fun openInOtherPane(fromPaneId: Int, path: String) {
        val targetId = if (fromPaneId == 0) 1 else 0
        navigate(targetId, path)
        _activePaneId.value = targetId
    }

    fun addBookmark(path: String, label: String) {
        _bookmarks.update { current ->
            if (current.any { it.path == path }) current
            else current + Bookmark(path = path, label = label.ifBlank { tabTitle(path) })
        }
    }

    fun removeBookmark(path: String) {
        _bookmarks.update { it.filterNot { bookmark -> bookmark.path == path } }
    }

    private fun paneMutable(paneId: Int): MutableStateFlow<PaneState> =
        if (paneId == 0) _leftPane else _rightPane

    companion object {
        fun defaultRoot(): String = DEFAULT_ROOT

        fun tabTitle(path: String): String = path.substringAfterLast('/').ifBlank { "Root" }
    }
}
