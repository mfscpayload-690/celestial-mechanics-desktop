using CelestialMechanics.AppCore.Scene;

namespace CelestialMechanics.AppCore.Tests;

public sealed class SceneGraphTests
{
    // ── AddNode ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddNode_ToRoot_NodeExists()
    {
        var graph = new SceneGraph();
        var node  = new SceneNode("Star A", NodeType.Entity);

        graph.AddNode(Guid.Empty, node);

        Assert.NotNull(graph.GetNode(node.Id));
        Assert.Equal("Star A", graph.GetNode(node.Id)!.Name);
    }

    [Fact]
    public void AddNode_ToParent_TreeStructureCorrect()
    {
        var graph  = new SceneGraph();
        var folder = new SceneNode("Cluster", NodeType.Folder);
        var child  = new SceneNode("Star", NodeType.Entity);

        graph.AddNode(Guid.Empty, folder);
        graph.AddNode(folder.Id, child);

        var retrieved = graph.GetNode(child.Id)!;
        Assert.NotNull(retrieved.Parent);
        Assert.Equal(folder.Id, retrieved.Parent!.Id);
        Assert.Single(graph.GetNode(folder.Id)!.Children);
    }

    [Fact]
    public void AddNode_DuplicateId_Throws()
    {
        var graph = new SceneGraph();
        var node  = new SceneNode("A", NodeType.Folder);
        graph.AddNode(Guid.Empty, node);

        Assert.Throws<InvalidOperationException>(() => graph.AddNode(Guid.Empty, node));
    }

    // ── RemoveNode ───────────────────────────────────────────────────────────

    [Fact]
    public void RemoveNode_LeafNode_RemovedFromParent()
    {
        var graph  = new SceneGraph();
        var parent = new SceneNode("Folder", NodeType.Folder);
        var leaf   = new SceneNode("Leaf",   NodeType.Entity);

        graph.AddNode(Guid.Empty, parent);
        graph.AddNode(parent.Id, leaf);

        bool removed = graph.RemoveNode(leaf.Id);

        Assert.True(removed);
        Assert.Null(graph.GetNode(leaf.Id));
        Assert.Empty(graph.GetNode(parent.Id)!.Children);
    }

    [Fact]
    public void RemoveNode_InternalNode_RemovesEntireSubtree()
    {
        var graph  = new SceneGraph();
        var folder = new SceneNode("Folder", NodeType.Folder);
        var child1 = new SceneNode("C1",     NodeType.Entity);
        var child2 = new SceneNode("C2",     NodeType.Entity);

        graph.AddNode(Guid.Empty, folder);
        graph.AddNode(folder.Id, child1);
        graph.AddNode(folder.Id, child2);

        graph.RemoveNode(folder.Id);

        Assert.Null(graph.GetNode(folder.Id));
        Assert.Null(graph.GetNode(child1.Id));
        Assert.Null(graph.GetNode(child2.Id));
    }

    [Fact]
    public void RemoveNode_Root_Throws()
    {
        var graph = new SceneGraph();
        Assert.Throws<InvalidOperationException>(() => graph.RemoveNode(Guid.Empty));
    }

    // ── MoveNode ─────────────────────────────────────────────────────────────

    [Fact]
    public void MoveNode_ChangesParent()
    {
        var graph = new SceneGraph();
        var folderA = new SceneNode("A", NodeType.Folder);
        var folderB = new SceneNode("B", NodeType.Folder);
        var child   = new SceneNode("C", NodeType.Entity);

        graph.AddNode(Guid.Empty, folderA);
        graph.AddNode(Guid.Empty, folderB);
        graph.AddNode(folderA.Id, child);

        graph.MoveNode(child.Id, folderB.Id);

        Assert.Equal(folderB.Id, graph.GetNode(child.Id)!.Parent!.Id);
        Assert.Empty(graph.GetNode(folderA.Id)!.Children);
        Assert.Single(graph.GetNode(folderB.Id)!.Children);
    }

    [Fact]
    public void MoveNode_IntoCycle_Throws()
    {
        var graph  = new SceneGraph();
        var parent = new SceneNode("P", NodeType.Folder);
        var child  = new SceneNode("C", NodeType.Folder);

        graph.AddNode(Guid.Empty, parent);
        graph.AddNode(parent.Id,  child);

        Assert.Throws<InvalidOperationException>(() => graph.MoveNode(parent.Id, child.Id));
    }

    // ── Traversal ────────────────────────────────────────────────────────────

    [Fact]
    public void TraverseDepthFirst_CorrectOrder()
    {
        var graph    = new SceneGraph();
        var folder   = new SceneNode("Folder", NodeType.Folder);
        var childA   = new SceneNode("A",      NodeType.Entity);
        var childB   = new SceneNode("B",      NodeType.Entity);

        graph.AddNode(Guid.Empty, folder);
        graph.AddNode(folder.Id, childA);
        graph.AddNode(folder.Id, childB);

        var order = graph.TraverseDepthFirst().Select(n => n.Name).ToList();

        // Folder comes before its children
        int folderIdx = order.IndexOf("Folder");
        int aIdx      = order.IndexOf("A");
        int bIdx      = order.IndexOf("B");
        Assert.True(folderIdx < aIdx, "Folder should precede A");
        Assert.True(folderIdx < bIdx, "Folder should precede B");
    }

    // ── FlattenToEntityList ───────────────────────────────────────────────────

    [Fact]
    public void FlattenToEntityList_ReturnsLinkedEntityIds()
    {
        var graph   = new SceneGraph();
        var entityId = Guid.NewGuid();
        var node    = new SceneNode("Star", NodeType.Entity) { LinkedEntityId = entityId };
        var folder  = new SceneNode("Folder", NodeType.Folder);

        graph.AddNode(Guid.Empty, folder);
        graph.AddNode(Guid.Empty, node); // folder has no link

        var list = graph.FlattenToEntityList().ToList();

        Assert.Single(list, entityId);
    }

    [Fact]
    public void GetNode_NonExistentId_ReturnsNull()
    {
        var graph = new SceneGraph();
        Assert.Null(graph.GetNode(Guid.NewGuid()));
    }
}
