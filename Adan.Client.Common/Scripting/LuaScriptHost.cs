namespace Adan.Client.Common.Scripting
{
    using System;
    using KeraLua;
    using NLua;

    /// <summary>
    /// One persistent, sandboxed Lua state per tab (per RootModel). Created
    /// once when the tab opens, disposed once when the tab closes. Scripts
    /// attached to triggers/aliases and packet-state handlers registered
    /// for this tab all run inside this single state, so their variables
    /// do not leak across tabs.
    /// </summary>
    public sealed partial class LuaScriptHost : IDisposable
    {
        // 1,000,000 VM instructions is comfortably more than any real
        // trigger/state-handler script needs per invocation (a handler
        // iterating a 50-member group with a few comparisons is in the
        // low thousands), and low enough that a runaway loop is killed
        // in well under 50ms on typical hardware.
        private const int InstructionBudget = 1_000_000;

        private readonly NLua.Lua _lua;
        private readonly KeraLua.LuaHookFunction _hookFunction;
        private bool _disposed;
        private bool _budgetExceeded;

        public LuaScriptHost()
        {
            _lua = CreateSandboxedState();

            // Keep a reference to the delegate for the lifetime of the host:
            // KeraLua's SetHook stores a native callback pointer derived from
            // this delegate, and if the delegate were only a transient
            // lambda the GC could collect it while native code still held
            // the pointer.
            _hookFunction = OnInstructionHook;
            _lua.State.SetHook(_hookFunction, LuaHookMask.Count, InstructionBudget);
        }

        private void OnInstructionHook(IntPtr luaStatePtr, IntPtr ar)
        {
            _budgetExceeded = true;

            // Resolve the KeraLua wrapper for the state pointer the hook
            // fired on, then raise a Lua error from inside the hook; NLua
            // surfaces this as a LuaScriptException at the DoString call.
            var state = KeraLua.Lua.FromIntPtr(luaStatePtr);
            state.Error("script exceeded instruction budget", Array.Empty<object>());
        }

        /// <summary>
        /// Evaluates a Lua expression of the form "return ...." and returns
        /// the first result. Intended for tests and simple host-side checks;
        /// production script entry points are the RegisterXxxHandler methods
        /// added in later tasks.
        /// </summary>
        /// <exception cref="NLua.Exceptions.LuaScriptException">
        /// Thrown when <paramref name="luaExpression"/> has invalid syntax or
        /// raises a runtime error during execution.
        /// </exception>
        /// <exception cref="LuaScriptTimeoutException">
        /// Thrown when the script exceeds the instruction budget and is aborted.
        /// </exception>
        public object Eval(string luaExpression)
        {
            _budgetExceeded = false;
            try
            {
                var results = _lua.DoString(luaExpression);
                return results.Length > 0 ? results[0] : null;
            }
            catch (NLua.Exceptions.LuaScriptException)
            {
                if (_budgetExceeded)
                {
                    throw new LuaScriptTimeoutException(
                        "Script exceeded the instruction budget (" + InstructionBudget + " instructions) and was aborted.");
                }

                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _lua.Dispose();
            _disposed = true;
        }
    }
}
