package com.provix.di

import android.content.Context
import com.provix.core.filesystem.FileSystemRepository
import com.provix.core.localization.LocalizationManager
import com.provix.core.settings.SettingsRepository
import com.provix.feature.archive.ArchiveRepository
import com.provix.feature.browser.BrowserSessionRepository
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object AppModule {
    @Provides
    @Singleton
    fun provideLocalization(@ApplicationContext context: Context): LocalizationManager =
        LocalizationManager.getInstance(context)

    @Provides
    @Singleton
    fun provideFileSystem(@ApplicationContext context: Context): FileSystemRepository =
        FileSystemRepository(context)

    @Provides
    @Singleton
    fun provideSettings(@ApplicationContext context: Context): SettingsRepository =
        SettingsRepository(context)

    @Provides
    @Singleton
    fun provideBrowserSession(): BrowserSessionRepository = BrowserSessionRepository()

    @Provides
    @Singleton
    fun provideArchive(): ArchiveRepository = ArchiveRepository()
}
