package com.provix.ui.theme

import android.content.Context
import com.provix.core.model.AppTheme
import com.provix.core.model.localizationKey
import com.provix.core.model.selectableAppThemes
import com.provix.core.ui.theme.ProvixPalette
import org.json.JSONObject

object ThemePackLoader {
    fun paletteFor(context: Context, theme: AppTheme): ProvixPalette = when (theme) {
        AppTheme.Light -> ProvixPalette.light()
        AppTheme.Amoled -> ProvixPalette.amoled()
        AppTheme.Midnight -> loadFromAsset(context, "Themes/Packs/catppuccin-mocha.json")
        AppTheme.Nord -> loadFromAsset(context, "Themes/Packs/dracula.json")
        AppTheme.Dark, AppTheme.Explorer, AppTheme.Forest, AppTheme.Rose, AppTheme.Custom ->
            ProvixPalette.darkDefault()
    }

    private fun loadFromAsset(context: Context, assetPath: String): ProvixPalette {
        return runCatching {
            val json = context.assets.open(assetPath).bufferedReader().use { it.readText() }
            paletteFromJson(json)
        }.getOrElse { ProvixPalette.darkDefault() }
    }

    private fun paletteFromJson(json: String): ProvixPalette {
        val root = JSONObject(json)
        val colors = root.optJSONObject("colors") ?: root
        fun c(key: String, fallback: androidx.compose.ui.graphics.Color): androidx.compose.ui.graphics.Color {
            val raw = colors.optString(key, "")
            return if (raw.isNotBlank()) parseHexColor(raw) else fallback
        }
        val base = ProvixPalette.darkDefault()
        return base.copy(
            background = c("GlassBackgroundBrush", base.background),
            panel = c("PanelBackgroundBrush", base.panel),
            paneSurface = c("OpaqueInputBackgroundBrush", base.paneSurface),
            titleBar = c("TitleBarBackgroundBrush", base.titleBar),
            border = c("BorderBrushDark", base.border),
            textPrimary = c("TextPrimaryBrush", base.textPrimary),
            textSecondary = c("TextSecondaryBrush", base.textSecondary),
            accent = c("AccentBrush", base.accent),
            selectionFill = c("SelectionFillBrush", base.selectionFill),
            selectionBorder = c("SelectionBorderBrush", base.selectionBorder),
            addressBar = c("AddressBarBackgroundBrush", base.addressBar),
            buttonChrome = c("ButtonChromeBrush", base.buttonChrome),
            splitter = c("SplitterIdleBrush", base.splitter),
            statusBar = c("StatusBarBackgroundBrush", base.statusBar),
            tabSelected = c("SelectionFillBrush", base.tabSelected),
            tabBorder = c("SelectionBorderBrush", base.tabBorder),
            navBackground = c("ModalPanelBackgroundBrush", base.navBackground),
            error = c("ErrorTextBrush", base.error),
        )
    }

    private fun parseHexColor(hex: String): androidx.compose.ui.graphics.Color {
        val h = hex.trim().removePrefix("#")
        val value = h.toLongOrNull(16) ?: return androidx.compose.ui.graphics.Color.White
        return when (h.length) {
            8 -> androidx.compose.ui.graphics.Color(value.toInt())
            6 -> androidx.compose.ui.graphics.Color(0xFF000000.toInt() or value.toInt())
            else -> androidx.compose.ui.graphics.Color.White
        }
    }
}

fun themeLabelKey(theme: AppTheme): String = theme.localizationKey()

val selectableThemes = selectableAppThemes
