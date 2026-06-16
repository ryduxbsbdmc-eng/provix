package com.provix.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.provix.core.model.AppTheme
import com.provix.core.ui.theme.LocalProvixPalette

private val ProvixTypography = Typography(
    titleLarge = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.SemiBold, fontSize = 18.sp),
    titleMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Medium, fontSize = 14.sp),
    bodyMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 13.sp),
    bodySmall = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 11.sp),
    labelSmall = TextStyle(fontFamily = FontFamily.SansSerif, fontSize = 11.sp, fontWeight = FontWeight.Medium),
)

@Composable
fun ProvixTheme(
    theme: AppTheme = AppTheme.Dark,
    content: @Composable () -> Unit,
) {
    val context = LocalContext.current
    val palette = remember(theme) { ThemePackLoader.paletteFor(context, theme) }
    val isLight = theme == AppTheme.Light

    val colorScheme = if (isLight) {
        lightColorScheme(
            primary = palette.accent,
            onPrimary = Color.White,
            background = palette.background,
            onBackground = palette.textPrimary,
            surface = palette.paneSurface,
            onSurface = palette.textPrimary,
            surfaceVariant = palette.panel,
            onSurfaceVariant = palette.textSecondary,
            outline = palette.border,
            error = palette.error,
        )
    } else {
        darkColorScheme(
            primary = palette.accent,
            onPrimary = Color.White,
            background = palette.background,
            onBackground = palette.textPrimary,
            surface = palette.paneSurface,
            onSurface = palette.textPrimary,
            surfaceVariant = palette.panel,
            onSurfaceVariant = palette.textSecondary,
            outline = palette.border,
            error = palette.error,
        )
    }

    CompositionLocalProvider(LocalProvixPalette provides palette) {
        MaterialTheme(colorScheme = colorScheme, typography = ProvixTypography, content = content)
    }
}
