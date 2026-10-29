// List references to external symbols whose name matches a pattern, with containing function.
// Args: output file, substring.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;

public class FindSym extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] a = getScriptArgs();
        PrintWriter out = new PrintWriter(new FileWriter(a[0]));
        SymbolIterator it = currentProgram.getSymbolTable().getAllSymbols(true);
        while (it.hasNext()) {
            Symbol s = it.next();
            if (!s.getName().contains(a[1])) continue;
            out.println("symbol " + s.getName() + " @ " + s.getAddress());
            for (Reference r : getReferencesTo(s.getAddress())) {
                Function f = getFunctionContaining(r.getFromAddress());
                out.println("  from " + r.getFromAddress() + " in " + (f == null ? "?" : f.getName() + " @ " + f.getEntryPoint()));
            }
        }
        out.close();
    }
}
