import Darwin
import ExpoModulesCore
import Foundation

enum WakeOnLanError: LocalizedError {
  case invalidMacAddress(String)
  case socketCreationFailed(Int32)
  case broadcastOptionFailed(Int32)
  case sendFailed(Int32)

  var errorDescription: String? {
    switch self {
    case .invalidMacAddress(let mac):
      return "Invalid MAC address: \(mac)"
    case .socketCreationFailed(let code):
      return "Failed to create UDP socket (errno \(code))"
    case .broadcastOptionFailed(let code):
      return "Failed to enable UDP broadcast (errno \(code))"
    case .sendFailed(let code):
      return "Failed to send Wake-on-LAN packet (errno \(code))"
    }
  }
}

public class WakeOnLanModule: Module {
  public func definition() -> ModuleDefinition {
    Name("WakeOnLan")

    AsyncFunction("sendMagicPacket") { (macAddress: String, broadcastIp: String) in
      let packet = try Self.buildMagicPacket(macAddress: macAddress)
      var targets: [String] = []
      let trimmed = broadcastIp.trimmingCharacters(in: .whitespacesAndNewlines)
      if !trimmed.isEmpty {
        targets.append(trimmed)
      }
      if !targets.contains("255.255.255.255") {
        targets.append("255.255.255.255")
      }
      try Self.sendUdpBroadcast(packet: packet, targets: targets, port: 9)
    }
  }

  private static func buildMagicPacket(macAddress: String) throws -> [UInt8] {
    let hexChars = macAddress
      .replacingOccurrences(of: ":", with: "")
      .replacingOccurrences(of: "-", with: "")
      .replacingOccurrences(of: ".", with: "")
      .trimmingCharacters(in: .whitespacesAndNewlines)

    guard hexChars.count == 12 else {
      throw WakeOnLanError.invalidMacAddress(macAddress)
    }

    var macBytes: [UInt8] = []
    macBytes.reserveCapacity(6)
    var index = hexChars.startIndex
    for _ in 0..<6 {
      let nextIndex = hexChars.index(index, offsetBy: 2)
      let byteStr = String(hexChars[index..<nextIndex])
      guard let byte = UInt8(byteStr, radix: 16) else {
        throw WakeOnLanError.invalidMacAddress(macAddress)
      }
      macBytes.append(byte)
      index = nextIndex
    }

    var packet = [UInt8](repeating: 0xFF, count: 6)
    packet.reserveCapacity(102)
    for _ in 0..<16 {
      packet.append(contentsOf: macBytes)
    }
    return packet
  }

  private static func sendUdpBroadcast(packet: [UInt8], targets: [String], port: UInt16) throws {
    let sock = Darwin.socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
    guard sock >= 0 else {
      throw WakeOnLanError.socketCreationFailed(errno)
    }
    defer { Darwin.close(sock) }

    var broadcastEnable: Int32 = 1
    let optResult = withUnsafePointer(to: &broadcastEnable) { ptr in
      setsockopt(sock, SOL_SOCKET, SO_BROADCAST, ptr, socklen_t(MemoryLayout<Int32>.size))
    }
    guard optResult >= 0 else {
      throw WakeOnLanError.broadcastOptionFailed(errno)
    }

    var lastErrno: Int32 = 0
    var sentAtLeastOnce = false

    for attempt in 0..<3 {
      for target in targets {
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = port.bigEndian

        let ptonResult = target.withCString { cstr in
          inet_pton(AF_INET, cstr, &addr.sin_addr)
        }
        guard ptonResult == 1 else { continue }

        let sent = packet.withUnsafeBytes { rawBuf -> Int in
          guard let baseAddress = rawBuf.baseAddress else { return -1 }
          return withUnsafePointer(to: &addr) { addrPtr in
            addrPtr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockAddrPtr in
              sendto(
                sock,
                baseAddress,
                rawBuf.count,
                0,
                sockAddrPtr,
                socklen_t(MemoryLayout<sockaddr_in>.size)
              )
            }
          }
        }
        if sent >= 0 {
          sentAtLeastOnce = true
        } else {
          lastErrno = errno
        }
      }
      if attempt < 2 {
        Thread.sleep(forTimeInterval: 0.05)
      }
    }

    if !sentAtLeastOnce {
      throw WakeOnLanError.sendFailed(lastErrno)
    }
  }
}
