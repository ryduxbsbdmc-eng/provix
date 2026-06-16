package com.provix

import android.Manifest
import android.content.Intent
import android.content.res.Configuration
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.NavigationRailItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.provix.core.localization.LocalizationManager
import com.provix.core.model.AppSettings
import com.provix.core.settings.SettingsRepository
import com.provix.core.ui.theme.LocalProvixPalette
import com.provix.feature.browser.BrowserScreen
import com.provix.feature.browser.BrowserViewModel
import com.provix.feature.settings.SettingsScreen
import com.provix.ui.screens.ArchiveScreen
import com.provix.ui.theme.ProvixTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {
    @Inject lateinit var localizationManager: LocalizationManager
    @Inject lateinit var settingsRepository: SettingsRepository

    private val storagePermission = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions(),
    ) { requestAllFilesAccessIfNeeded() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        requestStorageAccess()

        setContent {
            val settings by settingsRepository.settings.collectAsState(initial = AppSettings())
            val stringsVersion by localizationManager.stringsVersion.collectAsState()

            LaunchedEffect(settings.language) {
                localizationManager.load(settings.language)
            }

            ProvixTheme(theme = settings.theme) {
                key(stringsVersion) {
                    ProvixApp(
                        loc = localizationManager,
                        settingsRepository = settingsRepository,
                    )
                }
            }
        }
    }

    private fun requestStorageAccess() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (!Environment.isExternalStorageManager()) {
                val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                    data = Uri.parse("package:$packageName")
                }
                startActivity(intent)
            }
        } else {
            storagePermission.launch(
                arrayOf(
                    Manifest.permission.READ_EXTERNAL_STORAGE,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE,
                ),
            )
        }
    }

    private fun requestAllFilesAccessIfNeeded() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && !Environment.isExternalStorageManager()) {
            startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
        }
    }
}

private sealed class ProvixRoute(val route: String, val labelKey: String) {
    data object Browser : ProvixRoute("browser", "UI_AppTitle")
    data object Archive : ProvixRoute("archive", "UI_ArchivePasswordTitle")
    data object Settings : ProvixRoute("settings", "UI_Settings")
}

@Composable
private fun ProvixApp(
    loc: LocalizationManager,
    settingsRepository: SettingsRepository,
) {
    val navController = rememberNavController()
    val backStack by navController.currentBackStackEntryAsState()
    val currentRoute = backStack?.destination?.route ?: ProvixRoute.Browser.route
    val isLandscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val palette = LocalProvixPalette.current

    val navItems = listOf(
        ProvixRoute.Browser,
        ProvixRoute.Archive,
        ProvixRoute.Settings,
    )

    fun navigateTo(route: String) {
        navController.navigate(route) {
            popUpTo(ProvixRoute.Browser.route) { saveState = true }
            launchSingleTop = true
            restoreState = true
        }
    }

    @Composable
    fun NavIcon(item: ProvixRoute) {
        Icon(
            imageVector = when (item) {
                ProvixRoute.Browser -> Icons.Default.Folder
                ProvixRoute.Archive -> Icons.Default.Archive
                ProvixRoute.Settings -> Icons.Default.Settings
            },
            contentDescription = loc[item.labelKey],
        )
    }

    val selectedColor = Color.White
    val unselectedColor = palette.textSecondary
    val indicatorColor = palette.accent.copy(alpha = 0.35f)

    if (isLandscape) {
        Row(
            modifier = Modifier
                .fillMaxSize()
                .background(palette.background),
        ) {
            NavigationRail(
                modifier = Modifier
                    .fillMaxHeight()
                    .background(palette.navBackground),
                containerColor = palette.navBackground,
            ) {
                navItems.forEach { item ->
                    NavigationRailItem(
                        selected = currentRoute == item.route,
                        onClick = { navigateTo(item.route) },
                        icon = { NavIcon(item) },
                        label = { Text(loc[item.labelKey].take(10)) },
                        colors = NavigationRailItemDefaults.colors(
                            selectedIconColor = selectedColor,
                            selectedTextColor = selectedColor,
                            indicatorColor = indicatorColor,
                            unselectedIconColor = unselectedColor,
                            unselectedTextColor = unselectedColor,
                        ),
                    )
                }
            }
            ProvixNavHost(
                navController = navController,
                loc = loc,
                settingsRepository = settingsRepository,
                modifier = Modifier.weight(1f),
            )
        }
    } else {
        Scaffold(
            modifier = Modifier.fillMaxSize(),
            containerColor = palette.background,
            bottomBar = {
                NavigationBar(
                    containerColor = palette.navBackground,
                    contentColor = palette.textSecondary,
                ) {
                    navItems.forEach { item ->
                        NavigationBarItem(
                            selected = currentRoute == item.route,
                            onClick = { navigateTo(item.route) },
                            icon = { NavIcon(item) },
                            label = { Text(loc[item.labelKey].take(8)) },
                            colors = NavigationBarItemDefaults.colors(
                                selectedIconColor = selectedColor,
                                selectedTextColor = selectedColor,
                                indicatorColor = indicatorColor,
                                unselectedIconColor = unselectedColor,
                                unselectedTextColor = unselectedColor,
                            ),
                        )
                    }
                }
            },
        ) { padding ->
            ProvixNavHost(
                navController = navController,
                loc = loc,
                settingsRepository = settingsRepository,
                modifier = Modifier.padding(padding),
            )
        }
    }
}

@Composable
private fun ProvixNavHost(
    navController: androidx.navigation.NavHostController,
    loc: LocalizationManager,
    settingsRepository: SettingsRepository,
    modifier: Modifier = Modifier,
) {
    NavHost(
        navController = navController,
        startDestination = ProvixRoute.Browser.route,
        modifier = modifier,
    ) {
        composable(ProvixRoute.Browser.route) {
            val viewModel: BrowserViewModel = hiltViewModel()
            BrowserScreen(
                viewModel = viewModel,
                loc = loc,
                onOpenSettings = { navController.navigate(ProvixRoute.Settings.route) },
            )
        }
        composable(ProvixRoute.Archive.route) {
            ArchiveScreen(loc = loc)
        }
        composable(ProvixRoute.Settings.route) {
            SettingsScreen(
                settingsRepository = settingsRepository,
                loc = loc,
                onReloadLocale = { loc.load(it) },
            )
        }
    }
}
