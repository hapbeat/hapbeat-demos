package com.hapbeat.demoremote.desktop

import com.hapbeat.demoremote.data.KeyValueEditor
import com.hapbeat.demoremote.data.KeyValueStore
import java.io.File
import java.io.IOException
import java.nio.channels.FileChannel
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.nio.file.StandardOpenOption
import java.util.Properties

/** A properties file, replaced atomically on every write (temp file + ATOMIC_MOVE): a crash leaves the old or the new one. */
class PropertiesStore(private val file: File) : KeyValueStore {
    private val values = Properties()

    init {
        if (file.isFile) file.inputStream().use { values.load(it) }
    }

    @Synchronized override fun getString(key: String): String? = values.getProperty(key)
    override fun getInt(key: String, default: Int): Int = getString(key)?.toIntOrNull() ?: default
    override fun getLong(key: String, default: Long): Long = getString(key)?.toLongOrNull() ?: default
    override fun getBoolean(key: String, default: Boolean): Boolean = getString(key)?.toBooleanStrictOrNull() ?: default

    /** Every write is on disk when this returns, durable or not; the result is false when it could not be saved. */
    @Synchronized
    override fun write(durable: Boolean, block: KeyValueEditor.() -> Unit): Boolean {
        object : KeyValueEditor {
            override fun putString(key: String, value: String) { values.setProperty(key, value) }
            override fun putInt(key: String, value: Int) { values.setProperty(key, value.toString()) }
            override fun putLong(key: String, value: Long) { values.setProperty(key, value.toString()) }
            override fun putBoolean(key: String, value: Boolean) { values.setProperty(key, value.toString()) }
            override fun remove(key: String) { values.remove(key) }
        }.block()
        return try {
            save()
            true
        } catch (_: IOException) {
            false
        }
    }

    private fun save() {
        file.absoluteFile.parentFile?.mkdirs()
        val temp = File(file.path + ".tmp")
        temp.outputStream().use { values.store(it, null) }
        FileChannel.open(temp.toPath(), StandardOpenOption.WRITE).use { it.force(true) }
        Files.move(temp.toPath(), file.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
    }
}
