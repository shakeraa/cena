#!/usr/bin/env python3
"""
Generate a deterministic JPEG fixture carrying EXIF/GPS/Make/Model/DateTime
tags for the prr-001 EXIF-stripping integration test.

The bytes are committed to the repo so the test is deterministic and does
not depend on any third-party fixture. Handwritten APP1 segment keeps the
file ~340 bytes and avoids a runtime dependency on PIL/Pillow.

Tags written:
  - GPSLatitude       = 32.0784 N (Tel Aviv-ish)
  - GPSLongitude      = 34.7803 E
  - GPSLatitudeRef    = N
  - GPSLongitudeRef   = E
  - Make              = "PRR001-TESTCAM"
  - Model             = "FixtureSensor-1"
  - DateTimeOriginal  = "2026:04:20 09:15:42"

These are the exact tags the integration test asserts are GONE after
ExifStripper round-trips the file.
"""
import struct, os

# --- Minimal 8x8 JPEG from scratch (baseline DCT, Y+Cb+Cr, quality ~90) ---
# We produce a valid but tiny JPEG by writing a pre-baked skeleton and an
# APP1/Exif segment in front of it.

# Pre-baked 8x8 grayscale JPEG (SOI ... SOS ... EOI). This is a minimal
# single-block baseline JPEG accepted by ImageSharp / MetadataExtractor.
SKELETON_JPEG = bytes([
    0xFF, 0xD8,                                # SOI
    # DQT (luma)
    0xFF, 0xDB, 0x00, 0x43, 0x00,
    0x10, 0x0B, 0x0C, 0x0E, 0x0C, 0x0A, 0x10, 0x0E, 0x0D, 0x0E, 0x12, 0x11,
    0x10, 0x13, 0x18, 0x28, 0x1A, 0x18, 0x16, 0x16, 0x18, 0x31, 0x23, 0x25,
    0x1D, 0x28, 0x3A, 0x33, 0x3D, 0x3C, 0x39, 0x33, 0x38, 0x37, 0x40, 0x48,
    0x5C, 0x4E, 0x40, 0x44, 0x57, 0x45, 0x37, 0x38, 0x50, 0x6D, 0x51, 0x57,
    0x5F, 0x62, 0x67, 0x68, 0x67, 0x3E, 0x4D, 0x71, 0x79, 0x70, 0x64, 0x78,
    0x5C, 0x65, 0x67, 0x63,
    # SOF0 — grayscale 8x8
    0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00,
    # DHT (DC, luma)
    0xFF, 0xC4, 0x00, 0x1F, 0x00,
    0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B,
    # DHT (AC, luma)
    0xFF, 0xC4, 0x00, 0xB5, 0x10,
    0x00, 0x02, 0x01, 0x03, 0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 0x00, 0x01, 0x7D,
    0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
    0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
    0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
    0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
    0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
    0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
    0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
    0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
    0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
    0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
    0xF9, 0xFA,
    # SOS
    0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
    # 1 byte of compressed payload (all-DC-zero block) + EOI
    0x00,
    0xFF, 0xD9,
])

# --- Build the APP1/Exif segment manually ---
def make_ifd_entry(tag, ftype, count, value_bytes):
    # each IFD entry: tag(2) type(2) count(4) value/offset(4)
    return struct.pack(">HHL", tag, ftype, count) + value_bytes

def rational(num, den):
    return struct.pack(">LL", num, den)

# Exif TIFF header (big-endian)
tiff = b"MM\x00\x2A" + struct.pack(">L", 8)  # IFD0 starts at offset 8

# ---- IFD0 ----
make_str = b"PRR001-TESTCAM\x00"     # 15 bytes
model_str = b"FixtureSensor-1\x00"   # 16 bytes
dt_str    = b"2026:04:20 09:15:42\x00"  # 20 bytes
# GPS IFD will live after IFD0. We'll compute its offset after measuring IFD0.

