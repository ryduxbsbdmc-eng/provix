package com.provix.core.model

fun AppTheme.localizationKey(): String = when (this) {
    AppTheme.Dark -> "UI_ThemeDark"
    AppTheme.Light -> "UI_ThemeLight"
    AppTheme.Explorer -> "UI_ThemeExplorer"
    AppTheme.Midnight -> "UI_ThemeMidnight"
    AppTheme.Nord -> "UI_ThemeNord"
    AppTheme.Forest -> "UI_ThemeForest"
    AppTheme.Rose -> "UI_ThemeRose"
    AppTheme.Amoled -> "UI_ThemeAmoled"
    AppTheme.Custom -> "UI_ThemeCustom"
}

val selectableAppThemes = listOf(
    AppTheme.Dark,
    AppTheme.Light,
    AppTheme.Explorer,
    AppTheme.Midnight,
    AppTheme.Nord,
    AppTheme.Amoled,
)
