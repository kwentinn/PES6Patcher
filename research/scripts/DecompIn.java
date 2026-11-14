// Decompile the function containing each given address, in a project imported without analysis: the entry is
// searched backwards among the 16-aligned addresses that follow padding or a return.
// Args: output file, then hex addresses.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import java.io.*;
import java.util.*;

public class DecompIn extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        Set<Address> done = new HashSet<>();
        for (int i = 1; i < args.length; i++) {
            Address a = toAddr(Long.parseLong(args[i], 16));
            Function f = getFunctionContaining(a);
            long start = a.getOffset() & ~0xFL;
            for (int tries = 0; f == null && tries < 400; tries++, start -= 16) {
                Address c = toAddr(start);
                int p1 = getByte(c.subtract(1)) & 0xFF, p3 = getByte(c.subtract(3)) & 0xFF;
                if (p1 != 0xCC && p1 != 0x90 && p1 != 0xC3 && p3 != 0xC2) continue;
                if (getFunctionAt(c) != null) continue;
                disassemble(c);
                Function g = createFunction(c, null);
                if (g != null && g.getBody().contains(a)) f = g;
                else if (g != null) removeFunction(g);
            }
            out.println("##### " + a + " -> " + (f == null ? "no function" : f.getName() + " " + f.getBody()));
            if (f == null || !done.add(f.getEntryPoint())) continue;
            DecompileResults res = dec.decompileFunction(f, 180, monitor);
            out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "<decompile failed> " + res.getErrorMessage());
            out.println("----- asm");
            InstructionIterator it = currentProgram.getListing().getInstructions(f.getBody(), true);
            while (it.hasNext()) { Instruction ins = it.next(); out.println(ins.getAddress() + "  " + ins); }
        }
        out.close();
    }
}
