namespace w_finder.Models;

/// <summary>
/// Level in the selection filter hierarchy.
/// </summary>
public enum SelectionFilterLevel
{
    Category,
    Family,
    Type,
    Instance
}

/// <summary>
/// Raw element data collected from Revit on the API thread.
/// Passed to SelectionFilterNode.Build() to construct the tree.
/// </summary>
public class ElementData
{
    public long ElementId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string InstanceLabel { get; set; } = string.Empty;
}

/// <summary>
/// A node in the Category → Family → Type → Instance selection filter tree.
/// Supports tri-state checkbox propagation (checked / unchecked / partial).
/// </summary>
public class SelectionFilterNode
{
    public string Name { get; set; } = string.Empty;
    public SelectionFilterLevel Level { get; set; }

    /// <summary>
    /// Tri-state: true = checked, false = unchecked, null = partial (mixed children).
    /// </summary>
    public bool? IsChecked { get; set; } = false;

    public SelectionFilterNode? Parent { get; set; }
    public List<SelectionFilterNode> Children { get; set; } = new();

    /// <summary>
    /// All leaf-level ElementIds under this node (aggregated at every level).
    /// </summary>
    public List<long> LeafElementIds { get; set; } = new();

    /// <summary>
    /// Total number of instances under this node.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Display label for instance-level nodes (Mark value or "#ElementId").
    /// </summary>
    public string? InstanceLabel { get; set; }

    /// <summary>
    /// Toggles the checked state and propagates to children and parents.
    /// Checked/partial → unchecked. Unchecked → checked.
    /// </summary>
    public void Toggle()
    {
        bool newState = IsChecked != true; // false or null → true, true → false
        SetChecked(newState);
    }

    /// <summary>
    /// Sets this node and all descendants to the given state, then updates parents.
    /// </summary>
    public void SetChecked(bool value)
    {
        IsChecked = value;
        PropagateDown(value);
        UpdateParent();
    }

    /// <summary>
    /// Recursively sets all children to the given checked state.
    /// </summary>
    private void PropagateDown(bool value)
    {
        foreach (var child in Children)
        {
            child.IsChecked = value;
            child.PropagateDown(value);
        }
    }

    /// <summary>
    /// Recomputes the parent's IsChecked from its children, then recurses up.
    /// All checked → true. All unchecked → false. Mixed → null (partial).
    /// </summary>
    private void UpdateParent()
    {
        if (Parent == null) return;

        bool allChecked = true;
        bool allUnchecked = true;

        foreach (var sibling in Parent.Children)
        {
            if (sibling.IsChecked != true) allChecked = false;
            if (sibling.IsChecked != false) allUnchecked = false;
        }

        if (allChecked) Parent.IsChecked = true;
        else if (allUnchecked) Parent.IsChecked = false;
        else Parent.IsChecked = null; // partial

        Parent.UpdateParent();
    }

    /// <summary>
    /// Collects all checked leaf ElementIds under this node.
    /// Optimized: fully checked returns all, fully unchecked returns none, partial recurses.
    /// </summary>
    public HashSet<long> GetCheckedLeafIds()
    {
        if (IsChecked == true)
            return new HashSet<long>(LeafElementIds);
        if (IsChecked == false)
            return new HashSet<long>();

        // Partial — recurse children
        var result = new HashSet<long>();
        foreach (var child in Children)
        {
            foreach (var id in child.GetCheckedLeafIds())
                result.Add(id);
        }
        return result;
    }

    /// <summary>
    /// Builds the Category → Family → Type → Instance tree from raw element data.
    /// All nodes start as unchecked (user opts in to what they want to keep).
    /// </summary>
    public static List<SelectionFilterNode> Build(List<ElementData> elements)
    {
        var roots = new List<SelectionFilterNode>();

        // Group by category
        var byCategory = elements.GroupBy(e => e.CategoryName).OrderBy(g => g.Key);

        foreach (var catGroup in byCategory)
        {
            var catNode = new SelectionFilterNode
            {
                Name = catGroup.Key,
                Level = SelectionFilterLevel.Category,
                IsChecked = false
            };

            // Group by family within category
            var byFamily = catGroup.GroupBy(e => e.FamilyName).OrderBy(g => g.Key);

            foreach (var famGroup in byFamily)
            {
                var famNode = new SelectionFilterNode
                {
                    Name = famGroup.Key,
                    Level = SelectionFilterLevel.Family,
                    IsChecked = false,
                    Parent = catNode
                };

                // Group by type within family
                var byType = famGroup.GroupBy(e => e.TypeName).OrderBy(g => g.Key);

                foreach (var typeGroup in byType)
                {
                    var typeNode = new SelectionFilterNode
                    {
                        Name = typeGroup.Key,
                        Level = SelectionFilterLevel.Type,
                        IsChecked = false,
                        Parent = famNode
                    };

                    // Create instance nodes (leaves)
                    foreach (var elem in typeGroup)
                    {
                        var instNode = new SelectionFilterNode
                        {
                            Name = elem.InstanceLabel,
                            Level = SelectionFilterLevel.Instance,
                            IsChecked = false,
                            Parent = typeNode,
                            InstanceLabel = elem.InstanceLabel,
                            LeafElementIds = new List<long> { elem.ElementId },
                            TotalCount = 1
                        };
                        typeNode.Children.Add(instNode);
                    }

                    // Aggregate leaf IDs and count upward
                    typeNode.LeafElementIds = typeNode.Children.SelectMany(c => c.LeafElementIds).ToList();
                    typeNode.TotalCount = typeNode.LeafElementIds.Count;
                    famNode.Children.Add(typeNode);
                }

                famNode.LeafElementIds = famNode.Children.SelectMany(c => c.LeafElementIds).ToList();
                famNode.TotalCount = famNode.LeafElementIds.Count;
                catNode.Children.Add(famNode);
            }

            catNode.LeafElementIds = catNode.Children.SelectMany(c => c.LeafElementIds).ToList();
            catNode.TotalCount = catNode.LeafElementIds.Count;
            roots.Add(catNode);
        }

        return roots;
    }
}
