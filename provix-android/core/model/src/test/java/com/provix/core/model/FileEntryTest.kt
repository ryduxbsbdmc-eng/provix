package com.provix.core.model

import org.junit.Assert.assertEquals
import org.junit.Test

class FileEntryTest {
    @Test
    fun extensionParsedForFiles() {
        val entry = FileEntry("readme.md", "/tmp/readme.md", false, 10, 0)
        assertEquals("md", entry.extension)
    }
}
