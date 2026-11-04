package com.hapbeat.demoremote.net

import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.util.concurrent.TimeUnit

/** One IPv4 address of a network interface, as far as choosing the LAN needs to know. */
data class InterfaceCandidate(
    val name: String,
    val displayName: String,
    val address: String,
    val prefixLength: Int,
    val up: Boolean = true,
    val loopback: Boolean = false,
    val virtual: Boolean = false,
    val siteLocal: Boolean = true,
)

/** A default route of `route print -4 0.0.0.0`; [gateway] null when on-link. */
data class DefaultRoute(val gateway: String?, val interfaceAddress: String)

object InterfacePick {
    private val VIRTUAL_NAME = Regex("vEthernet|WSL|VPN|Virtual|VMware|VirtualBox|Hyper-V|Tailscale|ZeroTier|WireGuard|TAP-|Bluetooth|Loopback", RegexOption.IGNORE_CASE)

    /** The LAN: the site-local address of a real adapter, the one with [defaultRouteAddress] first. Null when there is none. */
    fun pick(candidates: List<InterfaceCandidate>, defaultRouteAddress: String?): InterfaceCandidate? {
        val usable = candidates.filter {
            it.up && !it.loopback && !it.virtual && it.siteLocal && !VIRTUAL_NAME.containsMatchIn(it.displayName) && !VIRTUAL_NAME.containsMatchIn(it.name)
        }
        return usable.firstOrNull { it.address == defaultRouteAddress } ?: usable.firstOrNull()
    }

    /** Lines `0.0.0.0 0.0.0.0 <gateway|On-link> <interface> <metric>` of `route print -4 0.0.0.0`, lowest metric first. */
    fun parseDefaultRoutes(output: String): List<DefaultRoute> =
        Regex("""^\s*0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+)\s*$""", RegexOption.MULTILINE).findAll(output)
            .map { it.groupValues }
            .sortedBy { it[3].toInt() }
            .map { DefaultRoute(it[1].takeIf { gateway -> gateway.first().isDigit() }, it[2]) }
            .toList()
}

/** The LAN found through `NetworkInterface` and `route print`, read again every [POLL_MS] on a daemon thread. */
class DesktopNetwork : PlatformNetwork {
    private data class Found(val local: LocalIpv4, val gateway: String?)

    @Volatile private var found: Found? = read()
    @Volatile private var thread: Thread? = null

    override fun start(onChanged: () -> Unit) {
        thread = Thread({
            try {
                while (true) {
                    Thread.sleep(POLL_MS)
                    val now = read()
                    val before = found
                    found = now
                    if (now != before) onChanged()
                }
            } catch (_: InterruptedException) {
                // stop()
            }
        }, "network-watch").apply { isDaemon = true; start() }
    }

    override fun stop() {
        thread?.interrupt()
        thread = null
    }

    override fun localIpv4(): LocalIpv4? = found?.local
    override fun gatewayIpv4(): String? = found?.gateway

    private fun read(): Found? {
        val interfaces = runCatching { NetworkInterface.getNetworkInterfaces().toList() }.getOrDefault(emptyList())
        val candidates = interfaces.flatMap { nif ->
            val up = runCatching { nif.isUp }.getOrDefault(false)
            val loopback = runCatching { nif.isLoopback }.getOrDefault(false)
            nif.interfaceAddresses.mapNotNull { a ->
                val inet = a.address as? Inet4Address ?: return@mapNotNull null
                InterfaceCandidate(nif.name, nif.displayName ?: "", inet.hostAddress, a.networkPrefixLength.toInt(), up, loopback, nif.isVirtual, inet.isSiteLocalAddress)
            }
        }
        val routes = InterfacePick.parseDefaultRoutes(routePrint())
        val route = routes.firstOrNull { InterfacePick.pick(candidates, it.interfaceAddress)?.address == it.interfaceAddress }
        val chosen = InterfacePick.pick(candidates, route?.interfaceAddress) ?: return null
        return Found(LocalIpv4(InetAddress.getByName(chosen.address), chosen.prefixLength), route?.gateway)
    }

    private fun routePrint(): String = runCatching {
        val process = ProcessBuilder("route", "print", "-4", "0.0.0.0").redirectErrorStream(true).start()
        val output = process.inputStream.bufferedReader().readText()
        process.waitFor(5, TimeUnit.SECONDS)
        output
    }.getOrDefault("")

    private companion object {
        const val POLL_MS = 5_000L
    }
}
