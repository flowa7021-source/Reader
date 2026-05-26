using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf.Editing;
using Xunit;

namespace Foliant.Engines.Pdf.Tests.Editing;

/// <summary>
/// Pure unit tests for <see cref="PdfDocumentEditor"/> sequencing. Использует
/// инъекцию dispatch-делегата (stub-трансформация, кодирующая применённую
/// последовательность в байтах) и <see cref="FakeEventStore"/>, поэтому не зависит
/// от PdfPig/PDFium. Проверяем Apply→append, Undo→replay, Redo, Save→IsDirty=false.
/// </summary>
public sealed class PdfDocumentEditorTests : IDisposable
{
    private const string Fingerprint = "fp-test";

    private readonly string _tmpDir;
    private readonly string _targetPath;
    private readonly FakeEventStore _store = new();

    public PdfDocumentEditorTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-editor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _targetPath = Path.Combine(_tmpDir, "doc.pdf");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task Apply_AppendsEvent_SetsDirty_AndTransforms()
    {
        var editor = NewEditor();

        await editor.ApplyAsync(new DeletePageCommand(1), default);

        editor.IsDirty.Should().BeTrue();
        _store.AppendCount.Should().Be(1);
        _store.Stream(Fingerprint).Should().ContainSingle().Which.Kind.Should().Be("delete-page");
    }

    [Fact]
    public async Task Apply_Twice_AccumulatesInWorkingState()
    {
        var (editor, working) = NewEditorWithProbe();

        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.ApplyAsync(new RotatePageCommand(1, ViewRotation.Cw90), default);

        // base + два маркера, в порядке применения.
        Decode(working()).Should().Equal("delete-page", "rotate-page");
        _store.Stream(Fingerprint).Should().HaveCount(2);
    }

    [Fact]
    public async Task Undo_ReplaysFromBase_AndCompacts()
    {
        var (editor, working) = NewEditorWithProbe();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.ApplyAsync(new RotatePageCommand(1, ViewRotation.Cw90), default);

        await editor.UndoAsync(default);

        Decode(working()).Should().Equal("delete-page");
        _store.Stream(Fingerprint).Should().ContainSingle().Which.Kind.Should().Be("delete-page");
        _store.CompactCount.Should().Be(1);
    }

    [Fact]
    public async Task Undo_OnEmpty_IsNoOp()
    {
        var editor = NewEditor();

        await editor.UndoAsync(default);

        _store.AppendCount.Should().Be(0);
        _store.CompactCount.Should().Be(0);
    }

    [Fact]
    public async Task Redo_ReappliesUndoneCommand()
    {
        var (editor, working) = NewEditorWithProbe();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.UndoAsync(default);

        await editor.RedoAsync(default);

        Decode(working()).Should().Equal("delete-page");
        _store.Stream(Fingerprint).Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_AfterUndo_ClearsRedo()
    {
        var editor = NewEditor();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.UndoAsync(default);

        await editor.ApplyAsync(new RotatePageCommand(0, ViewRotation.Cw180), default);
        await editor.RedoAsync(default); // redo стек очищен → no-op

        _store.Stream(Fingerprint).Should().ContainSingle().Which.Kind.Should().Be("rotate-page");
    }

    [Fact]
    public async Task Save_WritesWorkingBytes_ResetsState()
    {
        var (editor, working) = NewEditorWithProbe();
        await editor.ApplyAsync(new DeletePageCommand(0), default);

        await editor.SaveAsync(null, default);

        editor.IsDirty.Should().BeFalse();
        File.ReadAllBytes(_targetPath).Should().Equal(working());
        _store.Stream(Fingerprint).Should().BeEmpty();      // compacted to []
        _store.CompactCount.Should().Be(1);
    }

    [Fact]
    public async Task Save_ThenUndo_IsNoOp_BecauseAppliedCleared()
    {
        var editor = NewEditor();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.SaveAsync(null, default);

        await editor.UndoAsync(default);

        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Save_ToExplicitPath_WritesThere()
    {
        var editor = NewEditor();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        string other = Path.Combine(_tmpDir, "copy.pdf");

        await editor.SaveAsync(other, default);

        File.Exists(other).Should().BeTrue();
        Directory.GetFiles(_tmpDir, "*.tmp").Should().BeEmpty("temp file must be moved, not left behind");
    }

    [Fact]
    public async Task Undo_WhenReplayThrows_DoesNotCommitPartialState()
    {
        var (editor, working, fail) = NewEditorWithFailToggle();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.ApplyAsync(new RotatePageCommand(1, ViewRotation.Cw90), default);

        fail(true); // replay over the remaining ["delete-page"] will throw
        await editor.Invoking(e => e.UndoAsync(default)).Should().ThrowAsync<InvalidOperationException>();

        // The throwing undo must not have removed "rotate-page" from the applied log: a retry
        // (now succeeding) replays exactly ["delete-page"]. If state had been corrupted, the
        // remaining log would be empty and working would still show both commands.
        fail(false);
        await editor.UndoAsync(default);
        Decode(working()).Should().Equal("delete-page");
        editor.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Redo_WhenDispatchThrows_DoesNotDropTheRedoEntry()
    {
        var (editor, working, fail) = NewEditorWithFailToggle();
        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.UndoAsync(default); // pushes the command onto the redo stack

        fail(true);
        await editor.Invoking(e => e.RedoAsync(default)).Should().ThrowAsync<InvalidOperationException>();

        // The failed redo must not have popped the entry: a retry re-applies it.
        fail(false);
        await editor.RedoAsync(default);
        Decode(working()).Should().Equal("delete-page");
    }

    private (PdfDocumentEditor Editor, Func<byte[]> Working, Action<bool> Fail) NewEditorWithFailToggle()
    {
        byte[] baseBytes = Encode([]);
        byte[] current = baseBytes;
        bool shouldFail = false;
        var editor = new PdfDocumentEditor(baseBytes, Fingerprint, _store, _targetPath, Dispatch);
        return (editor, () => current, f => shouldFail = f);

        byte[] Dispatch(byte[] input, DocumentCommandRecord rec)
        {
            if (shouldFail)
            {
                throw new InvalidOperationException("dispatch failed");
            }
            current = Encode(Decode(input).Append(rec.Kind));
            return current;
        }
    }

    private PdfDocumentEditor NewEditor() => NewEditorWithProbe().Editor;

    private (PdfDocumentEditor Editor, Func<byte[]> Working) NewEditorWithProbe()
    {
        byte[] baseBytes = Encode([]);
        byte[] current = baseBytes;
        var editor = new PdfDocumentEditor(baseBytes, Fingerprint, _store, _targetPath, StubDispatch);
        return (editor, () => current);

        // Stub: добавляет Kind как маркер. Чисто, детерминированно, без PdfPig.
        byte[] StubDispatch(byte[] input, DocumentCommandRecord rec)
        {
            var kinds = Decode(input).Append(rec.Kind);
            current = Encode(kinds);
            return current;
        }
    }

    private static byte[] Encode(IEnumerable<string> kinds) =>
        Encoding.UTF8.GetBytes(string.Join("\n", kinds));

    private static string[] Decode(byte[] bytes)
    {
        string s = Encoding.UTF8.GetString(bytes);
        return s.Length == 0 ? [] : s.Split('\n');
    }
}
