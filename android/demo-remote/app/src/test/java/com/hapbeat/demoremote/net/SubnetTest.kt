package com.hapbeat.demoremote.net

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubnetTest {
    private fun ip(vararg b: Int) = ByteArray(4) { b[it].toByte() }

    @Test
    fun sameSubnet() {
        assertTrue(DemoSwitchSocket.inSubnet("192.168.0.42", ip(192, 168, 0, 11), 24))
        assertTrue(DemoSwitchSocket.inSubnet("10.0.3.200", ip(10, 0, 2, 5), 22))
    }

    @Test
    fun otherSubnet() {
        assertFalse(DemoSwitchSocket.inSubnet("192.168.1.42", ip(192, 168, 0, 11), 24))
        assertFalse(DemoSwitchSocket.inSubnet("172.20.10.3", ip(192, 168, 0, 11), 24))
        assertFalse(DemoSwitchSocket.inSubnet("10.0.4.1", ip(10, 0, 2, 5), 22))
        assertFalse(DemoSwitchSocket.inSubnet("not-an-ip", ip(192, 168, 0, 11), 24))
    }
}