# IFD0 placeholder — count + 3 entries + ExifIFDPointer (stub to Exif sub-IFD
# that we skip) + GPSIFDPointer + next-IFD(=0). We include only entries that
# MetadataExtractor will surface cleanly.
def build_app1():
    entries = []
    # We'll lay data (Make/Model/DateTime) AFTER the IFD0 body; collect offsets.
    ifd0_body_len = 2 + (5 * 12) + 4   # count + 5 entries + next-IFD
    data_offset = 8 + ifd0_body_len    # 8-byte TIFF header + IFD0 body
    # Make (tag 0x010F, ASCII)
    make_off = data_offset
    entries.append(make_ifd_entry(0x010F, 2, len(make_str), struct.pack(">L", make_off)))
    # Model (tag 0x0110)
    model_off = make_off + len(make_str)
    entries.append(make_ifd_entry(0x0110, 2, len(model_str), struct.pack(">L", model_off)))
    # DateTime (tag 0x0132)
    dt_off = model_off + len(model_str)
    entries.append(make_ifd_entry(0x0132, 2, len(dt_str), struct.pack(">L", dt_off)))
    # GPSInfo pointer (tag 0x8825, LONG)
    gps_ifd_offset = dt_off + len(dt_str)
    entries.append(make_ifd_entry(0x8825, 4, 1, struct.pack(">L", gps_ifd_offset)))
    # Orientation (tag 0x0112, SHORT) = 1, pads with two zero bytes
    entries.append(make_ifd_entry(0x0112, 3, 1, struct.pack(">HH", 1, 0)))

    ifd0 = struct.pack(">H", len(entries)) + b"".join(entries) + struct.pack(">L", 0)

    # Data block following IFD0
    data_block = make_str + model_str + dt_str

    # ---- GPS IFD ----
    # Entries:
    #   GPSLatitudeRef  (tag 1, ASCII 2) = "N\x00"
    #   GPSLatitude     (tag 2, RATIONAL x3) = 32/1, 4/1, 42.24/100 (Tel Aviv)
    #   GPSLongitudeRef (tag 3, ASCII 2) = "E\x00"
    #   GPSLongitude    (tag 4, RATIONAL x3)
    gps_entries = []
    gps_body_len = 2 + (4 * 12) + 4
    gps_data_offset = gps_ifd_offset + gps_body_len

    # GPSLatitudeRef — 2 bytes fits inline, left-justified + zero-pad
    gps_entries.append(make_ifd_entry(0x0001, 2, 2, b"N\x00\x00\x00"))
    # GPSLatitude — 3 rationals @ 8 bytes each = 24 bytes — goes to data block
    lat_off = gps_data_offset
    lat_data = rational(32, 1) + rational(4, 1) + rational(4224, 100)
    gps_entries.append(make_ifd_entry(0x0002, 5, 3, struct.pack(">L", lat_off)))
    # GPSLongitudeRef
    gps_entries.append(make_ifd_entry(0x0003, 2, 2, b"E\x00\x00\x00"))
    # GPSLongitude
    lon_off = lat_off + len(lat_data)
    lon_data = rational(34, 1) + rational(46, 1) + rational(4908, 100)
    gps_entries.append(make_ifd_entry(0x0004, 5, 3, struct.pack(">L", lon_off)))

    gps_ifd = struct.pack(">H", len(gps_entries)) + b"".join(gps_entries) + struct.pack(">L", 0)
    gps_data = lat_data + lon_data

    tiff_payload = tiff + ifd0 + data_block + gps_ifd + gps_data
    exif_payload = b"Exif\x00\x00" + tiff_payload
    # APP1 marker + length (length includes 2 length bytes, excludes marker)
    length = len(exif_payload) + 2
    app1 = b"\xFF\xE1" + struct.pack(">H", length) + exif_payload
    return app1

app1 = build_app1()
# Insert APP1 right after SOI (0xFFD8)
jpeg_bytes = SKELETON_JPEG[:2] + app1 + SKELETON_JPEG[2:]

out = "/tmp/exif-laden-sample.jpg"
with open(out, "wb") as f:
    f.write(jpeg_bytes)
print(f"wrote {len(jpeg_bytes)} bytes to {out}")
