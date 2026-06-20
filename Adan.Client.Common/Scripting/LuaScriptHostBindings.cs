namespace Adan.Client.Common.Scripting
{
    using System;

    /// <summary>
    /// Delegate bundle LuaScriptHost uses to reach RootModel capabilities
    /// without depending on RootModel directly (RootModel HOLDS a
    /// LuaScriptHost, not the other way around -- a direct reference would
    /// be circular). Every field is nullable; LuaScriptHost no-ops (or
    /// returns an empty string, for GetVariable) any binding that's left
    /// unset, the same way the original SendCommand-only constructor
    /// already no-ops when given a null delegate. This is also what keeps
    /// LuaScriptHost unit-testable without a live RootModel: tests construct
    /// a LuaScriptHostBindings with simple recording delegates instead.
    /// </summary>
    public sealed class LuaScriptHostBindings
    {
        public Action<string> SendCommand;
        public Action<string, string> SetVariable;
        public Action<string> ClearVariable;
        public Func<string, string> GetVariable;
        public Action<string> Echo;
        public Action<string> EnableGroup;
        public Action<string> DisableGroup;
        public Action<string> SetStatus;
        public Action<string, string> SendToWindow;
        public Action<string> SendToAllWindows;
    }
}
