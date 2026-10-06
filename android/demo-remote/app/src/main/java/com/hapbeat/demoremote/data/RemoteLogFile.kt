package com.hapbeat.demoremote.data

import java.io.File
import java.io.IOException
import java.util.concurrent.Executors

/**
 * The result log kept on disk (filesDir/remote-log.txt) so a failure at a venue can be read afterwards.
 * Writes run in order on one background thread; the newest [MAX_LINES] lines are kept.
 */
class RemoteLogFile(private val file: File) {
    private val writer = Executors.newSingleThreadExecutor()

    fun append(line: String) {
        writer.execute {
            try {
                val lines = if (file.isFile) file.readLines() else emptyList()
                file.writeText(keepLast(lines + line.replace('\n', ' '), MAX_LINES).joinToString("\n", postfix = "\n"))
            } catch (_: IOException) {
                // A diagnostic log that cannot be written must not disturb the remote.
            }
        }
    }

    /** Whole file (waits for queued writes); "" when empty or unreadable. */
    fun read(): String = writer.submit<String> {
        try {
            if (file.isFile) file.readText() else ""
        } catch (_: IOException) {
            ""
        }
    }.get()

    fun clear() {
        writer.execute { file.delete() }
    }

    companion object {
        const val FILE_NAME = "remote-log.txt"
        const val MAX_LINES = 500

        /** The last [max] of [lines] (older ones are dropped). */
        fun keepLast(lines: List<String>, max: Int): List<String> = if (lines.size <= max) lines else lines.subList(lines.size - max, lines.size)
    }
}
