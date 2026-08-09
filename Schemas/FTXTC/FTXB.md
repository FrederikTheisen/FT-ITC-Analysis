# FTXB version 1

FTXB is a little-endian row-major matrix. Bytes 0–3 are `FTXB`; byte 4 is version 1; byte 5 is scalar type (1 Float32, 2 Float64, 3 UInt8); byte 6 is layout 1; byte 7 is reserved; signed little-endian Int32 row and column counts start at offsets 8 and 12; payload starts at offset 16. Readers require an exact payload length and positive column count.
