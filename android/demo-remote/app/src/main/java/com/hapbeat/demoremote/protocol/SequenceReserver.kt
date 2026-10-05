package com.hapbeat.demoremote.protocol

/** Persistent storage of the controller's next sequence number. */
interface SequenceStore {
    fun readNext(): Long

    /** Must persist synchronously; returns false if the value could not be written. */
    fun writeNextSync(next: Long): Boolean
}

sealed interface SequenceReservation {
    data class Reserved(val seq: Long) : SequenceReservation
    data object Exhausted : SequenceReservation
    data object PersistFailed : SequenceReservation
}

/**
 * Reserves a sequence number before sending (same as the M5 controller): the incremented
 * value is committed first, so a crash after sending can never reuse a sequence.
 */
class SequenceReserver(private val store: SequenceStore) {
    @Synchronized
    fun reserve(): SequenceReservation {
        val seq = store.readNext().coerceAtLeast(1)
        if (seq > DemoSwitchProtocol.MAX_SEQ) return SequenceReservation.Exhausted
        if (!store.writeNextSync(seq + 1)) return SequenceReservation.PersistFailed
        return SequenceReservation.Reserved(seq)
    }
}
