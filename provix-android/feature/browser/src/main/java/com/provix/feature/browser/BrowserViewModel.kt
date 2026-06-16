package com.provix.feature.browser

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.provix.core.filesystem.FileSystemRepository
import com.provix.core.model.ContentSearchMatch
import com.provix.core.model.FileEntry
import com.provix.core.settings.SettingsRepository
import com.provix.feature.archive.ArchiveRepository
import com.provix.feature.preview.PreviewState
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class BrowserUiState(
    val leftEntries: List<FileEntry> = emptyList(),
    val rightEntries: List<FileEntry> = emptyList(),
    val leftLoading: Boolean = false,
    val rightLoading: Boolean = false,
    val error: String? = null,
    val searchQuery: String = "",
    val contentSearchResults: List<ContentSearchMatch> = emptyList(),
    val preview: PreviewState? = null,
    val dualPane: Boolean = true,
)

@HiltViewModel
class BrowserViewModel @Inject constructor(
    private val fileSystem: FileSystemRepository,
    private val session: BrowserSessionRepository,
    private val settingsRepository: SettingsRepository,
    private val archiveRepository: ArchiveRepository,
) : ViewModel() {
    private val _uiState = MutableStateFlow(BrowserUiState())
    val uiState: StateFlow<BrowserUiState> = _uiState.asStateFlow()

    val leftPane = session.leftPane
    val rightPane = session.rightPane
    val activePaneId = session.activePaneId
    val bookmarks = session.bookmarks

    val settings = settingsRepository.settings.stateIn(
        viewModelScope,
        SharingStarted.WhileSubscribed(5_000),
        com.provix.core.model.AppSettings(),
    )

    private var leftLoadJob: Job? = null
    private var rightLoadJob: Job? = null
    private var searchJob: Job? = null

    init {
        viewModelScope.launch {
            leftPane.map { it.currentPath }.distinctUntilChanged().collect { loadPane(0, it) }
        }
        viewModelScope.launch {
            rightPane.map { it.currentPath }.distinctUntilChanged().collect { loadPane(1, it) }
        }
        viewModelScope.launch {
            settings.collect { prefs ->
                _uiState.update { it.copy(dualPane = prefs.dualPaneEnabled) }
            }
        }
    }

    fun setActivePane(id: Int) = session.setActivePane(id)

    fun navigate(paneId: Int, path: String) {
        session.navigate(paneId, path)
        loadPane(paneId, path)
    }

    fun goBack(paneId: Int) {
        session.goBack(paneId)
        loadPane(paneId, session.currentPath(paneId))
    }

    fun goForward(paneId: Int) {
        session.goForward(paneId)
        loadPane(paneId, session.currentPath(paneId))
    }

    fun refresh(paneId: Int = activePaneId.value) = loadPane(paneId, session.currentPath(paneId))

    fun toggleSelection(paneId: Int, path: String) = session.toggleSelection(paneId, path)

    fun selectItem(paneId: Int, path: String) = session.setSelection(paneId, setOf(path))

    fun otherPaneId(paneId: Int): Int = if (paneId == 0) 1 else 0

    fun addTab(paneId: Int) = session.addTab(paneId)

    fun closeTab(paneId: Int, tabId: String) = session.closeTab(paneId, tabId)

    fun selectTab(paneId: Int, index: Int) {
        session.selectTab(paneId, index)
        loadPane(paneId, session.currentPath(paneId))
    }

    fun openInOtherPane(fromPaneId: Int) {
        val path = session.currentPath(fromPaneId)
        session.openInOtherPane(fromPaneId, path)
        loadPane(if (fromPaneId == 0) 1 else 0, path)
    }

    fun addBookmark(path: String, label: String) = session.addBookmark(path, label)

    fun removeBookmark(path: String) = session.removeBookmark(path)

    fun deleteSelected(paneId: Int) {
        val paths = session.selectedPaths(paneId)
        viewModelScope.launch {
            paths.forEach { fileSystem.delete(it) }
            session.setSelection(paneId, emptySet())
            refresh(paneId)
        }
    }

    fun copySelected(fromPaneId: Int, toPaneId: Int) {
        val sources = session.selectedPaths(fromPaneId)
        val dest = session.currentPath(toPaneId)
        viewModelScope.launch {
            sources.forEach { fileSystem.copy(it, dest) }
            refresh(fromPaneId)
            refresh(toPaneId)
        }
    }

    fun moveSelected(fromPaneId: Int, toPaneId: Int) {
        val sources = session.selectedPaths(fromPaneId)
        val dest = session.currentPath(toPaneId)
        viewModelScope.launch {
            sources.forEach { fileSystem.move(it, dest) }
            session.setSelection(fromPaneId, emptySet())
            refresh(fromPaneId)
            refresh(toPaneId)
        }
    }

    fun createFolder(paneId: Int, name: String) {
        viewModelScope.launch {
            fileSystem.createDirectory(session.currentPath(paneId), name)
            refresh(paneId)
        }
    }

    fun rename(path: String, newName: String, paneId: Int = activePaneId.value) {
        viewModelScope.launch {
            fileSystem.rename(path, newName)
            refresh(paneId)
        }
    }

    fun deleteItem(paneId: Int, path: String) {
        viewModelScope.launch {
            fileSystem.delete(path)
            session.setSelection(paneId, emptySet())
            refresh(paneId)
        }
    }

    fun copyItem(fromPaneId: Int, path: String) {
        val toPaneId = otherPaneId(fromPaneId)
        val dest = session.currentPath(toPaneId)
        viewModelScope.launch {
            fileSystem.copy(path, dest)
            refresh(fromPaneId)
            refresh(toPaneId)
        }
    }

    fun moveItem(fromPaneId: Int, path: String) {
        val toPaneId = otherPaneId(fromPaneId)
        val dest = session.currentPath(toPaneId)
        viewModelScope.launch {
            fileSystem.move(path, dest)
            session.setSelection(fromPaneId, emptySet())
            refresh(fromPaneId)
            refresh(toPaneId)
        }
    }

    fun openEntryInOtherPane(fromPaneId: Int, entry: FileEntry) {
        val targetPath = if (entry.isDirectory) {
            entry.path
        } else {
            entry.path.substringBeforeLast('/', session.currentPath(fromPaneId))
        }
        session.openInOtherPane(fromPaneId, targetPath)
        loadPane(otherPaneId(fromPaneId), targetPath)
    }

    fun extractArchive(paneId: Int, archivePath: String) {
        viewModelScope.launch {
            val dest = session.currentPath(paneId)
            archiveRepository.extractAll(archivePath, dest)
                .onFailure { error -> _uiState.update { it.copy(error = error.message) } }
                .onSuccess { refresh(paneId) }
        }
    }

    fun searchByName(query: String) {
        _uiState.update { it.copy(searchQuery = query) }
        searchJob?.cancel()
        searchJob = viewModelScope.launch {
            val path = session.currentPath(activePaneId.value)
            val results = fileSystem.searchByName(path, query)
            _uiState.update {
                when (activePaneId.value) {
                    0 -> it.copy(leftEntries = results, leftLoading = false)
                    else -> it.copy(rightEntries = results, rightLoading = false)
                }
            }
        }
    }

    fun searchContent(query: String) {
        searchJob?.cancel()
        searchJob = viewModelScope.launch {
            val path = session.currentPath(activePaneId.value)
            val results = fileSystem.searchContent(path, query)
            _uiState.update { it.copy(contentSearchResults = results) }
        }
    }

    fun previewFile(entry: FileEntry) {
        viewModelScope.launch {
            if (entry.isDirectory) {
                navigate(activePaneId.value, entry.path)
                return@launch
            }
            val text = fileSystem.readTextPreview(entry.path).getOrNull()
            _uiState.update {
                it.copy(preview = PreviewState(entry.path, entry.name, text, entry.extension))
            }
        }
    }

    fun clearPreview() = _uiState.update { it.copy(preview = null) }

    private fun loadPane(paneId: Int, path: String) {
        if (paneId == 0) {
            leftLoadJob?.cancel()
            leftLoadJob = viewModelScope.launch {
                updatePaneLoading(0, true)
                runCatching { fileSystem.listDirectory(path) }
                    .onSuccess { entries -> updatePaneEntries(0, entries) }
                    .onFailure { error -> _uiState.update { it.copy(error = error.message) } }
                updatePaneLoading(0, false)
            }
        } else {
            rightLoadJob?.cancel()
            rightLoadJob = viewModelScope.launch {
                updatePaneLoading(1, true)
                runCatching { fileSystem.listDirectory(path) }
                    .onSuccess { entries -> updatePaneEntries(1, entries) }
                    .onFailure { error -> _uiState.update { it.copy(error = error.message) } }
                updatePaneLoading(1, false)
            }
        }
    }

    private fun updatePaneLoading(paneId: Int, loading: Boolean) {
        _uiState.update {
            if (paneId == 0) it.copy(leftLoading = loading) else it.copy(rightLoading = loading)
        }
    }

    private fun updatePaneEntries(paneId: Int, entries: List<FileEntry>) {
        _uiState.update {
            if (paneId == 0) it.copy(leftEntries = entries) else it.copy(rightEntries = entries)
        }
    }
}
