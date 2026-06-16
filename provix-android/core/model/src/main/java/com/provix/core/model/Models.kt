package com.provix.core.model

import java.util.Date

data class FileEntry(
    val name: String,
    val path: String,
    val isDirectory: Boolean,
    val size: Long,
    val lastModified: Long,
    val extension: String = if (isDirectory) "" else name.substringAfterLast('.', ""),
)

data class StorageVolumeInfo(
    val id: String,
    val label: String,
    val path: String,
    val isRemovable: Boolean,
    val isPrimary: Boolean,
)

data class Bookmark(
    val id: Long = 0,
    val path: String,
    val label: String,
    val createdAt: Long = System.currentTimeMillis(),
)

data class PaneTab(
    val id: String,
    val path: String,
    val title: String,
)

data class NavigationRecord(
    val path: String,
    val timestamp: Long = System.currentTimeMillis(),
)

enum class AppTheme {
    Dark,
    Light,
    Explorer,
    Midnight,
    Nord,
    Forest,
    Rose,
    Amoled,
    Custom,
}

enum class FileIconStyle {
    Windows,
    Material,
    Minimal,
    Custom,
}

enum class AiProvider {
    OpenRouter,
    Ollama,
    LmStudio,
}

enum class SortMode {
    Name,
    Size,
    Date,
    Type,
}

enum class SortDirection {
    Ascending,
    Descending,
}

data class AppSettings(
    val settingsVersion: Int = 1,
    val theme: AppTheme = AppTheme.Dark,
    val customThemePath: String = "",
    val language: String = "en-US",
    val fileIconStyle: FileIconStyle = FileIconStyle.Material,
    val useBuiltInMediaViewer: Boolean = true,
    val aiProvider: AiProvider = AiProvider.OpenRouter,
    val openRouterApiKey: String = "",
    val localAiEndpoint: String = "",
    val preferredAiModel: String = "openai/gpt-4o-mini",
    val scrollSensitivity: Float = 1f,
    val showPreviewPanel: Boolean = true,
    val previewPanelWidthDp: Int = 280,
    val dualPaneEnabled: Boolean = true,
    val activePaneIndex: Int = 0,
)

data class ContentSearchMatch(
    val filePath: String,
    val fileName: String,
    val lineNumber: Int,
    val linePreview: String,
)

data class ArchiveEntry(
    val name: String,
    val path: String,
    val isDirectory: Boolean,
    val size: Long,
    val lastModified: Long,
)
