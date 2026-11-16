// Print disassembly between two addresses, disassembling what is not yet (code only reached through a jump table).
// Args: output file, start hex, end hex.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import java.io.*;

public class Disasm extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] a = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(a[0]));
        Address at = toAddr(Long.parseLong(a[1], 16));
        long end = Long.parseLong(a[2], 16);
        Instruction first = getInstructionContaining(at);
        if (first != null) at = first.getAddress();
        while (at.getOffset() < end) {
            Instruction ins = getInstructionAt(at);
            if (ins == null) { disassemble(at); ins = getInstructionAt(at); }
            if (ins == null) { out.println(at + "  ?? " + String.format("%02X", getByte(at) & 0xFF)); at = at.add(1); continue; }
            out.println(ins.getAddress() + "  " + ins);
            at = ins.getAddress().add(ins.getLength());
        }
        out.close();
    }
}
