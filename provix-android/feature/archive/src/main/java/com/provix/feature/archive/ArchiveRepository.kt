package com.provix.feature.archive

import com.github.junrar.Archive
import com.provix.core.model.ArchiveEntry
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry
import org.apache.commons.compress.archivers.sevenz.SevenZFile
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream

class ArchiveRepository {
    suspend fun listEntries(archivePath: String): Result<List<ArchiveEntry>> = withContext(Dispatchers.IO) {
        runCatching {
            when (archivePath.substringAfterLast('.').lowercase()) {
                "zip" -> listZip(archivePath)
                "rar" -> listRar(archivePath)
                "7z" -> list7z(archivePath)
                else -> error("Unsupported archive format")
            }
        }
    }

    suspend fun extractAll(archivePath: String, destinationDir: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val dest = File(destinationDir)
            if (!dest.exists()) dest.mkdirs()
            when (archivePath.substringAfterLast('.').lowercase()) {
                "zip" -> extractZip(archivePath, dest)
                "rar" -> extractRar(archivePath, dest)
                "7z" -> extract7z(archivePath, dest)
                else -> error("Unsupported archive format")
            }
        }
    }

    private fun listZip(path: String): List<ArchiveEntry> {
        val entries = mutableListOf<ArchiveEntry>()
        ZipInputStream(BufferedInputStream(FileInputStream(path))).use { zis ->
            var entry: ZipEntry? = zis.nextEntry
            while (entry != null) {
                entries += ArchiveEntry(
                    name = entry.name,
                    path = entry.name,
                    isDirectory = entry.isDirectory,
                    size = entry.size.coerceAtLeast(0),
                    lastModified = entry.time,
                )
                entry = zis.nextEntry
            }
        }
        return entries
    }

    private fun extractZip(path: String, dest: File) {
        ZipInputStream(BufferedInputStream(FileInputStream(path))).use { zis ->
            var entry: ZipEntry? = zis.nextEntry
            while (entry != null) {
                val outFile = File(dest, entry.name)
                if (entry.isDirectory) {
                    outFile.mkdirs()
                } else {
                    outFile.parentFile?.mkdirs()
                    FileOutputStream(outFile).use { fos -> zis.copyTo(fos) }
                }
                entry = zis.nextEntry
            }
        }
    }

    private fun listRar(path: String): List<ArchiveEntry> =
        Archive(File(path)).use { archive ->
            archive.fileHeaders.map { header ->
                ArchiveEntry(
                    name = header.fileName,
                    path = header.fileName,
                    isDirectory = header.isDirectory,
                    size = header.fullUnpackSize,
                    lastModified = header.mTime?.time ?: 0L,
                )
            }
        }

    private fun extractRar(path: String, dest: File) {
        com.github.junrar.Junrar.extract(path, dest.absolutePath)
    }

    private fun list7z(path: String): List<ArchiveEntry> {
        val entries = mutableListOf<ArchiveEntry>()
        SevenZFile.builder().setFile(File(path)).get().use { sevenZ ->
            var entry: SevenZArchiveEntry? = sevenZ.nextEntry
            while (entry != null) {
                entries += ArchiveEntry(
                    name = entry.name,
                    path = entry.name,
                    isDirectory = entry.isDirectory,
                    size = entry.size,
                    lastModified = entry.lastModifiedDate?.time ?: 0L,
                )
                entry = sevenZ.nextEntry
            }
        }
        return entries
    }

    private fun extract7z(path: String, dest: File) {
        SevenZFile.builder().setFile(File(path)).get().use { sevenZ ->
            var entry: SevenZArchiveEntry? = sevenZ.nextEntry
            while (entry != null) {
                val outFile = File(dest, entry.name)
                if (entry.isDirectory) {
                    outFile.mkdirs()
                } else {
                    outFile.parentFile?.mkdirs()
                    FileOutputStream(outFile).use { fos -> sevenZ.getInputStream(entry).copyTo(fos) }
                }
                entry = sevenZ.nextEntry
            }
        }
    }
}
