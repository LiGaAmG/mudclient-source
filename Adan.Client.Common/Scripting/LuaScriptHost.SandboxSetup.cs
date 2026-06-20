namespace Adan.Client.Common.Scripting
{
    using NLua;

    public sealed partial class LuaScriptHost
    {
        // Only these globals survive. Everything else (io, os.execute,
        // package, require, debug, dofile, loadfile) is stripped so a
        // user script cannot touch the filesystem, spawn processes, or
        // load arbitrary assemblies.
        private static readonly string[] AllowedGlobals =
        {
            "string", "table", "math", "tostring", "tonumber", "type",
            "pairs", "ipairs", "select", "error", "pcall", "xpcall",
            "assert", "print",
        };

        private static Lua CreateSandboxedState()
        {
            var lua = new Lua();
            lua.State.Encoding = System.Text.Encoding.UTF8;

            // NLua exposes the full standard library by default; remove
            // anything not in the allowlist instead of trying to enumerate
            // every dangerous function individually. The allowlist is
            // built as a real Lua table (NLua's DoString has no overload
            // for passing extra chunk arguments, and a bare C# string[]
            // does not behave like a Lua sequence for ipairs/# purposes),
            // then discarded once the sweep is done.
            var allowed = (LuaTable)lua.DoString("return {}")[0];
            for (var i = 0; i < AllowedGlobals.Length; i++)
            {
                allowed[i + 1] = AllowedGlobals[i];
            }

            lua["__sandbox_allowed"] = allowed;
            lua.DoString(@"
                local allowed = {}
                for _, name in ipairs(__sandbox_allowed) do allowed[name] = true end
                for key in pairs(_G) do
                    if not allowed[key] and key ~= '_G' then
                        _G[key] = nil
                    end
                end
                __sandbox_allowed = nil
            ", "sandbox-init");

            return lua;
        }
    }
}
