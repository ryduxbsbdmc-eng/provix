package com.provix.feature.settings

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.provix.core.localization.LocalizationManager
import com.provix.core.model.AppSettings
import com.provix.core.model.AppTheme
import com.provix.core.model.localizationKey
import com.provix.core.model.selectableAppThemes
import com.provix.core.settings.SettingsRepository
import kotlinx.coroutines.launch

private const val SUPPORT_WALLET = "0xBA59542F7c327448cAfd6bD786e89071dd246dcC"

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    settingsRepository: SettingsRepository,
    loc: LocalizationManager,
    onReloadLocale: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val settings by settingsRepository.settings.collectAsState(initial = AppSettings())
    var tab by remember { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()
    val context = LocalContext.current

    Column(modifier = modifier.fillMaxSize()) {
        TabRow(selectedTabIndex = tab) {
            Tab(selected = tab == 0, onClick = { tab = 0 }, text = { Text(loc["UI_SettingsTabGeneral"]) })
            Tab(selected = tab == 1, onClick = { tab = 1 }, text = { Text(loc["UI_SettingsTabSupport"]) })
        }

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (tab) {
                0 -> GeneralSettings(
                    settings = settings,
                    loc = loc,
                    onLanguageChange = { code ->
                        scope.launch {
                            settingsRepository.update { it.copy(language = code) }
                            loc.load(code)
                            onReloadLocale(code)
                        }
                    },
                    onThemeChange = { theme ->
                        scope.launch { settingsRepository.update { it.copy(theme = theme) } }
                    },
                    onDualPaneChange = { enabled ->
                        scope.launch { settingsRepository.update { it.copy(dualPaneEnabled = enabled) } }
                    },
                )
                1 -> SupportTab(context = context, loc = loc)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun GeneralSettings(
    settings: AppSettings,
    loc: LocalizationManager,
    onLanguageChange: (String) -> Unit,
    onThemeChange: (AppTheme) -> Unit,
    onDualPaneChange: (Boolean) -> Unit,
) {
    LanguageDropdown(settings.language, loc, onLanguageChange)
    ThemeDropdown(settings.theme, loc, onThemeChange)
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(loc["UI_DualPane"] ?: "Dual pane")
        Switch(checked = settings.dualPaneEnabled, onCheckedChange = onDualPaneChange)
    }
}

@Composable
private fun SupportTab(context: Context, loc: LocalizationManager) {
    Text(loc["UI_SupportIntro"])
    Text("${loc["UI_SupportToken"]} · ${loc["UI_SupportNetwork"]}")
    Text(SUPPORT_WALLET)
    Text(loc["UI_SupportNetworkWarning"])
    Button(onClick = {
        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("wallet", SUPPORT_WALLET))
    }) {
        Text(loc["UI_SupportCopyAddress"])
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LanguageDropdown(
    current: String,
    loc: LocalizationManager,
    onChange: (String) -> Unit,
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
        OutlinedTextField(
            value = loc.availableLocales.firstOrNull { it.code == current }?.displayName ?: current,
            onValueChange = {},
            readOnly = true,
            label = { Text(loc["UI_Language"]) },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
            modifier = Modifier
                .menuAnchor()
                .fillMaxWidth(),
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            loc.availableLocales.forEach { option ->
                DropdownMenuItem(
                    text = { Text(option.displayName) },
                    onClick = {
                        expanded = false
                        onChange(option.code)
                    },
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ThemeDropdown(
    current: AppTheme,
    loc: LocalizationManager,
    onChange: (AppTheme) -> Unit,
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
        OutlinedTextField(
            value = loc[current.localizationKey()],
            onValueChange = {},
            readOnly = true,
            label = { Text(loc["UI_Theme"]) },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
            modifier = Modifier
                .menuAnchor()
                .fillMaxWidth(),
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            selectableAppThemes.forEach { theme ->
                DropdownMenuItem(
                    text = { Text(loc[theme.localizationKey()]) },
                    onClick = {
                        expanded = false
                        onChange(theme)
                    },
                )
            }
        }
    }
}
