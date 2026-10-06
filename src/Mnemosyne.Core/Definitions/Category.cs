namespace Mnemosyne.Core.Definitions;

/// <summary>The eight categories, in display order. My tasks holds the tasks the player adds by hand; it is
/// last, after Money.</summary>
public enum Category { Story, Jobs, Events, Resets, Content, Collections, Money, MyTasks }

/// <summary>A category's name as the window, the settings page and the text form show it. Each reads as its own
/// name but My tasks, whose enum name has no space. What is stored stays the enum's name: CategoriesOff, CategoryReadAt and a role's
/// "category:" key.</summary>
public static class CategoryNames
{
    public static string Label(this Category category) => category == Category.MyTasks ? "My tasks" : category.ToString();
}
