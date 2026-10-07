"""String anchoring, end to end.

Find a function in the NEW image by the log string it references, with no
dependence on its instruction bytes. Validated against the OLD image first,
where the answer is already known.
"""
import re
from capstone import *
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

OLD = r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_44C3EE9C.bin"
NEW = r"G:\LMU_Plugin\analysis\dumps\LMU_runtime_C40C815F.bin"
old = open(OLD, "rb").read()
new = open(NEW, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_64); md.detail = True


def find_string(buf, text):
    """Every offset holding this exact NUL-terminated string."""
    pat = text.encode() + b"\x00"
    return [m.start() for m in re.finditer(re.escape(pat), buf)]


def refs_to(buf, target, scan_from=0x1000, scan_to=None):
    """Offsets of instructions whose RIP-relative operand points at target."""
    scan_to = scan_to or min(len(buf), 0x1800000)
    out = []
    # disp32 = target - (insn_end). Search for the 4 bytes directly: far cheaper
    # than disassembling 24 MB, then confirm each hit by decoding.
    for end_guess in range(4, 12):
        want = (target - 0).to_bytes(8, "little", signed=False)
    i = scan_from
    step = 0x100000
    while i < scan_to:
        chunk = buf[i:i + step + 16]
        for ins in md.disasm(chunk, i):
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                    if ins.address + ins.size + op.mem.disp == target:
                        out.append(ins.address)
        i += step
    return out


def function_start(buf, inside, back=0x800):
    """Walk back to the nearest plausible prologue.

    Looks for INT3/NOP padding followed by a standard frame set-up, which is
    how MSVC lays functions out.
    """
    lo = max(0, inside - back)
    best = None
    for off in range(inside, lo, -1):
        if buf[off - 1] in (0xCC, 0x90) and buf[off] in (0x40, 0x48, 0x4C, 0x55, 0x53, 0x56, 0x57):
            best = off
            break
    return best


TARGET = "Entered Slot::Restart(%d)"
KNOWN_OLD = 0x00D2F810

for label, buf, known in (("OLD", old, KNOWN_OLD), ("NEW", new, None)):
    print(f"--- {label} image")
    hits = find_string(buf, TARGET)
    print(f"  string {TARGET!r}: {len(hits)} hit(s) at {[hex(h) for h in hits]}")
    if not hits:
        continue
    sites = refs_to(buf, hits[0])
    print(f"  referenced by {len(sites)} instruction(s): {[hex(s) for s in sites[:5]]}")
    starts = {function_start(buf, s) for s in sites}
    starts.discard(None)
    for s in sorted(starts):
        mark = ""
        if known is not None:
            mark = "  <- MATCHES the profile" if s == known else f"  (profile says {known:#x})"
        print(f"  function start candidate: {s:#010x}{mark}")
    print()
