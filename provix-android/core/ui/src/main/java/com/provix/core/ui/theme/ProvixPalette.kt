package com.provix.core.ui.theme

import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.graphics.Color

data class ProvixPalette(
    val background: Color,
    val panel: Color,
    val paneSurface: Color,
    val titleBar: Color,
    val border: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val accent: Color,
    val selectionFill: Color,
    val selectionBorder: Color,
    val addressBar: Color,
    val buttonChrome: Color,
    val splitter: Color,
    val statusBar: Color,
    val folderTint: Color,
    val tabSelected: Color,
    val tabBorder: Color,
    val navBackground: Color,
    val paneActiveBorder: Color = Color(0xFF0078D7),
    val paneInactiveBorder: Color = Color(0x33FFFFFF),
    val error: Color,
    // Optional depth tokens — fall back to sensible dark values so theme packs
    // that only set the core colors still render correctly.
    val surfaceElevated: Color = Color(0xFF202126),
    val rowHover: Color = Color(0x14FFFFFF),
    val iconSurface: Color = Color(0x18FFFFFF),
    val accentSoft: Color = Color(0x294DA3FF),
) {
    companion object {
        fun darkDefault() = ProvixPalette(
            background = Color(0xFF101216),
            panel = Color(0xFF181A1F),
            paneSurface = Color(0xFF15171C),
            titleBar = Color(0xFF181A20),
            border = Color(0x1FFFFFFF),
            textPrimary = Color(0xFFF2F4F8),
            textSecondary = Color(0xFF9AA3B2),
            accent = Color(0xFF4DA3FF),
            selectionFill = Color(0x2E4DA3FF),
            selectionBorder = Color(0xFF4DA3FF),
            addressBar = Color(0xFF1E2026),
            buttonChrome = Color(0x14FFFFFF),
            splitter = Color(0x14FFFFFF),
            statusBar = Color(0xFF15171C),
            folderTint = Color(0xFFFFC83D),
            tabSelected = Color(0x2E4DA3FF),
            tabBorder = Color(0x664DA3FF),
            navBackground = Color(0xFF14161B),
            paneActiveBorder = Color(0xFF4DA3FF),
            paneInactiveBorder = Color(0x1FFFFFFF),
            error = Color(0xFFFF6B6B),
            surfaceElevated = Color(0xFF202229),
            rowHover = Color(0x12FFFFFF),
            iconSurface = Color(0x16FFFFFF),
            accentSoft = Color(0x294DA3FF),
        )

        fun light() = ProvixPalette(
            background = Color(0xFFF3F5F9),
            panel = Color(0xFFFFFFFF),
            paneSurface = Color(0xFFFFFFFF),
            titleBar = Color(0xFFFFFFFF),
            border = Color(0x14000000),
            textPrimary = Color(0xFF161A22),
            textSecondary = Color(0xFF5C6473),
            accent = Color(0xFF0067D6),
            selectionFill = Color(0x1F0067D6),
            selectionBorder = Color(0xFF0067D6),
            addressBar = Color(0xFFEEF1F6),
            buttonChrome = Color(0xFFEDF0F5),
            splitter = Color(0x12000000),
            statusBar = Color(0xFFFFFFFF),
            folderTint = Color(0xFFE0A100),
            tabSelected = Color(0x1F0067D6),
            tabBorder = Color(0x4D0067D6),
            navBackground = Color(0xFFFFFFFF),
            paneActiveBorder = Color(0xFF0067D6),
            paneInactiveBorder = Color(0x14000000),
            error = Color(0xFFC62828),
            surfaceElevated = Color(0xFFFFFFFF),
            rowHover = Color(0x0A000000),
            iconSurface = Color(0x0F000000),
            accentSoft = Color(0x1F0067D6),
        )

        fun amoled() = darkDefault().copy(
            background = Color(0xFF000000),
            panel = Color(0xFF080808),
            paneSurface = Color(0xFF050505),
            titleBar = Color(0xFF080808),
            statusBar = Color(0xFF050505),
            navBackground = Color(0xFF000000),
            surfaceElevated = Color(0xFF121212),
            addressBar = Color(0xFF101010),
        )
    }
}

val LocalProvixPalette = compositionLocalOf { ProvixPalette.darkDefault() }
