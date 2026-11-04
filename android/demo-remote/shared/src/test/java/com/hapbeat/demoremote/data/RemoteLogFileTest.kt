package com.hapbeat.demoremote.data

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File
import java.nio.file.Files

class RemoteLogFileTest {
    @Test
    fun keepsTheNewestLines() {
        val kept = RemoteLogFile.keepLast((1..520).map { "line $it" }, RemoteLogFile.MAX_LINES)
        assertEquals(500, kept.size)
        assertEquals("line 21", kept.first())
        assertEquals("line 520", kept.last())
        assertEquals(listOf("a"), RemoteLogFile.keepLast(listOf("a"), RemoteLogFile.MAX_LINES))
    }

    @Test
    fun appendTrimsAndClears() {
        val dir = Files.createTempDirectory("remote-log").toFile()
        try {
            val log = RemoteLogFile(File(dir, RemoteLogFile.FILE_NAME))
            repeat(505) { log.append("entry $it") }
            val lines = log.read().trimEnd('\n').split('\n')
            assertEquals(500, lines.size)
            assertEquals("entry 5", lines.first())
            assertEquals("entry 504", lines.last())
            log.clear()
            assertEquals("", log.read())
        } finally {
            dir.deleteRecursively()
        }
    }
}
