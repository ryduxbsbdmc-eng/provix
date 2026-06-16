package com.provix.core.settings

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.floatPreferencesKey
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.provix.core.model.AiProvider
import com.provix.core.model.AppSettings
import com.provix.core.model.AppTheme
import com.provix.core.model.FileIconStyle
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "provix_settings")

class SettingsRepository(private val context: Context) {
    val settings: Flow<AppSettings> = context.dataStore.data.map { prefs ->
        AppSettings(
            settingsVersion = prefs[KEY_VERSION] ?: 1,
            theme = AppTheme.entries.getOrElse(prefs[KEY_THEME] ?: 0) { AppTheme.Dark },
            customThemePath = prefs[KEY_CUSTOM_THEME] ?: "",
            language = prefs[KEY_LANGUAGE] ?: "en-US",
            fileIconStyle = FileIconStyle.entries.getOrElse(prefs[KEY_ICON_STYLE] ?: 1) { FileIconStyle.Material },
            useBuiltInMediaViewer = prefs[KEY_MEDIA_VIEWER] ?: true,
            aiProvider = AiProvider.entries.getOrElse(prefs[KEY_AI_PROVIDER] ?: 0) { AiProvider.OpenRouter },
            openRouterApiKey = prefs[KEY_OPENROUTER_KEY] ?: "",
            localAiEndpoint = prefs[KEY_LOCAL_AI] ?: "",
            preferredAiModel = prefs[KEY_AI_MODEL] ?: "openai/gpt-4o-mini",
            scrollSensitivity = prefs[KEY_SCROLL] ?: 1f,
            showPreviewPanel = prefs[KEY_PREVIEW] ?: true,
            previewPanelWidthDp = prefs[KEY_PREVIEW_WIDTH] ?: 280,
            dualPaneEnabled = prefs[KEY_DUAL_PANE] ?: true,
            activePaneIndex = prefs[KEY_ACTIVE_PANE] ?: 0,
        )
    }

    suspend fun update(transform: (AppSettings) -> AppSettings) {
        val current = settings.first()
        val updated = transform(current)
        context.dataStore.edit { prefs ->
            prefs[KEY_VERSION] = updated.settingsVersion
            prefs[KEY_THEME] = updated.theme.ordinal
            prefs[KEY_CUSTOM_THEME] = updated.customThemePath
            prefs[KEY_LANGUAGE] = updated.language
            prefs[KEY_ICON_STYLE] = updated.fileIconStyle.ordinal
            prefs[KEY_MEDIA_VIEWER] = updated.useBuiltInMediaViewer
            prefs[KEY_AI_PROVIDER] = updated.aiProvider.ordinal
            prefs[KEY_OPENROUTER_KEY] = updated.openRouterApiKey
            prefs[KEY_LOCAL_AI] = updated.localAiEndpoint
            prefs[KEY_AI_MODEL] = updated.preferredAiModel
            prefs[KEY_SCROLL] = updated.scrollSensitivity
            prefs[KEY_PREVIEW] = updated.showPreviewPanel
            prefs[KEY_PREVIEW_WIDTH] = updated.previewPanelWidthDp
            prefs[KEY_DUAL_PANE] = updated.dualPaneEnabled
            prefs[KEY_ACTIVE_PANE] = updated.activePaneIndex
        }
    }

    companion object {
        private val KEY_VERSION = intPreferencesKey("settings_version")
        private val KEY_THEME = intPreferencesKey("theme")
        private val KEY_CUSTOM_THEME = stringPreferencesKey("custom_theme_path")
        private val KEY_LANGUAGE = stringPreferencesKey("language")
        private val KEY_ICON_STYLE = intPreferencesKey("icon_style")
        private val KEY_MEDIA_VIEWER = booleanPreferencesKey("media_viewer")
        private val KEY_AI_PROVIDER = intPreferencesKey("ai_provider")
        private val KEY_OPENROUTER_KEY = stringPreferencesKey("openrouter_key")
        private val KEY_LOCAL_AI = stringPreferencesKey("local_ai_endpoint")
        private val KEY_AI_MODEL = stringPreferencesKey("ai_model")
        private val KEY_SCROLL = floatPreferencesKey("scroll_sensitivity")
        private val KEY_PREVIEW = booleanPreferencesKey("show_preview")
        private val KEY_PREVIEW_WIDTH = intPreferencesKey("preview_width")
        private val KEY_DUAL_PANE = booleanPreferencesKey("dual_pane")
        private val KEY_ACTIVE_PANE = intPreferencesKey("active_pane")
    }
}
