using System.Collections.Generic;

namespace Adan.Client.Common.Scripting
{
    /// <summary>Undo/redo snapshots for one unsaved script draft.</summary>
    public sealed class ScriptDraftHistory
    {
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();

        public void RecordChange(string previousText)
        {
            _undo.Push(previousText ?? string.Empty);
            _redo.Clear();
        }

        public bool TryUndo(string currentText, out string text)
        {
            if (_undo.Count == 0) { text = null; return false; }
            _redo.Push(currentText ?? string.Empty);
            text = _undo.Pop();
            return true;
        }

        public bool TryRedo(string currentText, out string text)
        {
            if (_redo.Count == 0) { text = null; return false; }
            _undo.Push(currentText ?? string.Empty);
            text = _redo.Pop();
            return true;
        }
    }
}
