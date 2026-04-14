using CelestialMechanics.AppCore.Scene;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Services;

public sealed class SceneService : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<int, Guid> _bodyToNode = new();
    private readonly Dictionary<Guid, int> _nodeToBody = new();

    public SceneGraph SceneGraph { get; } = new();
    public SelectionManager SelectionManager { get; } = new();

    public event Action? SceneChanged;

    public SceneService()
    {
        SelectionManager.BindSceneGraph(SceneGraph);
    }

    public void RepopulateFromSimulation(SimulationService simulationService)
    {
        var bodies = simulationService.GetBodies();

        lock (_sync)
        {
            ClearAllNodesLocked();
            _bodyToNode.Clear();
            _nodeToBody.Clear();

            foreach (var body in bodies.OrderBy(b => b.Id))
            {
                var node = new SceneNode(BuildNodeName(body), NodeType.Entity);
                SceneGraph.AddNode(Guid.Empty, node);
                _bodyToNode[body.Id] = node.Id;
                _nodeToBody[node.Id] = body.Id;
            }

            SelectionManager.PurgeStaleSelections();
        }

        SceneChanged?.Invoke();
    }

    public Guid? GetNodeIdForBody(int bodyId)
    {
        lock (_sync)
        {
            return _bodyToNode.TryGetValue(bodyId, out var nodeId)
                ? nodeId
                : null;
        }
    }

    public int? GetBodyIdForNode(Guid nodeId)
    {
        lock (_sync)
        {
            return _nodeToBody.TryGetValue(nodeId, out var bodyId)
                ? bodyId
                : null;
        }
    }

    public IReadOnlyList<SceneNodeItem> GetItems()
    {
        lock (_sync)
        {
            return SceneGraph.TraverseDepthFirst()
                .Where(node => _nodeToBody.ContainsKey(node.Id))
                .Select(node =>
                {
                    int bodyId = _nodeToBody[node.Id];
                    var bodyType = ExtractTypeFromName(node.Name);

                    return new SceneNodeItem
                    {
                        NodeId = node.Id,
                        BodyId = bodyId,
                        Name = node.Name,
                        BodyType = bodyType,
                    };
                })
                .ToList();
        }
    }

    public void Dispose()
    {
        SceneGraph.Dispose();
    }

    private void ClearAllNodesLocked()
    {
        var ids = SceneGraph.TraverseDepthFirst()
            .Select(node => node.Id)
            .ToList();

        foreach (var id in ids)
        {
            SceneGraph.RemoveNode(id);
        }
    }

    private static string BuildNodeName(PhysicsBody body)
    {
        return $"{body.Type} {body.Id}";
    }

    private static BodyType ExtractTypeFromName(string name)
    {
        var firstToken = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken != null && Enum.TryParse<BodyType>(firstToken, out var bodyType))
        {
            return bodyType;
        }

        return BodyType.Custom;
    }
}
