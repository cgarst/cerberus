package expo.modules.wakeonlan

import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class WakeOnLanModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("WakeOnLan")

    AsyncFunction("sendMagicPacket") { macAddress: String, broadcastIp: String ->
      val packetBytes = buildMagicPacket(macAddress)
      val targets = mutableListOf<String>()
      val trimmed = broadcastIp.trim()
      if (trimmed.isNotEmpty()) {
        targets.add(trimmed)
      }
      if (!targets.contains("255.255.255.255")) {
        targets.add("255.255.255.255")
      }

      var lastException: Exception? = null
      var sentAtLeastOnce = false

      DatagramSocket().use { socket ->
        socket.broadcast = true
        for (attempt in 0 until 3) {
          for (target in targets) {
            try {
              val address = InetAddress.getByName(target)
              val packet = DatagramPacket(packetBytes, packetBytes.size, address, 9)
              socket.send(packet)
              sentAtLeastOnce = true
            } catch (ex: Exception) {
              lastException = ex
            }
          }
          if (attempt < 2) {
            Thread.sleep(50)
          }
        }
      }

      if (!sentAtLeastOnce) {
        throw lastException ?: IllegalStateException("Failed to send Wake-on-LAN packet")
      }
    }
  }

  private fun buildMagicPacket(macAddress: String): ByteArray {
    val hexChars = macAddress
      .replace(":", "")
      .replace("-", "")
      .replace(".", "")
      .trim()

    require(hexChars.length == 12) { "Invalid MAC address: $macAddress" }

    val macBytes = ByteArray(6)
    for (i in 0 until 6) {
      val byteStr = hexChars.substring(i * 2, i * 2 + 2)
      macBytes[i] = byteStr.toInt(16).toByte()
    }

    val packet = ByteArray(102)
    for (i in 0 until 6) {
      packet[i] = 0xFF.toByte()
    }
    for (i in 0 until 16) {
      System.arraycopy(macBytes, 0, packet, 6 + i * 6, 6)
    }
    return packet
  }
}
