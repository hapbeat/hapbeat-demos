package com.hapbeat.demoremote.adb

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.security.KeyPairGenerator
import java.security.Signature

class AdbProtocolTest {
    @Test
    fun checksumIsUnsignedByteSum() {
        assertEquals(0, AdbProtocol.checksum(ByteArray(0)))
        assertEquals(0x01 + 0xFF + 0x80, AdbProtocol.checksum(byteArrayOf(0x01, -1, -128)))
    }

    @Test
    fun encodesCnxnHeaderLittleEndian() {
        val payload = "host::\u0000".toByteArray(Charsets.UTF_8)
        val header = AdbProtocol.encodeHeader(AdbProtocol.A_CNXN, AdbProtocol.VERSION, AdbProtocol.MAX_PAYLOAD, payload)
        val expected = byteArrayOf(
            0x43, 0x4e, 0x58, 0x4e, // "CNXN"
            0x01, 0x00, 0x00, 0x01, // version 0x01000001
            0x00, 0x00, 0x04, 0x00, // 256 KiB
            0x07, 0x00, 0x00, 0x00, // data length
            0x32, 0x02, 0x00, 0x00, // byte sum of "host::\0" = 562
            0xbc.toByte(), 0xb1.toByte(), 0xa7.toByte(), 0xb1.toByte(), // CNXN xor 0xFFFFFFFF
        )
        assertArrayEquals(expected, header)
    }

    @Test
    fun decodeRoundTripsAndChecksMagic() {
        val payload = byteArrayOf(1, 2, 3)
        val bytes = AdbProtocol.encodeHeader(AdbProtocol.A_WRTE, 7, 9, payload)
        val header = AdbProtocol.decodeHeader(bytes)!!
        assertEquals(AdbProtocol.A_WRTE, header.command)
        assertEquals(7, header.arg0)
        assertEquals(9, header.arg1)
        assertEquals(3, header.dataLength)
        assertEquals(6, header.dataCrc)
        assertEquals(AdbProtocol.A_WRTE xor -1, header.magic)

        bytes[20] = (bytes[20] + 1).toByte()
        assertNull(AdbProtocol.decodeHeader(bytes))
        assertNull(AdbProtocol.decodeHeader(ByteArray(23)))
    }

    @Test
    fun signatureInputIsDigestInfoThenToken() {
        val token = ByteArray(20) { it.toByte() }
        val input = AdbProtocol.signatureInput(token)
        assertEquals(35, input.size)
        assertArrayEquals(
            byteArrayOf(0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2b, 0x0e, 0x03, 0x02, 0x1a, 0x05, 0x00, 0x04, 0x14),
            input.copyOfRange(0, 15),
        )
        assertArrayEquals(token, input.copyOfRange(15, 35))
    }

    @Test
    fun signatureVerifiesAsPkcs1Sha1() {
        // adbd verifies with RSA_verify(NID_sha1, token, ...): the same as SHA1withRSA over a pre-hashed token.
        val pair = KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.genKeyPair()
        val token = ByteArray(20) { (it * 7).toByte() }
        val signature = AdbProtocol.sign(pair.private, token)
        assertEquals(256, signature.size)
        val verifier = Signature.getInstance("NONEwithRSA").apply { initVerify(pair.public) }
        verifier.update(AdbProtocol.signatureInput(token))
        assertTrue(verifier.verify(signature))
    }

    @Test
    fun servicePayloadIsNulTerminated() {
        assertArrayEquals("tcpip:5555\u0000".toByteArray(Charsets.UTF_8), AdbProtocol.servicePayload("tcpip:5555"))
    }

    @Test
    fun parsesWlanIpv4() {
        val output = "30: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP group default qlen 3000\r\n" +
            "    inet 192.168.0.42/24 brd 192.168.0.255 scope global wlan0\r\n" +
            "       valid_lft forever preferred_lft forever\r\n"
        assertEquals("192.168.0.42", AdbProtocol.parseWlanIpv4(output))
        assertNull(AdbProtocol.parseWlanIpv4("Device \"wlan0\" does not exist.\n"))
    }
}
