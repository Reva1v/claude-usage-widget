#!/usr/bin/env swift
// make-icon.swift — generates Resources/AppIcon.icns from CoreGraphics primitives.
// Run from repo root: swift Scripts/make-icon.swift

import AppKit
import CoreGraphics
import Foundation

// MARK: - Color helpers

func cgColor(hex: UInt32) -> CGColor {
    let r = CGFloat((hex >> 16) & 0xFF) / 255.0
    let g = CGFloat((hex >> 8)  & 0xFF) / 255.0
    let b = CGFloat(hex         & 0xFF) / 255.0
    return CGColor(red: r, green: g, blue: b, alpha: 1.0)
}

// MARK: - Master image (1024 × 1024)

let masterSize = 1024

guard let ctx = CGContext(
    data: nil,
    width: masterSize,
    height: masterSize,
    bitsPerComponent: 8,
    bytesPerRow: 0,
    space: CGColorSpaceCreateDeviceRGB(),
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
) else {
    fputs("ERROR: could not create CGContext\n", stderr)
    exit(1)
}

// Transparent background (already zeroed — nothing to clear explicitly)

// Background rounded rect: inset 100 pt, corner radius 180 — Theme.panel.
let bgRect = CGRect(x: 100, y: 100, width: 824, height: 824)
let bgPath = CGPath(roundedRect: bgRect, cornerWidth: 180, cornerHeight: 180, transform: nil)
ctx.setFillColor(cgColor(hex: 0x1E2230))
ctx.addPath(bgPath)
ctx.fillPath()

// Ring — echoes the widget's usage dials: a track circle (Theme.track) with
// an accent-colored arc (Theme.accent) laid over most of it, like a dial
// filled well below its limit.
let ringCenter = CGPoint(x: CGFloat(masterSize) / 2, y: CGFloat(masterSize) / 2)
let ringRadius: CGFloat = 280
let ringLineWidth: CGFloat = 88

ctx.setLineWidth(ringLineWidth)
ctx.setLineCap(.round)

ctx.setStrokeColor(cgColor(hex: 0x404557))
ctx.addArc(center: ringCenter, radius: ringRadius, startAngle: 0, endAngle: 2 * .pi, clockwise: false)
ctx.strokePath()

// Arc drawn clockwise from the top (12 o'clock), covering ~70% of the ring —
// CoreGraphics angles run counter-clockwise from the 3 o'clock position, so
// the top is -.pi / 2 and "clockwise" in screen terms is `clockwise: true`.
ctx.setStrokeColor(cgColor(hex: 0xA6D189))
ctx.addArc(
    center: ringCenter,
    radius: ringRadius,
    startAngle: -.pi / 2,
    endAngle: -.pi / 2 + 2 * .pi * 0.7,
    clockwise: false
)
ctx.strokePath()

guard let masterImage = ctx.makeImage() else {
    fputs("ERROR: could not create master CGImage\n", stderr)
    exit(1)
}

// MARK: - Iconset sizes

struct IconFile {
    let filename: String
    let pixels: Int
}

let iconFiles: [IconFile] = [
    IconFile(filename: "icon_16x16.png",      pixels: 16),
    IconFile(filename: "icon_16x16@2x.png",   pixels: 32),
    IconFile(filename: "icon_32x32.png",      pixels: 32),
    IconFile(filename: "icon_32x32@2x.png",   pixels: 64),
    IconFile(filename: "icon_128x128.png",    pixels: 128),
    IconFile(filename: "icon_128x128@2x.png", pixels: 256),
    IconFile(filename: "icon_256x256.png",    pixels: 256),
    IconFile(filename: "icon_256x256@2x.png", pixels: 512),
    IconFile(filename: "icon_512x512.png",    pixels: 512),
    IconFile(filename: "icon_512x512@2x.png", pixels: 1024),
]

// MARK: - Temp iconset directory

let fm = FileManager.default
let tempDir = fm.temporaryDirectory.appendingPathComponent("AppIcon.iconset")
try fm.createDirectory(at: tempDir, withIntermediateDirectories: true)

for icon in iconFiles {
    guard let scaledCtx = CGContext(
        data: nil,
        width: icon.pixels,
        height: icon.pixels,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        fputs("ERROR: could not create context for \(icon.filename)\n", stderr)
        exit(1)
    }

    scaledCtx.interpolationQuality = .high
    scaledCtx.draw(masterImage, in: CGRect(x: 0, y: 0, width: icon.pixels, height: icon.pixels))

    guard let scaledImage = scaledCtx.makeImage() else {
        fputs("ERROR: could not create image for \(icon.filename)\n", stderr)
        exit(1)
    }

    let destURL = tempDir.appendingPathComponent(icon.filename)
    let nsImage = NSBitmapImageRep(cgImage: scaledImage)
    guard let pngData = nsImage.representation(using: .png, properties: [:]) else {
        fputs("ERROR: PNG encoding failed for \(icon.filename)\n", stderr)
        exit(1)
    }
    try pngData.write(to: destURL)
}

// MARK: - Run iconutil

let outputPath = "Resources/AppIcon.icns"

let process = Process()
process.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
process.arguments = ["-c", "icns", tempDir.path, "-o", outputPath]
try process.run()
process.waitUntilExit()

guard process.terminationStatus == 0 else {
    fputs("ERROR: iconutil exited with status \(process.terminationStatus)\n", stderr)
    exit(1)
}

// Clean up temp iconset
try fm.removeItem(at: tempDir)

print("Icon written to: \(outputPath)")
