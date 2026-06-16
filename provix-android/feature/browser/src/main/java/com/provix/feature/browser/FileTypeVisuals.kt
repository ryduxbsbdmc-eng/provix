package com.provix.feature.browser

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.InsertDriveFile
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Audiotrack
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.GridOn
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.Movie
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Slideshow
import androidx.compose.material.icons.filled.Terminal
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import com.provix.core.model.FileEntry

/** Icon + accent tint for a file, chosen by extension. Colors read on dark and light. */
data class FileVisual(val icon: ImageVector, val tint: Color)

private val IMAGE = setOf("png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "heic", "ico", "tiff")
private val VIDEO = setOf("mp4", "mkv", "mov", "avi", "webm", "flv", "m4v", "3gp")
private val AUDIO = setOf("mp3", "wav", "flac", "aac", "ogg", "m4a", "opus", "wma")
private val ARCHIVE = setOf("zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso")
private val CODE = setOf(
    "kt", "java", "js", "ts", "tsx", "jsx", "py", "c", "cpp", "h", "cs", "go",
    "rs", "rb", "php", "swift", "json", "xml", "yml", "yaml", "html", "css", "sql", "gradle",
)
private val SCRIPT = setOf("sh", "bat", "ps1", "cmd", "zsh", "bash")
private val DOC = setOf("doc", "docx", "txt", "md", "rtf", "odt", "epub")
private val SHEET = setOf("xls", "xlsx", "csv", "ods")
private val SLIDES = setOf("ppt", "pptx", "odp", "key")

fun FileEntry.visual(folderTint: Color, accent: Color): FileVisual {
    if (isDirectory) return FileVisual(Icons.Filled.Folder, folderTint)
    return when (extension.lowercase()) {
        in IMAGE -> FileVisual(Icons.Filled.Image, Color(0xFF4FC1A6))
        in VIDEO -> FileVisual(Icons.Filled.Movie, Color(0xFFE06C9F))
        in AUDIO -> FileVisual(Icons.Filled.Audiotrack, Color(0xFF9B7BE8))
        in ARCHIVE -> FileVisual(Icons.Filled.Archive, Color(0xFFD9A441))
        "pdf" -> FileVisual(Icons.Filled.PictureAsPdf, Color(0xFFE5564E))
        in SHEET -> FileVisual(Icons.Filled.GridOn, Color(0xFF52A765))
        in SLIDES -> FileVisual(Icons.Filled.Slideshow, Color(0xFFE08A3C))
        in SCRIPT -> FileVisual(Icons.Filled.Terminal, Color(0xFF7FB2E5))
        in CODE -> FileVisual(Icons.Filled.Code, Color(0xFF5B9DE0))
        in DOC -> FileVisual(Icons.Filled.Description, Color(0xFF6FA8DC))
        else -> FileVisual(Icons.AutoMirrored.Filled.InsertDriveFile, accent)
    }
}
