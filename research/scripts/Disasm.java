// Print disassembly between two addresses. Args: output file, start hex, end hex.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import java.io.*;

public class Disasm extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] a = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(a[0]));
        Instruction ins = getInstructionContaining(toAddr(Long.parseLong(a[1], 16)));
        long end = Long.parseLong(a[2], 16);
        while (ins != null && ins.getAddress().getOffset() < end) { out.println(ins.getAddress() + "  " + ins); ins = ins.getNext(); }
        out.close();
    }
}
