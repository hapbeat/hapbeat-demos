package com.hapbeat.demoremote

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class DemoCatalogThumbnailTest {
    @Test
    fun everySessionDemoHasThumbnail() {
        DemoCatalog.sessionApps.forEach { assertNotNull(it.demoId, it.thumbnail) }
    }

    @Test
    fun thumbnailFilesAreBundled() {
        // Unit tests run with the module directory (app/) as the working directory.
        DemoCatalog.sessionApps.forEach { app ->
            val file = File("src/main/assets", app.thumbnail!!)
            assertTrue(file.path, file.isFile)
        }
    }
}
