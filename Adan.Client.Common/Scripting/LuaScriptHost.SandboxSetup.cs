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

        // The instruction-budget watchdog hook raises a Lua error to abort
        // a runaway script, but a plain native error() raised from the
        // hook only unwinds to the NEAREST enclosing pcall -- including a
        // pcall the *script itself* set up. A script can defeat the
        // watchdog entirely by re-wrapping itself in a new pcall every time
        // the old one is caught (e.g.
        // "while true do pcall(function() while true do end end) end"),
        // since each fresh pcall buys another window before the next hook
        // tick and the error never reaches the unprotected outer scope. No
        // amount of re-raising from the hook fixes this -- longjmp
        // semantics mean it always stops at the nearest pcall, however many
        // of those a malicious/buggy script creates.
        //
        // The fix: redefine pcall/xpcall in the sandbox so that after the
        // real (native) protected call returns -- whether it succeeded,
        // failed normally, or was aborted by the watchdog -- they check the
        // host-side __watchdog_timeout flag. If it is set, they immediately
        // re-raise instead of returning the swallowed result to the script,
        // so the error keeps propagating through every layer of
        // script-created pcalls until it reaches the unprotected top level
        // and surfaces to the C# host as a LuaScriptException.
        //
        // This must run AFTER __watchdog_timeout has been registered on the
        // Lua state (which requires a live LuaScriptHost instance), so it
        // is a separate instance method invoked from the constructor rather
        // than part of the static CreateSandboxedState.
        private void InstallPcallGuard()
        {
            _lua.DoString(@"
                local real_pcall = pcall
                local real_xpcall = xpcall
                local real_error = error

                pcall = function(...)
                    local results = { real_pcall(...) }
                    if __watchdog_timeout() then
                        real_error('script exceeded instruction budget')
                    end
                    return table.unpack(results)
                end

                xpcall = function(...)
                    local results = { real_xpcall(...) }
                    if __watchdog_timeout() then
                        real_error('script exceeded instruction budget')
                    end
                    return table.unpack(results)
                end
            ", "sandbox-pcall-guard");
        }
    }
}
