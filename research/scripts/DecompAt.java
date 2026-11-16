// Decompile a function created exactly at each given address (DecompIn searches the entry backwards instead): for code
// reached by a jump out of the protection, where the bytes before are another function's.
// Args: output file, then hex addresses.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import java.io.*;

public class DecompAt extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(args[0]));
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        for (int i = 1; i < args.length; i++) {
            Address a = toAddr(Long.parseLong(args[i], 16));
            Function f = getFunctionAt(a);
            if (f == null) {
                Function inside = getFunctionContaining(a);
                if (inside != null) removeFunction(inside);
                disassemble(a);
                f = createFunction(a, null);
            }
            out.println("##### " + a + " -> " + (f == null ? "no function" : f.getName() + " " + f.getBody()));
            if (f == null) continue;
            DecompileResults res = dec.decompileFunction(f, 180, monitor);
            out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "<decompile failed> " + res.getErrorMessage());
            out.println("----- asm");
            InstructionIterator it = currentProgram.getListing().getInstructions(f.getBody(), true);
            while (it.hasNext()) { Instruction ins = it.next(); out.println(ins.getAddress() + "  " + ins); }
        }
        out.close();
    }
}
