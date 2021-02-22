using System.Windows.Input;

namespace RehostedWorkflowDesigner.Helpers
{
	public static class BreakpointCommands
	{
		public static ICommand CmdToggleBreakpoint = new RoutedCommand(nameof(CmdToggleBreakpoint), typeof(BreakpointCommands));
	}
}
