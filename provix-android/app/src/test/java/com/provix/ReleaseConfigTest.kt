package com.provix

import org.junit.Test

class ReleaseConfigTest {
    @Test
    fun versionMatchesWindows() {
        assert(BuildConfig.VERSION_NAME == "1.3.5")
        assert(BuildConfig.VERSION_CODE == 135)
    }
}
