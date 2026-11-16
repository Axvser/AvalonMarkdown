"""PlantUML text -> URL encoding, byte-for-byte identical to plantuml-encoder@1.4.0.

The preview builds its diagram URLs with that npm package (loaded from the CDN in
index.html), so a checker that wants to ask plantuml.com the same question must
reproduce its output exactly. The algorithm is:

    deflateRaw(utf8(text), level 9)  ->  pack every 3 bytes into 4 six-bit chars

The 6-bit alphabet is 0-9 A-Z a-z - _ (the standard PlantUML alphabet), which is
URL-safe, so no percent-encoding is needed on the result.

Verified against the real service: the encoder here produces URLs that return
image/svg+xml diagrams for valid input and HTTP 400 for syntax errors -- the same
status codes the browser's <img> sees.

    python plantuml_encode.py "@startuml\nA -> B\n@enduml"
"""

from __future__ import annotations

import sys
import zlib

__all__ = ["encode", "diagram_url", "DEFAULT_SERVER"]

DEFAULT_SERVER = "https://www.plantuml.com/plantuml/svg/"

_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_"


def _encode6bit(b: int) -> str:
    """Map a 0-63 value to its alphabet character.

    Byte-for-byte the `encode6bit` of plantuml-encoder@1.4.0, including its
    unreachable '?' fallback for out-of-range input.
    """
    if b < 10:
        return chr(48 + b)
    b -= 10
    if b < 26:
        return chr(65 + b)
    b -= 26
    if b < 26:
        return chr(97 + b)
    b -= 26
    if b == 0:
        return "-"
    if b == 1:
        return "_"
    return "?"


def _append3bytes(b1: int, b2: int, b3: int) -> str:
    """Pack three bytes into four 6-bit characters (the upstream `append3bytes`)."""
    c1 = b1 >> 2
    c2 = ((b1 & 0x3) << 4) | (b2 >> 4)
    c3 = ((b2 & 0xF) << 2) | (b3 >> 6)
    c4 = b3 & 0x3F
    return _encode6bit(c1) + _encode6bit(c2) + _encode6bit(c3) + _encode6bit(c4)


def _deflate_raw(data: str) -> bytes:
    """Raw DEFLATE (no zlib header, no adler32) of the UTF-8 bytes."""
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    return compressor.compress(data.encode("utf-8")) + compressor.flush()


def encode(text: str) -> str:
    """Encode PlantUML source into the path segment the preview would build."""
    compressed = _deflate_raw(text)
    out = []
    for i in range(0, len(compressed), 3):
        chunk = compressed[i:i + 3]
        # The tail is zero-padded to a full triple; upstream does the same by
        # reading past the end as 0, which never changes the first characters
        # that matter for the final partial group.
        b1 = chunk[0]
        b2 = chunk[1] if len(chunk) > 1 else 0
        b3 = chunk[2] if len(chunk) > 2 else 0
        out.append(_append3bytes(b1, b2, b3))
    return "".join(out)


def diagram_url(text: str, server: str = DEFAULT_SERVER) -> str:
    """Full URL the preview's <img src> would request for this diagram."""
    return server + encode(text)


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    # Join so a caller can pass the diagram as separate shell words.
    text = " ".join(argv[1:]).replace("\\n", "\n")
    print(diagram_url(text))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
