using System.Collections.Generic;

namespace Adan.Client.Common.Scripting
{
    /// <summary>Safe, documented snippets offered by the native Scripts editor.</summary>
    public sealed class ScriptSnippet
    {
        public string Category { get; private set; }
        public string Title { get; private set; }
        public string Code { get; private set; }

        public ScriptSnippet(string category, string title, string code)
        {
            Category = category;
            Title = title;
            Code = code;
        }
    }

    public static class ScriptSnippetCatalog
    {
        public static readonly IList<ScriptSnippet> All = new List<ScriptSnippet>
        {
            new ScriptSnippet("Ожидание", "Wait(ms)", "Wait(1000)"),
            new ScriptSnippet("Ожидание", "WaitText(ms)", "local line = WaitText(1000)"),
            new ScriptSnippet("Ожидание", "WaitGroupState()", "WaitGroupState()"),
            new ScriptSnippet("Ожидание", "WaitRoomState()", "WaitRoomState()"),
            new ScriptSnippet("Ожидание", "WaitRoomChange()", "WaitRoomChange()"),

            new ScriptSnippet("Клиент", "SendCommand(text)", "SendCommand(\"команда\")"),
            new ScriptSnippet("Клиент", "Echo(text)", "Echo(\"текст\")"),
            new ScriptSnippet("Клиент", "Lower(text)", "Lower(\"ТЕКСТ\")"),
            new ScriptSnippet("Клиент", "GetVariable(name)", "local value = GetVariable(\"имя\")"),
            new ScriptSnippet("Клиент", "SetVariable(name, value)", "SetVariable(\"имя\", \"значение\")"),
            new ScriptSnippet("Клиент", "ClearVariable(name)", "ClearVariable(\"имя\")"),
            new ScriptSnippet("Клиент", "SetStatus(text)", "SetStatus(\"статус\")"),

            new ScriptSnippet("Lua", "local переменная", "local value = "),
            new ScriptSnippet("Lua", "if … then", "if condition then\n    \nend"),
            new ScriptSnippet("Lua", "for i = 1, n", "for i = 1, count do\n    \nend"),
            new ScriptSnippet("Lua", "for key, value in pairs", "for key, value in pairs(table) do\n    \nend"),
            new ScriptSnippet("Lua", "for i, value in ipairs", "for i, value in ipairs(list) do\n    \nend"),
            new ScriptSnippet("Lua", "function", "function Name()\n    \nend"),
            new ScriptSnippet("Lua", "string.match", "string.match(text, \"pattern\")"),
            new ScriptSnippet("Lua", "table.insert", "table.insert(list, value)"),
            new ScriptSnippet("Lua", "tonumber", "tonumber(value)"),
        };
    }
}
