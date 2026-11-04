package com.hapbeat.demoremote.net

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import java.net.Inet4Address

/**
 * Pins this process to the Wi-Fi network. A demo LAN often has no internet, and Android may then
 * keep mobile data as the default network, which would send DISCOVER / adb out of the wrong
 * interface. Binding the process covers the UDP socket and dadb's own sockets alike.
 */
class WifiBinding(context: Context, private val onChanged: () -> Unit) {
    private val cm = context.getSystemService(ConnectivityManager::class.java)
    @Volatile var network: Network? = null
        private set

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            this@WifiBinding.network = network
            cm?.bindProcessToNetwork(network)
            onChanged() // sockets created before the binding keep their old network
        }

        override fun onLost(network: Network) {
            if (this@WifiBinding.network != network) return
            this@WifiBinding.network = null
            cm?.bindProcessToNetwork(null)
            onChanged()
        }
    }

    fun start() {
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        cm?.registerNetworkCallback(request, callback)
    }

    fun stop() {
        runCatching { cm?.unregisterNetworkCallback(callback) }
        network = null
        cm?.bindProcessToNetwork(null)
    }
}

/** The Wi-Fi network as [PlatformNetwork]. */
class AndroidNetwork(private val context: Context) : PlatformNetwork {
    private var binding: WifiBinding? = null

    override fun start(onChanged: () -> Unit) {
        binding = WifiBinding(context, onChanged).also { it.start() }
    }

    override fun stop() {
        binding?.stop()
    }

    override fun localIpv4(): LocalIpv4? {
        val network = binding?.network ?: return null
        val cm = context.getSystemService(ConnectivityManager::class.java) ?: return null
        val link = cm.getLinkProperties(network) ?: return null
        return link.linkAddresses.firstOrNull { it.address is Inet4Address }?.let { LocalIpv4(it.address, it.prefixLength) }
    }

    override fun gatewayIpv4(): String? {
        val network = binding?.network ?: return null
        val cm = context.getSystemService(ConnectivityManager::class.java) ?: return null
        val link = cm.getLinkProperties(network) ?: return null
        return link.routes.firstOrNull { it.isDefaultRoute && it.gateway is Inet4Address }?.gateway?.hostAddress
    }
}
