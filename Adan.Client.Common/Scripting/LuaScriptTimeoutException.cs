namespace Adan.Client.Common.Scripting
{
    using System;

    /// <summary>
    /// Thrown when a script exceeds its instruction budget. Caught at every
    /// call site that invokes user script code so one bad script cannot
    /// hang the conveyor thread for the whole tab.
    /// </summary>
    public sealed class LuaScriptTimeoutException : Exception
    {
        public LuaScriptTimeoutException(string message) : base(message)
        {
        }
    }
}
