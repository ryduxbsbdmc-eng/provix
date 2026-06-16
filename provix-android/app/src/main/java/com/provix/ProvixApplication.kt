package com.provix

import android.app.Application
import com.provix.core.localization.LocalizationManager
import dagger.hilt.android.HiltAndroidApp

@HiltAndroidApp
class ProvixApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        LocalizationManager.getInstance(this)
    }
}
