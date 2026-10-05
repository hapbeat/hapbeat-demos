package com.hapbeat.demoremote.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class SequenceReserverTest {
    private class FakeStore(var next: Long, var writable: Boolean = true) : SequenceStore {
        val writes = mutableListOf<Long>()
        override fun readNext(): Long = next
        override fun writeNextSync(next: Long): Boolean {
            if (!writable) return false
            writes += next
            this.next = next
            return true
        }
    }

    @Test
    fun reservesAndPersistsBeforeReturning() {
        val store = FakeStore(1)
        val reserver = SequenceReserver(store)
        assertEquals(SequenceReservation.Reserved(1), reserver.reserve())
        assertEquals(SequenceReservation.Reserved(2), reserver.reserve())
        assertEquals(listOf(2L, 3L), store.writes)
    }

    @Test
    fun persistFailureDoesNotHandOutSequence() {
        val store = FakeStore(5, writable = false)
        assertEquals(SequenceReservation.PersistFailed, SequenceReserver(store).reserve())
        assertEquals(5L, store.next)
    }

    @Test
    fun maximumSequenceThenExhausted() {
        val store = FakeStore(DemoSwitchProtocol.MAX_SEQ)
        val reserver = SequenceReserver(store)
        assertEquals(SequenceReservation.Reserved(DemoSwitchProtocol.MAX_SEQ), reserver.reserve())
        assertEquals(SequenceReservation.Exhausted, reserver.reserve())
    }
}
