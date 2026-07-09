namespace BricsAI.Core
{
    public interface IToolPlugin
    {
        string Name { get; }
        string Description { get; }
        
        // Indicates which major version this plugin targets (e.g., 15 or 19)
        int TargetVersion { get; } 
        
        // The example JSON instruction to inject into the Main Agent's System Prompt
        // MUST be specific to the TargetVersion LISP commands.
        string GetPromptExample(); 

        // Evaluates if this specific plugin tool should handle the intercepted command
        bool CanExecute(string netCommandName);

        // Executes the native C# Interop automation logic on the active CAD drawing
        string Execute(dynamic activeDocument, string netCommandArgs);
    }
}
