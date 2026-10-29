// Decompile the function containing each given address; if none, create one
// at the nearest preceding function start or dump raw disassembly around it.
// Args: output file, then hex addresses.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import java.io.*;
import java.util.*;

public class DumpAt extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        Set<Function> done = new HashSet<>();
        for (int i = 1; i < args.length; i++) {
            Address a = toAddr(Long.parseLong(args[i], 16));
            Function f = getFunctionContaining(a);
            out.println("##### " + a + " -> " + (f == null ? "no function" : f.getName()));
            if (f == null) {
                Instruction ins = getInstructionContaining(a);
                if (ins == null) { disassemble(a); ins = getInstructionAt(a); }
                for (int n = 0; n < 40 && ins != null; n++) ins = ins.getPrevious();
                for (int n = 0; n < 90 && ins != null; n++, ins = ins.getNext())
                    out.println("  " + ins.getAddress() + "  " + ins);
                continue;
            }
            if (!done.add(f)) continue;
            DecompileResults res = dec.decompileFunction(f, 60, monitor);
            out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "<decompile failed>");
        }
        out.close();
    }
}
