package com.hapbeat.demoremote.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AdbPortScannerTest {
    private fun ip(vararg b: Int) = ByteArray(4) { b[it].toByte() }

    @Test
    fun slash24ExcludesNetworkBroadcastAndSelf() {
        val hosts = AdbPortScanner.subnetHosts(ip(192, 168, 0, 11), 24)
        assertEquals(253, hosts.size)
        assertEquals("192.168.0.1", hosts.first())
        assertEquals("192.168.0.254", hosts.last())
        assertFalse("192.168.0.11" in hosts)
        assertTrue("192.168.0.37" in hosts)
    }

    @Test
    fun largeSubnetsAreNotScanned() {
        assertTrue(AdbPortScanner.subnetHosts(ip(10, 0, 0, 5), 16).isEmpty())
        assertEquals(1021, AdbPortScanner.subnetHosts(ip(10, 0, 0, 5), 22).size)
    }
}
