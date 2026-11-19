using System.Numerics;
using YukkuriMovieMaker.UndoRedo;

namespace YMM43D.PreviewTool
{
    internal sealed class CursorCommand(Vector3 before, Vector3 after, Action<Vector3> place) : IUndoRedoCommand
    {
        private Vector3 After => after;

        public bool IsAction => false;

        public bool IsEmpty => before == after;

        public bool CanConnect(IUndoRedoCommandBase command) => command is CursorCommand;

        public IUndoRedoCommandBase Connect(IUndoRedoCommandBase command) => new CursorCommand(before, ((CursorCommand)command).After, place);

        public void Undo() => place(before);

        public void Redo() => place(after);
    }
}
