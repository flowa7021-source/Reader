using Foliant.Domain;
using UglyToad.PdfPig.Core;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Превращает плоский depth-список в PDF outline-linkage: для каждого узла — /Parent, /Prev, /Next,
/// /First, /Last, /Count, плюс root-уровневые /First /Last /Count. Алгоритм — один проход с
/// depth-stack'ом: <c>stack[d]</c> хранит индекс последнего узла на глубине <c>d</c>; новый узел
/// глубины <c>d</c> становится next-sibling'ом для <c>stack[d]</c> (или первым ребёнком
/// <c>stack[d-1]</c> / первым root'ом). Глубина зажимается так, чтобы нельзя было «провалиться»
/// глубже, чем на 1 уровень за шаг — это держит дерево связным даже для кривого входа (Depth=5
/// сразу после Depth=0 трактуется как Depth=1).
/// </summary>
internal static class OutlineLinks
{
    public static OutlineLinkTable Compute(
        IReadOnlyList<DocumentOutlineEntry> entries, IndirectReference root, IReadOnlyList<IndirectReference> itemRefs)
    {
        var items = new OutlineItemLinks[entries.Count];
        var lastAtDepth = new List<int>();
        var rootChain = SiblingChain.Empty;

        for (int i = 0; i < entries.Count; i++)
        {
            int depth = ClampDepth(entries[i].Depth, lastAtDepth.Count);
            TrimStack(lastAtDepth, depth);

            if (depth == 0)
            {
                LinkSibling(items, itemRefs, prev: rootChain.Last, current: i, parent: root);
                rootChain = rootChain.Append(i);
            }
            else
            {
                LinkChild(items, itemRefs, parentIdx: lastAtDepth[depth - 1], current: i);
            }

            lastAtDepth.Add(i);
        }

        return new OutlineLinkTable(
            items,
            rootChain.First >= 0 ? itemRefs[rootChain.First] : null,
            rootChain.Last >= 0 ? itemRefs[rootChain.Last] : null,
            rootChain.Count);
    }

    // Нельзя прыгнуть глубже чем на 1 уровень за раз — иначе появится «висячий» уровень без родителя.
    private static int ClampDepth(int requested, int currentStackDepth) =>
        Math.Clamp(requested, 0, currentStackDepth);

    /// <summary>Аккумулятор root-уровневой цепочки sibling'ов: индекс первого/последнего узла и их
    /// количество. <see cref="First"/> = -1 пока цепочка пуста.</summary>
    private readonly record struct SiblingChain(int First, int Last, int Count)
    {
        public static SiblingChain Empty => new(-1, -1, 0);

        public SiblingChain Append(int index) =>
            new(First < 0 ? index : First, index, Count + 1);
    }

    private static void TrimStack(List<int> stack, int depth)
    {
        while (stack.Count > depth)
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    private static void LinkSibling(
        OutlineItemLinks[] items, IReadOnlyList<IndirectReference> refs, int prev, int current, IndirectReference parent)
    {
        IndirectReference? prevRef = prev >= 0 ? refs[prev] : null;
        items[current] = items[current] with { Parent = parent, Prev = prevRef };
        if (prev >= 0)
        {
            items[prev] = items[prev] with { Next = refs[current] };
        }
    }

    private static void LinkChild(OutlineItemLinks[] items, IReadOnlyList<IndirectReference> refs, int parentIdx, int current)
    {
        var parent = items[parentIdx];
        IndirectReference? prevSibling = parent.Last;
        items[current] = items[current] with { Parent = refs[parentIdx], Prev = prevSibling };

        if (prevSibling is { } ps)
        {
            int prevIdx = IndexOf(refs, ps);
            items[prevIdx] = items[prevIdx] with { Next = refs[current] };
        }

        items[parentIdx] = parent with
        {
            First = parent.First ?? refs[current],
            Last = refs[current],
            ChildCount = parent.ChildCount + 1,
        };
    }

    private static int IndexOf(IReadOnlyList<IndirectReference> refs, IndirectReference target)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            if (refs[i].ObjectNumber == target.ObjectNumber)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Linkage одного outline-узла. Все ссылки опциональны (root-уровень → нет /Parent? нет —
/// у item'а /Parent всегда есть; null означает «не выписывать ключ»).</summary>
internal readonly record struct OutlineItemLinks(
    IndirectReference Parent,
    IndirectReference? Prev,
    IndirectReference? Next,
    IndirectReference? First,
    IndirectReference? Last,
    int ChildCount);

/// <summary>Полный результат: per-item linkage + root-уровневые /First /Last /Count.</summary>
internal sealed record OutlineLinkTable(
    IReadOnlyList<OutlineItemLinks> Items,
    IndirectReference? RootFirst,
    IndirectReference? RootLast,
    int RootCount);
