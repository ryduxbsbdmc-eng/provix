package com.provix.core.filesystem

import android.content.Context
import android.os.Build
import android.os.Environment
import android.os.storage.StorageManager
import android.os.storage.StorageVolume
import com.provix.core.model.ContentSearchMatch
import com.provix.core.model.FileEntry
import com.provix.core.model.SortDirection
import com.provix.core.model.SortMode
import com.provix.core.model.StorageVolumeInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.File
import java.io.FileInputStream
import java.io.InputStreamReader
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.Locale

class FileSystemRepository(private val context: Context) {
    private val skippedSearchDirs = setOf(
        ".git", ".vs", ".idea", "node_modules", "__pycache__",
        "bin", "obj", "packages", "vendor", "Android", "data",
    )

    suspend fun getStorageVolumes(): List<StorageVolumeInfo> = withContext(Dispatchers.IO) {
        val result = mutableListOf<StorageVolumeInfo>()
        val storageManager = context.getSystemService(StorageManager::class.java)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            storageManager.storageVolumes.forEach { volume ->
                val path = volume.directory?.absolutePath ?: return@forEach
                result += StorageVolumeInfo(
                    id = volume.uuid ?: path,
                    label = volume.getDescription(context)?.toString() ?: path,
                    path = path,
                    isRemovable = volume.isRemovable,
                    isPrimary = volume.isPrimary,
                )
            }
        }

