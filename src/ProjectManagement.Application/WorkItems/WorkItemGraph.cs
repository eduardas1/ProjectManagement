using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.WorkItems;

public static class WorkItemGraph
{
    public static bool CreatesCycle(IReadOnlyList<WorkItemLink> links, int sourceId, int targetId)
    {
        var adjacency = links.ToLookup(link => link.SourceId, link => link.TargetId);
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(targetId);
        while (pending.TryPop(out var id))
        {
            if (id == sourceId) return true;
            if (!visited.Add(id)) continue;
            foreach (var target in adjacency[id]) pending.Push(target);
        }
        return false;
    }
}
