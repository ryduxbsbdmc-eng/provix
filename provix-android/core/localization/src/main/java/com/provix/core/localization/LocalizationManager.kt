package com.provix.core.localization

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONObject
import java.util.Locale

class LocalizationManager private constructor(context: Context) {
    private val appContext = context.applicationContext
    private val strings = mutableMapOf<String, String>()
    private val _locale = MutableStateFlow("en-US")
    val locale: StateFlow<String> = _locale.asStateFlow()

    /** Increments when strings reload — bind Compose UI to this for recomposition. */
    private val _stringsVersion = MutableStateFlow(0)
    val stringsVersion: StateFlow<Int> = _stringsVersion.asStateFlow()

    val availableLocales = listOf(
        LocaleOption("en-US", "English"),
        LocaleOption("ru-RU", "Русский"),
        LocaleOption("uk-UA", "Українська"),
    )

    fun load(localeCode: String) {
        val normalized = localeCode.ifBlank { "en-US" }
        val assetPath = "Locales/$normalized.json"
        runCatching {
            val json = appContext.assets.open(assetPath).bufferedReader().use { it.readText() }
            val obj = JSONObject(json)
            strings.clear()
            obj.keys().forEach { key ->
                strings[key] = obj.optString(key, key)
            }
            _locale.value = normalized
            _stringsVersion.value += 1
        }.onFailure {
            if (normalized != "en-US") load("en-US")
        }
    }

    operator fun get(key: String): String = strings[key] ?: key

    fun get(key: String, vararg args: Any?): String {
        var value = get(key)
        args.forEachIndexed { index, arg ->
            value = value.replace("{$index}", arg?.toString() ?: "")
        }
        return value
    }

    data class LocaleOption(val code: String, val displayName: String)

    companion object {
        @Volatile
        private var instance: LocalizationManager? = null

        fun getInstance(context: Context): LocalizationManager =
            instance ?: synchronized(this) {
                instance ?: LocalizationManager(context).also { manager ->
                    instance = manager
                    manager.load(systemDefaultLocale())
                }
            }

        private fun systemDefaultLocale(): String {
            val tag = Locale.getDefault().toLanguageTag()
            return when {
                tag.startsWith("ru") -> "ru-RU"
                tag.startsWith("uk") -> "uk-UA"
                else -> "en-US"
            }
        }
    }
}