        if (result.isEmpty()) {
            val root = Environment.getExternalStorageDirectory()
            result += StorageVolumeInfo(
                id = "primary",
                label = "Internal storage",
                path = root.absolutePath,
                isRemovable = false,
                isPrimary = true,
            )
        }
        result
    }

    suspend fun listDirectory(
        path: String,
        sortMode: SortMode = SortMode.Name,
        sortDirection: SortDirection = SortDirection.Ascending,
    ): List<FileEntry> = withContext(Dispatchers.IO) {
        val dir = File(path)
        if (!dir.exists() || !dir.isDirectory) return@withContext emptyList()

        dir.listFiles()
            ?.map { file ->
                FileEntry(
                    name = file.name,
                    path = file.absolutePath,
                    isDirectory = file.isDirectory,
                    size = if (file.isDirectory) 0L else file.length(),
                    lastModified = file.lastModified(),
                )
            }
            ?.sortedWith(compareFiles(sortMode, sortDirection))
            ?: emptyList()
    }

    suspend fun searchByName(
        rootPath: String,
        query: String,
        maxResults: Int = 2500,
        maxDepth: Int = 32,
    ): List<FileEntry> = withContext(Dispatchers.IO) {
        val results = mutableListOf<FileEntry>()
        val normalized = query.trim().lowercase(Locale.ROOT)
        if (normalized.isEmpty()) return@withContext emptyList()

        fun walk(dir: File, depth: Int) {
            if (results.size >= maxResults || depth > maxDepth) return
            val files = dir.listFiles() ?: return
            for (file in files) {
                if (results.size >= maxResults) return
                if (file.isDirectory && file.name in skippedSearchDirs) continue
                if (file.name.lowercase(Locale.ROOT).contains(normalized)) {
                    results += FileEntry(
                        name = file.name,
                        path = file.absolutePath,
                        isDirectory = file.isDirectory,
                        size = if (file.isDirectory) 0L else file.length(),
                        lastModified = file.lastModified(),
                    )
                }
                if (file.isDirectory) walk(file, depth + 1)
            }
        }

        walk(File(rootPath), 0)
        results
    }

    suspend fun searchContent(
        rootPath: String,
        query: String,
        extensions: Set<String> = setOf("txt", "cs", "json", "md", "xml", "kt", "java"),
        maxResults: Int = 500,
    ): List<ContentSearchMatch> = withContext(Dispatchers.IO) {
        val results = mutableListOf<ContentSearchMatch>()
        val needle = query.trim().lowercase(Locale.ROOT)
        if (needle.isEmpty()) return@withContext emptyList()

        fun walk(dir: File, depth: Int) {
            if (results.size >= maxResults || depth > 12) return
            val files = dir.listFiles() ?: return
            for (file in files) {
                if (results.size >= maxResults) return
                if (file.isDirectory) {
                    if (file.name !in skippedSearchDirs) walk(file, depth + 1)
                    continue
                }
                val ext = file.extension.lowercase(Locale.ROOT)
                if (ext !in extensions || file.length() > 2_000_000) continue
                try {
                    BufferedReader(InputStreamReader(FileInputStream(file))).use { reader ->
                        var lineNo = 0
                        reader.lineSequence().forEach { line ->
                            lineNo++
                            if (line.lowercase(Locale.ROOT).contains(needle)) {
                                results += ContentSearchMatch(
                                    filePath = file.absolutePath,
                                    fileName = file.name,
                                    lineNumber = lineNo,
                                    linePreview = line.trim().take(200),
                                )
                                if (results.size >= maxResults) return@use
                            }
                        }
                    }
                } catch (_: Exception) {
                }
            }
        }

        walk(File(rootPath), 0)
        results
    }

    suspend fun copy(source: String, destinationDir: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val src = File(source)
            val destDir = File(destinationDir)
            require(destDir.isDirectory) { "Destination is not a directory" }
            val dest = File(destDir, src.name)
            if (src.isDirectory) {
                copyDirectory(src, dest)
            } else {
                Files.copy(src.toPath(), dest.toPath(), StandardCopyOption.REPLACE_EXISTING)
            }
            Unit
        }
    }

    suspend fun move(source: String, destinationDir: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val src = File(source)
            val destDir = File(destinationDir)
            require(destDir.isDirectory) { "Destination is not a directory" }
            val dest = File(destDir, src.name)
            if (!src.renameTo(dest)) {
                if (src.isDirectory) {
                    copyDirectory(src, dest)
                } else {
                    Files.copy(src.toPath(), dest.toPath(), StandardCopyOption.REPLACE_EXISTING)
                }
                deleteRecursive(src)
            }
            Unit
        }
    }

    suspend fun delete(path: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching { deleteRecursive(File(path)) }
    }

    suspend fun createDirectory(parentPath: String, name: String): Result<FileEntry> = withContext(Dispatchers.IO) {
        runCatching {
            val dir = File(parentPath, name)
            check(dir.mkdirs() || dir.exists()) { "Could not create directory" }
            FileEntry(dir.name, dir.absolutePath, true, 0L, dir.lastModified())
        }
    }

    suspend fun rename(path: String, newName: String): Result<FileEntry> = withContext(Dispatchers.IO) {
        runCatching {
            val src = File(path)
            val dest = File(src.parentFile, newName)
            check(src.renameTo(dest)) { "Rename failed" }
            FileEntry(dest.name, dest.absolutePath, dest.isDirectory, if (dest.isDirectory) 0L else dest.length(), dest.lastModified())
        }
    }

    suspend fun readTextPreview(path: String, maxChars: Int = 32_768): Result<String> = withContext(Dispatchers.IO) {
        runCatching {
            File(path).readText().take(maxChars)
        }
    }

    private fun copyDirectory(source: File, dest: File) {
        if (!dest.exists()) dest.mkdirs()
        source.listFiles()?.forEach { child ->
            val target = File(dest, child.name)
            if (child.isDirectory) copyDirectory(child, target) else Files.copy(child.toPath(), target.toPath(), StandardCopyOption.REPLACE_EXISTING)
        }
    }

    private fun deleteRecursive(file: File) {
        if (file.isDirectory) file.listFiles()?.forEach { deleteRecursive(it) }
        file.delete()
    }

    private fun compareFiles(sortMode: SortMode, sortDirection: SortDirection): Comparator<FileEntry> {
        val base = when (sortMode) {
            SortMode.Name -> compareBy<FileEntry> { !it.isDirectory }.thenBy { it.name.lowercase(Locale.ROOT) }
            SortMode.Size -> compareBy<FileEntry> { !it.isDirectory }.thenBy { it.size }
            SortMode.Date -> compareBy<FileEntry> { !it.isDirectory }.thenBy { it.lastModified }
            SortMode.Type -> compareBy<FileEntry> { !it.isDirectory }.thenBy { it.extension.lowercase(Locale.ROOT) }.thenBy { it.name.lowercase(Locale.ROOT) }
        }
        return if (sortDirection == SortDirection.Descending) base.reversed() else base
    }
}
