#!/usr/bin/env swift

import AppKit
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

enum ScreenshotError: Error, CustomStringConvertible {
    case usage(String)
    case load(String)
    case crop(String)
    case write(String)

    var description: String {
        switch self {
        case .usage(let message), .load(let message), .crop(let message), .write(let message):
            return message
        }
    }
}

func loadImage(_ path: String) throws -> CGImage {
    guard let image = NSImage(contentsOfFile: path),
          let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
        throw ScreenshotError.load("Could not load image: \(path)")
    }
    return cgImage
}

func writePNG(_ image: CGImage, to path: String) throws {
    let url = URL(fileURLWithPath: path) as CFURL
    guard let destination = CGImageDestinationCreateWithURL(
        url,
        UTType.png.identifier as CFString,
        1,
        nil
    ) else {
        throw ScreenshotError.write("Could not create PNG destination: \(path)")
    }

    CGImageDestinationAddImage(destination, image, [
        kCGImagePropertyPNGDictionary: [kCGImagePropertyPNGsRGBIntent: 0]
    ] as CFDictionary)
    guard CGImageDestinationFinalize(destination) else {
        throw ScreenshotError.write("Could not write PNG: \(path)")
    }
}

func parseInteger(_ value: String, name: String) throws -> Int {
    guard let result = Int(value) else {
        throw ScreenshotError.usage("Invalid \(name): \(value)")
    }
    return result
}

func cropTopLeft(_ image: CGImage, x: Int, y: Int, width: Int, height: Int) throws -> CGImage {
    guard x >= 0, y >= 0, width > 0, height > 0,
          x + width <= image.width, y + height <= image.height else {
        throw ScreenshotError.crop(
            "Crop \(x),\(y),\(width),\(height) is outside \(image.width)×\(image.height)"
        )
    }

    let rect = CGRect(x: x, y: y, width: width, height: height)
    guard let cropped = image.cropping(to: rect) else {
        throw ScreenshotError.crop("Could not crop image")
    }
    return cropped
}

func blackTrimBounds(_ image: CGImage, threshold: UInt8, padding: Int) throws -> CGRect {
    let width = image.width
    let height = image.height
    let bytesPerRow = width * 4
    var pixels = [UInt8](repeating: 0, count: bytesPerRow * height)
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let context = CGContext(
        data: &pixels,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: bytesPerRow,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        throw ScreenshotError.crop("Could not create trim context")
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    var minX = width
    var minY = height
    var maxX = -1
    var maxY = -1

    for y in 0..<height {
        let row = y * bytesPerRow
        for x in 0..<width {
            let offset = row + x * 4
            let visible = pixels[offset] > threshold ||
                pixels[offset + 1] > threshold ||
                pixels[offset + 2] > threshold
            if visible {
                minX = min(minX, x)
                minY = min(minY, y)
                maxX = max(maxX, x)
                maxY = max(maxY, y)
            }
        }
    }

    guard maxX >= minX, maxY >= minY else {
        throw ScreenshotError.crop("Image contains no pixels above the black threshold")
    }

    minX = max(0, minX - padding)
    minY = max(0, minY - padding)
    maxX = min(width - 1, maxX + padding)
    maxY = min(height - 1, maxY + padding)
    return CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)
}

func trimBlack(_ image: CGImage, threshold: UInt8, padding: Int) throws -> CGImage {
    let bounds = try blackTrimBounds(image, threshold: threshold, padding: padding)
    guard let cropped = image.cropping(to: bounds) else {
        throw ScreenshotError.crop("Could not apply black trim")
    }
    return cropped
}

func compose(_ images: [CGImage], horizontal: Bool, gap: Int) throws -> CGImage {
    guard !images.isEmpty else {
        throw ScreenshotError.usage("Composition needs at least one input image")
    }

    let width = horizontal
        ? images.reduce(0) { $0 + $1.width } + gap * (images.count - 1)
        : images.map(\.width).max()!
    let height = horizontal
        ? images.map(\.height).max()!
        : images.reduce(0) { $0 + $1.height } + gap * (images.count - 1)
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let context = CGContext(
        data: nil,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        throw ScreenshotError.write("Could not create composition context")
    }

    context.setFillColor(NSColor.white.cgColor)
    context.fill(CGRect(x: 0, y: 0, width: width, height: height))

    if horizontal {
        var x = 0
        for image in images {
            let y = (height - image.height) / 2
            context.draw(image, in: CGRect(x: x, y: y, width: image.width, height: image.height))
            x += image.width + gap
        }
    } else {
        var top = height
        for image in images {
            top -= image.height
            let x = (width - image.width) / 2
            context.draw(image, in: CGRect(x: x, y: top, width: image.width, height: image.height))
            top -= gap
        }
    }

    guard let result = context.makeImage() else {
        throw ScreenshotError.write("Could not finalize composition")
    }
    return result
}

func run() throws {
    let arguments = Array(CommandLine.arguments.dropFirst())
    guard let command = arguments.first else {
        throw ScreenshotError.usage(
            "Usage: process_screenshots.swift crop|trim-black|hstack|vstack ..."
        )
    }

    switch command {
    case "crop":
        guard arguments.count == 7 else {
            throw ScreenshotError.usage("Usage: crop INPUT OUTPUT X Y WIDTH HEIGHT")
        }
        let input = try loadImage(arguments[1])
        let output = try cropTopLeft(
            input,
            x: parseInteger(arguments[3], name: "x"),
            y: parseInteger(arguments[4], name: "y"),
            width: parseInteger(arguments[5], name: "width"),
            height: parseInteger(arguments[6], name: "height")
        )
        try writePNG(output, to: arguments[2])

    case "trim-black":
        guard arguments.count == 3 || arguments.count == 5 else {
            throw ScreenshotError.usage("Usage: trim-black INPUT OUTPUT [THRESHOLD PADDING]")
        }
        let threshold = arguments.count == 5
            ? UInt8(clamping: try parseInteger(arguments[3], name: "threshold"))
            : 8
        let padding = arguments.count == 5
            ? try parseInteger(arguments[4], name: "padding")
            : 0
        let output = try trimBlack(try loadImage(arguments[1]), threshold: threshold, padding: padding)
        try writePNG(output, to: arguments[2])

    case "hstack", "vstack":
        guard arguments.count >= 5 else {
            throw ScreenshotError.usage("Usage: \(command) OUTPUT GAP INPUT INPUT [...]")
        }
        let gap = try parseInteger(arguments[2], name: "gap")
        let images = try arguments.dropFirst(3).map(loadImage)
        let output = try compose(images, horizontal: command == "hstack", gap: gap)
        try writePNG(output, to: arguments[1])

    default:
        throw ScreenshotError.usage("Unknown command: \(command)")
    }
}

do {
    try run()
} catch {
    FileHandle.standardError.write(Data("\(error)\n".utf8))
    exit(1)
}
